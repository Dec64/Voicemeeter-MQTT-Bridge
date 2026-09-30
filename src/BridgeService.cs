// Voicemeeter MQTT Bridge - Windows tray bridge between VB-Audio Voicemeeter Potato and MQTT.
// Copyright (C) 2026 Richard Cornwell <rcp@techtoknow.net>
//
// This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty
// of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License along with this program. If not, see <https://www.gnu.org/licenses/>.

using System.Diagnostics;
using System.Text.Json;
using MQTTnet.Client;

namespace VoicemeeterMqttBridge;

public sealed class BridgeService
{
    private readonly AppSettings _settings;
    private readonly IVoicemeeterRemote _vm;
    private readonly MqttBridge _mqtt;
    private readonly Action<string> _log;
    private readonly List<VmControl> _controls;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _controlLock = new(1, 1);
    private readonly Dictionary<string, float> _publishedControls = new();
    private readonly HashSet<string> _failedControlReads = new();
    private readonly TimeProvider _time;
    private long? _lastControlScan;
    private bool _controlConnectionLost;
    private DateTime _lastMeterPublish = DateTime.MinValue;

    public string MqttStatus => _mqtt.StatusText;
    public string VoicemeeterStatus { get; private set; } = "Not connected";
    public int ControlReadErrorCount => _failedControlReads.Count;
    public string? LastCommandError { get; private set; }

    public BridgeService(AppSettings settings, IVoicemeeterRemote? remote = null,
        IMqttClient? mqttClient = null, Action<string>? log = null, TimeProvider? timeProvider = null)
    {
        _settings = settings;
        _vm = remote ?? new VoicemeeterRemote();
        _log = log ?? Log.Write;
        _time = timeProvider ?? TimeProvider.System;
        _controls = VmControl.BuildPotatoControls(settings);
        _mqtt = new MqttBridge(settings, this, mqttClient, _log);
    }

    public async Task StartAsync()
    {
        await EnsureVoicemeeterAsync();
        await _mqtt.StartAsync();
        _ = Task.Run(PollLoopAsync);
    }

    public async Task StopAsync()
    {
        try { await _mqtt.PublishAvailabilityAsync(false); } catch { }
        _cts.Cancel();
        try { await _mqtt.StopAsync(); } catch { }
        try { _vm.Logout(); } catch { }
    }

    public async Task ReconnectAsync()
    {
        // PostConnectSetupAsync owns discovery and the forced snapshot for every connection.
        await _mqtt.ReconnectAsync();
    }

    private async Task EnsureVoicemeeterAsync()
    {
        try
        {
            _vm.Load();
            int login = _vm.Login();
            if (login != 0 && _settings.StartPotatoWithApp)
            {
                VoicemeeterStatus = "Starting Potato...";
                Log.Write("Login returned " + login + "; attempting to start Voicemeeter Potato.");
                try { _vm.RunVoicemeeter(3); } catch (Exception ex) { Log.Write("RunVoicemeeter(3) failed: " + ex.Message); }
                StartPotatoExeFallback();
                await Task.Delay(3000);
                login = _vm.Login();
            }
            VoicemeeterStatus = login == 0 ? "Connected" : "Login failed: " + login;
            Log.Write("Voicemeeter status: " + VoicemeeterStatus);
        }
        catch (Exception ex)
        {
            VoicemeeterStatus = "Error: " + ex.Message;
            Log.Write("Voicemeeter init failed: " + ex);
        }
    }

