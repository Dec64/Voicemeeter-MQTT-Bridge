// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
using System.Text.Json;
using MQTTnet.Client;

namespace VoicemeeterMqttBridge;

/// <summary>Owns successive v2 sessions, draining one before starting its replacement.</summary>
public sealed class MeterTelemetryRuntime
{
    private readonly AppSettings _settings;
    private readonly RemoteApiOwner _remote;
    private readonly IMqttClient _client;
    private readonly Func<bool> _ready;
    private readonly Func<long> _connectionEpoch;
    private readonly Action<string> _log;
    private readonly TimeProvider _time;
    private int _started;
    private string _status = "Not started";
    public string Status => Volatile.Read(ref _status);

    public MeterTelemetryRuntime(AppSettings settings, RemoteApiOwner remote, IMqttClient client,
        Func<bool> ready, Func<long> connectionEpoch, Action<string> log, TimeProvider? time = null)
    {
        _settings = settings; _remote = remote; _client = client;
        _ready = ready; _connectionEpoch = connectionEpoch; _log = log; _time = time ?? TimeProvider.System;
    }

    public async Task RunAsync(CancellationToken token)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Runtime already started.");
        try
        {
            // Capture a private configuration once. UI settings changes restart the BridgeService.
            if (!_settings.MeteringV2.Enabled) { SetStatus("Disabled"); return; }
            var settings = JsonSerializer.Deserialize<MeteringV2Settings>(JsonSerializer.Serialize(_settings.MeteringV2))!;
            settings.Validate();
            if ((!settings.FastEnabled && !settings.SlowEnabled) || !settings.Sources.Any(s => s.Enabled))
            { SetStatus("No enabled streams/sources"); return; }
            while (!token.IsCancellationRequested)
            {
                if (!_ready() || !_client.IsConnected)
                {
                    SetStatus("Waiting for connection");
                    await Task.Delay(TimeSpan.FromMilliseconds(250), _time, token);
                    continue;
                }
                bool failed = false;
                try
                {
                    long epoch = _connectionEpoch();
                    var registry = SourceRegistry.Read(_remote, settings);
                    token.ThrowIfCancellationRequested();
                    if (!_ready() || !_client.IsConnected || epoch != _connectionEpoch()) continue;
                    using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
                    var supervisor = new MeterTelemetrySupervisor(_client, _remote, _time);
                    var session = supervisor.RunAsync(registry, settings, _settings.EffectiveBaseTopic, "1.0.1-dev-v2",
                        TimeSpan.FromMilliseconds(Math.Max(750, settings.FastPublishIntervalMs * 3)),
                        TimeSpan.FromMilliseconds(Math.Max(15000, settings.SlowPublishIntervalMs * 3)), stop.Token,
                        discovery: _settings.HomeAssistantDiscovery ? new SlowDiscoveryOptions(_settings.ComputerName,
                            _settings.HomeAssistantDiscoveryPrefix, Math.Max(15, settings.SlowPublishIntervalMs / 1000 * 3)) : null,
                        status: new TelemetryStatusOptions(TimeSpan.FromSeconds(2)));
                    SetStatus("Session active");
                    try
                    {
                        while (!session.IsCompleted && !token.IsCancellationRequested && _ready() &&
                            _client.IsConnected && epoch == _connectionEpoch())
                            await Task.WhenAny(session, Task.Delay(TimeSpan.FromMilliseconds(250), _time, stop.Token));
                    }
                    finally
                    {
                        stop.Cancel();
                        await session; // Never abandon a native call or in-flight send during replacement.
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    failed = true;
                    SetStatus("Retrying after " + ex.GetType().Name);
                }
                if (failed) await Task.Delay(TimeSpan.FromSeconds(5), _time, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { SetStatus("Disabled: " + ex.GetType().Name); }
        finally { if (token.IsCancellationRequested) SetStatus("Stopped"); }
    }

    private void SetStatus(string status)
    {
        if (Status == status) return;
        Volatile.Write(ref _status, status);
        try { _log("V2 telemetry: " + status); } catch { }
    }
}