    private static void StartPotatoExeFallback()
    {
        if (Process.GetProcessesByName("voicemeeter8x64").Any() || Process.GetProcessesByName("voicemeeter8").Any()) return;
        foreach (string path in VoicemeeterRemote.PotatoExeCandidates())
        {
            try
            {
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                    Log.Write("Started Potato EXE: " + path);
                    return;
                }
            }
            catch (Exception ex) { Log.Write("Failed to start Potato EXE " + path + ": " + ex.Message); }
        }
    }

    private async Task PollLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                if (!_vm.IsLoaded) await EnsureVoicemeeterAsync();
                await PollControlStateAsync();

                if (_settings.PublishMeters && (DateTime.UtcNow - _lastMeterPublish).TotalMilliseconds >= _settings.PublishMetersEveryMs)
                {
                    _lastMeterPublish = DateTime.UtcNow;
                    await PublishMetersAsync();
                }
            }
            catch (Exception ex)
            {
                VoicemeeterStatus = "Poll error: " + ex.Message;
                Log.Write("Poll loop error: " + ex);
            }
            await Task.Delay(Math.Clamp(_settings.PollIntervalMs, 100, 10000), _cts.Token).ContinueWith(_ => { });
        }
    }

    public async Task PollControlStateAsync()
    {
        await _controlLock.WaitAsync();
        try
        {
            int dirty = _vm.IsParametersDirty();
            if (dirty < 0)
            {
                _controlConnectionLost = true;
                SetControlStatus(dirty == -2 ? "Engine unavailable" : $"Remote API dirty error: {dirty}");
                return;
            }

            bool reconcile = _settings.ControlReconcileIntervalMs > 0 &&
                (_lastControlScan is null || _time.GetElapsedTime(_lastControlScan.Value).TotalMilliseconds >=
                    Math.Clamp(_settings.ControlReconcileIntervalMs, 1000, 3600000));
            if (dirty > 0 || reconcile || _controlConnectionLost)
                await ReadControlsAsync(force: _controlConnectionLost);
            _controlConnectionLost = false;
            SetControlStatus("Connected");
        }
        catch (Exception ex)
        {
            _controlConnectionLost = true;
            SetControlStatus("Remote API poll error: " + ex.Message);
        }
        finally { _controlLock.Release(); }
    }

    private void SetControlStatus(string status)
    {
        if (VoicemeeterStatus == status) return;
        VoicemeeterStatus = status;
        _log("Voicemeeter status: " + status);
    }

    public async Task HandleMqttCommandAsync(string topic, string payload)
    {
        try
        {
            string baseTopic = _settings.EffectiveBaseTopic;
            if (topic.Equals(baseTopic + "/set", StringComparison.OrdinalIgnoreCase))
            {
                var cmd = JsonSerializer.Deserialize<GenericSetCommand>(payload, AppSettings.JsonOptions());
                if (cmd != null && !string.IsNullOrWhiteSpace(cmd.Parameter))
                {
                    await SetParameterAsync(cmd.Parameter, cmd.Value, publish: true);
                    return;
                }
            }

            if (topic.StartsWith(baseTopic + "/parameter/", StringComparison.OrdinalIgnoreCase) && topic.EndsWith("/set", StringComparison.OrdinalIgnoreCase))
            {
                string id = topic[(baseTopic.Length + "/parameter/".Length)..^"/set".Length].Trim('/');
                VmControl? control = _controls.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (control == null)
                {
                    _log("No control mapped for id: " + id);
                    return;
                }
                float value = control.Kind == VmControlKind.Switch ? PayloadToBoolFloat(payload) : PayloadToFloat(payload);
                await SetParameterAsync(control.Parameter, value, publish: true);
            }
        }
        catch (Exception ex)
        {
            LastCommandError = ex.Message;
            _log("Handle MQTT command failed: " + ex);
        }
    }

    public async Task SetParameterAsync(string parameter, float value, bool publish)
    {
        await _controlLock.WaitAsync();
        try
        {
            if (!float.IsFinite(value)) throw new ArgumentException("Control value must be finite.");
            int rc = _vm.SetParameterFloat(parameter, value);
            _log($"Set {parameter}={value} rc={rc}");
            if (rc != 0) throw new InvalidOperationException($"Set {parameter} failed: rc={rc}");
            LastCommandError = null;
            VmControl? control = FindControl(parameter);
            if (publish && control != null) await ReadControlAsync(control, force: true);
        }
        catch (Exception ex)
        {
            LastCommandError = ex.Message;
            _log("Control command failed: " + ex.Message);
        }
        finally { _controlLock.Release(); }
    }

    public async Task PublishParameterStateAsync(string parameter)
    {
        await _controlLock.WaitAsync();
        try
        {
            VmControl? control = FindControl(parameter);
            if (control != null) await ReadControlAsync(control, force: true);
        }
        finally { _controlLock.Release(); }
    }

    private VmControl? FindControl(string parameter) => _controls.FirstOrDefault(
        c => c.Parameter.Equals(parameter, StringComparison.OrdinalIgnoreCase));

    public async Task PublishAllStateAsync()
    {
        await _controlLock.WaitAsync();
        try { await ReadControlsAsync(force: true); }
        finally { _controlLock.Release(); }
    }

    private async Task ReadControlsAsync(bool force)
    {
        _lastControlScan = _time.GetTimestamp();
        foreach (var control in _controls) await ReadControlAsync(control, force);
    }

    private async Task ReadControlAsync(VmControl control, bool force)
    {
        float value;
        try
        {
            value = _vm.GetParameterFloat(control.Parameter);
            if (!float.IsFinite(value)) throw new InvalidOperationException("Non-finite readback");
        }
        catch (Exception ex)
        {
            _publishedControls.Remove(control.Id);
            if (_failedControlReads.Add(control.Id))
                _log($"Control read failed: {control.Parameter}: {ex.Message}");
            return;
        }
        if (_failedControlReads.Remove(control.Id)) _log("Control read recovered: " + control.Parameter);

        if (!force && _publishedControls.TryGetValue(control.Id, out float previous) &&
            (control.Kind == VmControlKind.Switch
                ? (Math.Abs(previous) > 0.5) == (Math.Abs(value) > 0.5)
                : Math.Abs(previous - value) < control.Step / 2)) return;

        // QoS 0 completion confirms a local send, not broker/subscriber acknowledgement.
        if (await _mqtt.PublishStateAsync(control, value)) _publishedControls[control.Id] = value;
        else _publishedControls.Remove(control.Id);
    }

    public async Task PublishMetersAsync()
    {
        var doc = new Dictionary<string, float>();
        // Voicemeeter Remote exposes linear peak levels. Type 0 is pre-fader input, type 3 is output bus in common examples.
        for (int ch = 0; ch < 64; ch++)
        {
            try
            {
                float v = _vm.GetLevel(0, ch);
                if (!float.IsNaN(v) && v > -200) doc[$"in_{ch}"] = v;
            }
            catch { break; }
        }
        for (int ch = 0; ch < 64; ch++)
        {
            try
            {
                float v = _vm.GetLevel(3, ch);
                if (!float.IsNaN(v) && v > -200) doc[$"out_{ch}"] = v;
            }
            catch { break; }
        }
        await _mqtt.PublishRawAsync(_settings.EffectiveBaseTopic + "/meters", JsonSerializer.Serialize(doc), retain: false);
    }

    public async Task PublishDiscoveryAsync() => await _mqtt.PublishDiscoveryAsync(_controls);

    private static float PayloadToFloat(string payload)
    {
        payload = payload.Trim();
        if (payload.StartsWith("{"))
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("value", out var v)) return v.GetSingle();
        }
        return float.Parse(payload, System.Globalization.CultureInfo.InvariantCulture);
    }
    private static float PayloadToBoolFloat(string payload)
    {
        string p = payload.Trim().Trim('"').ToLowerInvariant();
        if (p is "on" or "true" or "1") return 1;
        if (p is "off" or "false" or "0") return 0;
        float value = PayloadToFloat(payload);
        if (!float.IsFinite(value)) throw new ArgumentException("Control value must be finite.");
        return value != 0 ? 1 : 0;
    }
}
