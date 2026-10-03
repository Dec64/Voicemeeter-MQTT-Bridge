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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace VoicemeeterMqttBridge;

public static class AppRuntimePaths
{
    public const string AppName = "Voicemeeter MQTT Bridge";

    public static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppName);

    public static string SettingsPath => Path.Combine(ConfigDir, "appsettings.json");
    public static string LogPath => Path.Combine(ConfigDir, "voicemeeter-mqtt-bridge.log");

    public static string LegacySettingsPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    public static string DemoSettingsPath => Path.Combine(AppContext.BaseDirectory, "appsettings.demo.json");

    public static void Ensure() => Directory.CreateDirectory(ConfigDir);
}

internal static class Program
{
    public static AppSettings Settings = new();
    public static BridgeService? Bridge;

    [STAThread]
    private static void Main()
    {
        InstallGlobalExceptionHandlers();
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        using var singleInstance = new Mutex(true, @"Local\VoicemeeterMqttBridge_" + AppSettings.Sanitize(Environment.UserName), out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Voicemeeter MQTT Bridge is already running in the tray. Look for the tray icon near the clock.", "Voicemeeter MQTT Bridge", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Log.Write("Application starting. Version " + typeof(Program).Assembly.GetName().Version + ".");
        Settings = AppSettings.Load();
        Log.Write($"Effective MQTT client ID: {Settings.EffectiveClientId}; base topic: {Settings.EffectiveBaseTopic}");

        Bridge = new BridgeService(Settings);
        using var tray = new TrayApp(Settings, Bridge);

        FireAndForget("Bridge startup", async () => await Bridge.StartAsync());
        Application.ApplicationExit += (_, _) =>
        {
            Log.Write("Application exiting.");
            try { Bridge?.StopAsync().GetAwaiter().GetResult(); } catch (Exception ex) { Log.Write("Stop during exit failed: " + ex); }
        };

        Application.Run();
    }

    public static void FireAndForget(string name, Func<Task> action)
    {
        _ = Task.Run(async () =>
        {
            try { await action(); }
            catch (Exception ex) { Log.Write(name + " failed: " + ex); }
        });
    }

    private static void InstallGlobalExceptionHandlers()
    {
        Application.ThreadException += (_, e) => Log.Write("UI thread exception: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("Unhandled exception: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Write("Unobserved task exception: " + e.Exception);
            e.SetObserved();
        };
    }
}

public sealed class AppSettings : IJsonOnDeserialized
{
    [JsonPropertyName("mqttHost")] public string MqttHost { get; set; } = "127.0.0.1";
    [JsonPropertyName("mqttPort")] public int MqttPort { get; set; } = 1883;
    [JsonPropertyName("mqttUsername")] public string MqttUsername { get; set; } = "";
    [JsonPropertyName("mqttPassword")] public string MqttPassword { get; set; } = "";
    [JsonPropertyName("clientId")] public string ClientId { get; set; } = "voicemeeter-{computer}";
    [JsonPropertyName("baseTopic")] public string BaseTopic { get; set; } = "voicemeeter/{computer}";
    [JsonPropertyName("homeAssistantDiscovery")] public bool HomeAssistantDiscovery { get; set; } = true;
    [JsonPropertyName("homeAssistantDiscoveryPrefix")] public string HomeAssistantDiscoveryPrefix { get; set; } = "homeassistant";
    [JsonPropertyName("publishDiscoveryOnConnect")] public bool PublishDiscoveryOnConnect { get; set; } = true;
    [JsonPropertyName("startPotatoWithApp")] public bool StartPotatoWithApp { get; set; } = true;
    [JsonPropertyName("pollIntervalMs")] public int PollIntervalMs { get; set; } = 250;
    [JsonPropertyName("controlReconcileIntervalMs")] public int ControlReconcileIntervalMs { get; set; } = 30000;
    [JsonPropertyName("publishMeters")] public bool PublishMeters { get; set; } = true;
    [JsonPropertyName("publishMetersEveryMs")] public int PublishMetersEveryMs { get; set; } = 1000;
    [JsonPropertyName("publishAllMappedControls")] public bool PublishAllMappedControls { get; set; } = true;
    [JsonPropertyName("enableStripRoutingDiscovery")] public bool EnableStripRoutingDiscovery { get; set; } = true;
    [JsonPropertyName("enableStripGainDiscovery")] public bool EnableStripGainDiscovery { get; set; } = true;
    [JsonPropertyName("enableBusDiscovery")] public bool EnableBusDiscovery { get; set; } = true;
    [JsonPropertyName("enableRecorderDiscovery")] public bool EnableRecorderDiscovery { get; set; } = true;

    private MeteringV2Settings? _meteringV2;
    [JsonPropertyName("meteringV2")]
    public MeteringV2Settings MeteringV2
    {
        get => _meteringV2 ??= new()
        {
            LegacyMetersEnabled = PublishMeters,
            LegacyMetersIntervalMs = PublishMetersEveryMs
        };
        set => _meteringV2 = value;
    }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalSettings { get; set; }

    // Resolve migration defaults after ALL legacy properties, regardless of JSON order.
    void IJsonOnDeserialized.OnDeserialized() => _ = MeteringV2;

    [JsonIgnore] public static string SettingsPath => AppRuntimePaths.SettingsPath;
    [JsonIgnore] public string ComputerName => Sanitize(Environment.MachineName);
    [JsonIgnore] public string EffectiveClientId => Expand(ClientId);
    [JsonIgnore] public string EffectiveBaseTopic => TrimTopic(Expand(BaseTopic));

    public static AppSettings Load()
    {
        try
        {
            AppRuntimePaths.Ensure();

            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions()) ?? new AppSettings();
            }

            string sourcePath = "";

            // Migration path for v1.0.0 installs/testing builds that stored settings beside the EXE.
            // This path is read-only after migration. Future saves always go to AppData.
            if (File.Exists(AppRuntimePaths.LegacySettingsPath))
            {
                sourcePath = AppRuntimePaths.LegacySettingsPath;
            }
            else if (File.Exists(AppRuntimePaths.DemoSettingsPath))
            {
                sourcePath = AppRuntimePaths.DemoSettingsPath;
            }

            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                try
                {
                    var imported = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(sourcePath), JsonOptions()) ?? new AppSettings();
                    imported.Save();
                    Log.Write("Settings created in AppData from " + sourcePath);
                    return imported;
                }
                catch (Exception importEx)
                {
                    Log.Write("Failed to import settings from " + sourcePath + ": " + importEx);
                }
            }

            var defaults = new AppSettings();
            defaults.Save();
            return defaults;
        }
        catch (Exception ex)
        {
            Log.Write("Failed to load AppData appsettings.json: " + ex);
            return new AppSettings();
        }
    }

    public void Save()
    {
        AppRuntimePaths.Ensure();
        SettingsFile.Save(this, SettingsPath);
        Log.Write("Settings saved to " + SettingsPath);
    }

    public string Expand(string value) => value.Replace("{computer}", ComputerName, StringComparison.OrdinalIgnoreCase);
    public static string TrimTopic(string value) => value.Trim().Trim('/');
    public static string Sanitize(string value)
    {
        var sb = new StringBuilder();
        foreach (char c in value.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
        return sb.ToString().Trim('_');
    }
    public static JsonSerializerOptions JsonOptions(bool indented = false) => new() { WriteIndented = indented, PropertyNameCaseInsensitive = true };
}

public static class Log
{
    private static readonly object Sync = new();
    public static string PathName => AppRuntimePaths.LogPath;
    public static void Write(string message)
    {
        try
        {
            AppRuntimePaths.Ensure();
            lock (Sync) File.AppendAllText(PathName, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}

public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "VoicemeeterMqttBridge";

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            string currentValue = key?.GetValue(AppName)?.ToString() ?? "";
            return currentValue.Contains(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        if (key == null) throw new InvalidOperationException("Unable to open Windows startup registry key.");
        if (enabled) key.SetValue(AppName, $"\"{Application.ExecutablePath}\"");
        else key.DeleteValue(AppName, false);
    }
}

public sealed class TrayApp : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly AppSettings _settings;
    private readonly BridgeService _bridge;
    private ToolStripMenuItem? _mqttStatus;
    private ToolStripMenuItem? _vmStatus;

    public TrayApp(AppSettings settings, BridgeService bridge)
    {
        _settings = settings;
        _bridge = bridge;
        _notifyIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Voicemeeter MQTT Bridge",
            Visible = true
        };
        BuildMenu();
        _notifyIcon.DoubleClick += (_, _) => ShowSettings();
    }


    private static Icon LoadTrayIcon()
    {
        try
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "app.ico");
            if (File.Exists(iconPath)) return new Icon(iconPath);
        }
        catch (Exception ex) { Log.Write("Unable to load assets/app.ico for tray icon: " + ex.Message); }

        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
        catch { return SystemIcons.Application; }
    }

    private void BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) =>
        {
            if (_mqttStatus != null) _mqttStatus.Text = "MQTT: " + _bridge.MqttStatus;
            if (_vmStatus != null) _vmStatus.Text = "Voicemeeter: " + _bridge.VoicemeeterStatus;
        };
        _mqttStatus = new ToolStripMenuItem("MQTT: Disconnected") { Enabled = false };
        _vmStatus = new ToolStripMenuItem("Voicemeeter: Unknown") { Enabled = false };
        menu.Items.Add(_mqttStatus);
        menu.Items.Add(_vmStatus);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings...", null, (_, _) => ShowSettings());
        var startWithWindowsItem = new ToolStripMenuItem("Start with Windows") { Checked = StartupManager.IsEnabled(), CheckOnClick = true };
        startWithWindowsItem.Click += (_, _) =>
        {
            try { StartupManager.SetEnabled(startWithWindowsItem.Checked); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Start with Windows", MessageBoxButtons.OK, MessageBoxIcon.Error);
                startWithWindowsItem.Checked = StartupManager.IsEnabled();
            }
        };
        menu.Items.Add(startWithWindowsItem);
        var startPotatoItem = new ToolStripMenuItem("Start Voicemeeter Potato with this app") { Checked = _settings.StartPotatoWithApp, CheckOnClick = true };
        startPotatoItem.Click += (_, _) => { _settings.StartPotatoWithApp = startPotatoItem.Checked; _settings.Save(); };
        menu.Items.Add(startPotatoItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open Log File", null, (_, _) =>
        {
            AppRuntimePaths.Ensure();
            if (!File.Exists(Log.PathName)) File.WriteAllText(Log.PathName, "");
            Process.Start(new ProcessStartInfo { FileName = Log.PathName, UseShellExecute = true });
        });
        menu.Items.Add("Open Config Folder", null, (_, _) => { AppRuntimePaths.Ensure(); Process.Start(new ProcessStartInfo { FileName = AppRuntimePaths.ConfigDir, UseShellExecute = true }); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) =>
        {
            _notifyIcon.Visible = false;
            await _bridge.StopAsync();
            Application.Exit();
        });
        _notifyIcon.ContextMenuStrip = menu;
    }

    private void ShowSettings()
    {
        using var form = new SettingsForm(_settings, _bridge);
        form.ShowDialog();
    }

    public void Dispose() => _notifyIcon.Dispose();
}

public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly BridgeService _bridge;

    private readonly TextBox _host = new();
    private readonly NumericUpDown _port = new();
    private readonly TextBox _user = new();
    private readonly TextBox _pass = new();
    private readonly TextBox _clientId = new();
    private readonly TextBox _baseTopic = new();
    private readonly TextBox _haPrefix = new();
    private readonly NumericUpDown _pollMs = new();
    private readonly NumericUpDown _meterMs = new();

    private readonly CheckBox _startWin = new();
    private readonly CheckBox _startPotato = new();
    private readonly CheckBox _haDiscovery = new();
    private readonly CheckBox _meters = new();
    private readonly CheckBox _stripGain = new();
    private readonly CheckBox _stripRouting = new();
    private readonly CheckBox _bus = new();
    private readonly CheckBox _recorder = new();

    private readonly Label _status = new();
    private MeteringV2Settings _meteringDraft = new();

    public SettingsForm(AppSettings settings, BridgeService bridge)
    {
        _settings = settings;
        _bridge = bridge;

        Text = "Voicemeeter MQTT Bridge Settings";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Width = 760;
        Height = 700;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9);

        _meteringDraft = SettingsEditor.CloneMetering(settings.MeteringV2);
        BuildUi();
        LoadValues();
    }

    private void BuildUi()
    {
        Controls.Add(new Label
        {
            Text = "Voicemeeter MQTT Bridge",
            Left = 22,
            Top = 16,
            Width = 700,
            Height = 32,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 30, 30)
        });

        var mqtt = MakeGroup("MQTT Connection", 22, 58, 700, 185);
        AddLabel(mqtt, "Server", 18, 32); AddText(mqtt, _host, 105, 28, 390, "10.13.37.101");
        AddLabel(mqtt, "Port", 520, 32); AddNumber(mqtt, _port, 605, 28, 65, 1, 65535);
        AddLabel(mqtt, "Username", 18, 72); AddText(mqtt, _user, 105, 68, 245, "optional");
        AddLabel(mqtt, "Password", 380, 72); AddText(mqtt, _pass, 465, 68, 205, "optional"); _pass.UseSystemPasswordChar = true;
        AddLabel(mqtt, "Client ID", 18, 112); AddText(mqtt, _clientId, 105, 108, 565, "voicemeeter-{computer}");
        AddLabel(mqtt, "Base Topic", 18, 148); AddText(mqtt, _baseTopic, 105, 144, 565, "voicemeeter/{computer}");

        var startup = MakeGroup("Startup", 22, 252, 700, 76);
        AddCheck(startup, _startWin, "Start with Windows", 18, 30, 270);
        AddCheck(startup, _startPotato, "Start Voicemeeter Potato with this app", 350, 30, 315);

        var features = MakeGroup("Publishing / Home Assistant", 22, 338, 700, 124);
        AddCheck(features, _haDiscovery, "Home Assistant discovery", 18, 30, 220);
        AddLabel(features, "Prefix", 250, 32); AddText(features, _haPrefix, 360, 28, 310, "homeassistant");
        AddCheck(features, _meters, "Publish meters", 18, 68, 160);
        AddLabel(features, "Meter ms", 200, 70); AddNumber(features, _meterMs, 300, 66, 95, 250, 60000);
        AddLabel(features, "Poll ms", 430, 70); AddNumber(features, _pollMs, 525, 66, 95, 100, 10000);

        var controls = MakeGroup("Mapped Controls", 22, 472, 700, 64);
        AddCheck(controls, _stripGain, "Strip gain/mute", 18, 28, 150);
        AddCheck(controls, _stripRouting, "Routing", 190, 28, 100);
        AddCheck(controls, _bus, "Buses", 315, 28, 90);
        AddCheck(controls, _recorder, "Recorder", 430, 28, 110);

        _status.Left = 22;
        _status.Top = 552;
        _status.Width = 410;
        _status.Height = 25;
        _status.Text = "Status: " + _bridge.MqttStatus + " / " + _bridge.VoicemeeterStatus;
        Controls.Add(_status);

        var test = new Button { Text = "Test MQTT", Left = 438, Top = 546, Width = 100, Height = 34 };
        test.Click += async (_, _) => await TestMqttAsync();
        var save = new Button { Text = "Save", Left = 548, Top = 546, Width = 80, Height = 34 };
        save.Click += async (_, _) => {
            try { await SaveAsync(); }
            catch (Exception error) { _status.Text = error is ArgumentException ? error.Message : "Settings could not be saved: " + error.GetType().Name; }
        };
        var cancel = new Button { Text = "Cancel", Left = 638, Top = 546, Width = 80, Height = 34 };
        cancel.Click += (_, _) => Close();
        Controls.Add(test);
        Controls.Add(save);
        Controls.Add(cancel);

        var advanced = new Button { Text = "Advanced metering / sources...", Left = 22, Top = 590, Width = 260, Height = 34 };
        advanced.Click += (_, _) => { var draft = SettingsEditor.Clone(_settings); draft.MeteringV2 = _meteringDraft; using var form = new MeteringSettingsForm(draft, _bridge); if (form.ShowDialog(this) == DialogResult.OK) _meteringDraft = form.Result!; };
        Controls.Add(advanced);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private GroupBox MakeGroup(string text, int left, int top, int width, int height)
    {
        var group = new GroupBox { Text = text, Left = left, Top = top, Width = width, Height = height, BackColor = Color.White };
        Controls.Add(group);
        return group;
    }

    private static void AddLabel(Control parent, string text, int left, int top) => parent.Controls.Add(new Label { Text = text, Left = left, Top = top, Width = 80, Height = 24 });

    private static void AddText(Control parent, TextBox box, int left, int top, int width, string placeholder)
    {
        box.Left = left;
        box.Top = top;
        box.Width = width;
        box.Height = 26;
        box.PlaceholderText = placeholder;
        parent.Controls.Add(box);
    }

    private static void AddNumber(Control parent, NumericUpDown box, int left, int top, int width, int min, int max)
    {
        box.Left = left;
        box.Top = top;
        box.Width = width;
        box.Minimum = min;
        box.Maximum = max;
        parent.Controls.Add(box);
    }

    private static void AddCheck(Control parent, CheckBox box, string text, int left, int top, int width)
    {
        box.Text = text;
        box.Left = left;
        box.Top = top;
        box.Width = width;
        box.Height = 24;
        parent.Controls.Add(box);
    }

    private void LoadValues()
    {
        _host.Text = _settings.MqttHost;
        _port.Value = Math.Clamp(_settings.MqttPort, 1, 65535);
        _user.Text = _settings.MqttUsername;
        _pass.Text = _settings.MqttPassword;
        _clientId.Text = _settings.ClientId;
        _baseTopic.Text = _settings.BaseTopic;
        _pollMs.Value = Math.Clamp(_settings.PollIntervalMs, 100, 10000);
        _meters.Checked = _settings.PublishMeters;
        _meterMs.Value = Math.Clamp(_settings.PublishMetersEveryMs, 250, 60000);
        _startWin.Checked = StartupManager.IsEnabled();
        _startPotato.Checked = _settings.StartPotatoWithApp;
        _haDiscovery.Checked = _settings.HomeAssistantDiscovery;
        _haPrefix.Text = _settings.HomeAssistantDiscoveryPrefix;
        _stripGain.Checked = _settings.EnableStripGainDiscovery;
        _stripRouting.Checked = _settings.EnableStripRoutingDiscovery;
        _bus.Checked = _settings.EnableBusDiscovery;
        _recorder.Checked = _settings.EnableRecorderDiscovery;
    }

    private AppSettings ReadForm()
    {
        var draft = SettingsEditor.Clone(_settings);
        draft.MeteringV2 = SettingsEditor.CloneMetering(_meteringDraft);

        draft.MqttHost = _host.Text.Trim();
        draft.MqttPort = (int)_port.Value;
        draft.MqttUsername = _user.Text.Trim();
        draft.MqttPassword = _pass.Text;
        draft.ClientId = string.IsNullOrWhiteSpace(_clientId.Text) ? "voicemeeter-{computer}" : _clientId.Text.Trim();
        draft.BaseTopic = string.IsNullOrWhiteSpace(_baseTopic.Text) ? "voicemeeter/{computer}" : _baseTopic.Text.Trim();
        draft.PollIntervalMs = (int)_pollMs.Value;
        draft.PublishMeters = _meters.Checked;
        draft.PublishMetersEveryMs = (int)_meterMs.Value;
        draft.StartPotatoWithApp = _startPotato.Checked;
        draft.HomeAssistantDiscovery = _haDiscovery.Checked;
        draft.HomeAssistantDiscoveryPrefix = string.IsNullOrWhiteSpace(_haPrefix.Text) ? "homeassistant" : _haPrefix.Text.Trim().Trim('/');
        draft.EnableStripGainDiscovery = _stripGain.Checked;
        draft.EnableStripRoutingDiscovery = _stripRouting.Checked;
        draft.EnableBusDiscovery = _bus.Checked;
        draft.EnableRecorderDiscovery = _recorder.Checked;
        return draft;
    }

    private async Task TestMqttAsync()
    {
        _status.Text = "Status: testing MQTT...";
        bool ok = await MqttBridge.TestConnectionAsync(ReadForm(), TimeSpan.FromSeconds(5));
        _status.Text = ok ? "Status: MQTT test succeeded" : "Status: MQTT test failed";
        MessageBox.Show(ok ? "MQTT connection succeeded." : "MQTT connection failed.", "MQTT Test", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private Task SaveAsync()
    {
        var s = ReadForm();
        s.MeteringV2.Validate();
        s.Save(); // Persist the complete draft before changing the active in-memory settings.
        _settings.MeteringV2 = s.MeteringV2;
        _settings.MqttHost = s.MqttHost;
        _settings.MqttPort = s.MqttPort;
        _settings.MqttUsername = s.MqttUsername;
        _settings.MqttPassword = s.MqttPassword;
        _settings.ClientId = s.ClientId;
        _settings.BaseTopic = s.BaseTopic;
        _settings.PollIntervalMs = s.PollIntervalMs;
        _settings.PublishMeters = s.PublishMeters;
        _settings.PublishMetersEveryMs = s.PublishMetersEveryMs;
        _settings.StartPotatoWithApp = s.StartPotatoWithApp;
        _settings.HomeAssistantDiscovery = s.HomeAssistantDiscovery;
        _settings.HomeAssistantDiscoveryPrefix = s.HomeAssistantDiscoveryPrefix;
        _settings.EnableStripGainDiscovery = s.EnableStripGainDiscovery;
        _settings.EnableStripRoutingDiscovery = s.EnableStripRoutingDiscovery;
        _settings.EnableBusDiscovery = s.EnableBusDiscovery;
        _settings.EnableRecorderDiscovery = s.EnableRecorderDiscovery;
        StartupManager.SetEnabled(_startWin.Checked);

        Program.FireAndForget("Settings reconnect", async () => await _bridge.ReconnectAsync());

        MessageBox.Show("Settings saved. MQTT reconnect started. Restart the bridge to apply v2 metering and discovery changes.", "Voicemeeter MQTT Bridge", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Close();
        return Task.CompletedTask;
    }
}

public sealed class GenericSetCommand
{
    [JsonPropertyName("parameter")] public string Parameter { get; set; } = "";
    [JsonPropertyName("value")] public float Value { get; set; }
}

public enum VmControlKind { Number, Switch }

public sealed class VmControl
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Parameter { get; init; }
    public required VmControlKind Kind { get; init; }
    public float Min { get; init; } = 0;
    public float Max { get; init; } = 1;
    public float Step { get; init; } = 1;
    public string Icon { get; init; } = "";
    public bool Discover { get; init; } = true;

    public static List<VmControl> BuildPotatoControls(AppSettings s)
    {
        var list = new List<VmControl>();
        for (int i = 0; i < 8; i++)
        {
            string name = i < 5 ? $"Hardware Input {i + 1}" : $"Virtual Input {i - 4}";
            if (s.EnableStripGainDiscovery)
            {
                list.Add(Number($"strip_{i}_gain", $"{name} Gain", $"Strip[{i}].Gain", -60, 12, 0.1f, "mdi:volume-high"));
                list.Add(Switch($"strip_{i}_mute", $"{name} Mute", $"Strip[{i}].Mute", "mdi:volume-mute"));
                list.Add(Switch($"strip_{i}_solo", $"{name} Solo", $"Strip[{i}].Solo", "mdi:account-voice"));
                list.Add(Number($"strip_{i}_comp", $"{name} Compressor", $"Strip[{i}].Comp", 0, 10, 0.1f, "mdi:compress"));
                list.Add(Number($"strip_{i}_gate", $"{name} Gate", $"Strip[{i}].Gate", 0, 10, 0.1f, "mdi:gate"));
            }
            if (s.EnableStripRoutingDiscovery)
            {
                foreach (string bus in new[] { "A1", "A2", "A3", "A4", "A5", "B1", "B2", "B3" })
                    list.Add(Switch($"strip_{i}_{bus.ToLowerInvariant()}", $"{name} to {bus}", $"Strip[{i}].{bus}", "mdi:audio-input-rca"));
            }
        }
        if (s.EnableBusDiscovery)
        {
            for (int i = 0; i < 8; i++)
            {
                string name = i < 5 ? $"Bus A{i + 1}" : $"Bus B{i - 4}";
                list.Add(Number($"bus_{i}_gain", $"{name} Gain", $"Bus[{i}].Gain", -60, 12, 0.1f, "mdi:volume-high"));
                list.Add(Switch($"bus_{i}_mute", $"{name} Mute", $"Bus[{i}].Mute", "mdi:volume-mute"));
                list.Add(Switch($"bus_{i}_mono", $"{name} Mono", $"Bus[{i}].Mono", "mdi:mono"));
                list.Add(Switch($"bus_{i}_eq_on", $"{name} EQ On", $"Bus[{i}].EQ.on", "mdi:equalizer"));
            }
        }
        if (s.EnableRecorderDiscovery)
        {
            list.Add(Switch("recorder_record", "Recorder Record", "Recorder.Record", "mdi:record-rec"));
            list.Add(Switch("recorder_play", "Recorder Play", "Recorder.Play", "mdi:play"));
            list.Add(Switch("recorder_stop", "Recorder Stop", "Recorder.Stop", "mdi:stop"));
        }
        return list;
    }
    private static VmControl Number(string id, string name, string parameter, float min, float max, float step, string icon) => new() { Id = id, Name = name, Parameter = parameter, Kind = VmControlKind.Number, Min = min, Max = max, Step = step, Icon = icon };
    private static VmControl Switch(string id, string name, string parameter, string icon) => new() { Id = id, Name = name, Parameter = parameter, Kind = VmControlKind.Switch, Icon = icon };
}

public sealed class MqttBridge
{
    private readonly AppSettings _settings;
    private readonly BridgeService _bridge;
    private readonly IMqttClient _client;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly SemaphoreSlim _setupLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private long _connectionEpoch;
    private long _readyEpoch = -1;
    private int _birthPending;
    internal IMqttClient Client => _client;
    internal long ConnectionEpoch => Interlocked.Read(ref _connectionEpoch);
    internal bool TelemetryReady => !_lifetime.IsCancellationRequested && _client.IsConnected &&
        Interlocked.Read(ref _readyEpoch) == ConnectionEpoch;
    private bool IsCurrent(long epoch) => !_lifetime.IsCancellationRequested && _client.IsConnected && epoch == ConnectionEpoch;
    private void RevokeReadiness() { Interlocked.Exchange(ref _readyEpoch, -1); Interlocked.Increment(ref _connectionEpoch); }
    private volatile bool _manualDisconnect;
    private int _connectAttempt;
    public string StatusText { get; private set; } = "Disconnected";

    public MqttBridge(AppSettings settings, BridgeService bridge, IMqttClient? client = null,
        Action<string>? log = null)
    {
        _settings = settings;
        _bridge = bridge;
        _log = log ?? Log.Write;
        _client = client ?? new MqttFactory().CreateMqttClient();

        _client.ConnectedAsync += e =>
        {
            RevokeReadiness();
            long epoch = ConnectionEpoch;
            _connectAttempt = 0;
            StatusText = $"Connected to {_settings.MqttHost}:{_settings.MqttPort}";
            _log($"MQTT connected. ClientId={_settings.EffectiveClientId}; Result={e.ConnectResult.ResultCode}; AssignedClientId={e.ConnectResult.AssignedClientIdentifier}");

            // Do the heavier subscribe/discovery/state work outside the MQTTnet
            // event callback. This prevents a publish/subscription exception from
            // bubbling through ConnectedAsync and causing an immediate disconnect.
            Program.FireAndForget("MQTT post-connect setup", () => PostConnectSetupAsync(epoch));
            return Task.CompletedTask;
        };

        _client.DisconnectedAsync += async e =>
        {
            RevokeReadiness();
            await _bridge.SuspendTelemetryAsync();
            StatusText = "Disconnected: " + e.Reason;
            _log($"MQTT disconnected: Reason={e.Reason}; ReasonString={e.ReasonString}; ClientWasConnected={e.ClientWasConnected}; Exception={e.Exception}");

            if (_manualDisconnect)
            {
                _log("MQTT disconnect was intentional; automatic reconnect suppressed.");
                return;
            }

            int delaySeconds = Math.Min(30, 3 + (++_connectAttempt * 2));
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), _lifetime.Token);
                await ConnectLoopAsync();
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        };

        _client.ApplicationMessageReceivedAsync += async e =>
        {
            try
            {
                string payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
                string topic = e.ApplicationMessage.Topic;
                _log($"MQTT RX {topic}");
                if (topic.Equals(_settings.HomeAssistantDiscoveryPrefix.Trim('/') + "/status", StringComparison.OrdinalIgnoreCase) && payload.Trim().Equals("online", StringComparison.OrdinalIgnoreCase))
                {
                    // QoS 1 telemetry sends must not wait inside the MQTT receive callback.
                    if (Interlocked.CompareExchange(ref _birthPending, 1, 0) == 0)
                    {
                        long epoch = ConnectionEpoch;
                        Program.FireAndForget("HA birth refresh", () => RefreshHomeAssistantAsync(epoch));
                    }
                    return;
                }
                await _bridge.HandleMqttCommandAsync(topic, payload, e.ApplicationMessage.Retain);
            }
            catch (Exception ex)
            {
                _log("MQTT receive handler failed: " + ex);
            }
        };
    }

    private async Task RefreshHomeAssistantAsync(long epoch)
    {
        bool acquired = false;
        try
        {
            await _setupLock.WaitAsync(_lifetime.Token); acquired = true;
            if (!IsCurrent(epoch)) return;
            RevokeReadiness(); epoch = ConnectionEpoch;
            await _bridge.SuspendTelemetryAsync();
            if (!IsCurrent(epoch)) return;
            if (_settings.HomeAssistantDiscovery) await _bridge.PublishDiscoveryAsync();
            if (!IsCurrent(epoch)) return;
            await _bridge.InitializeAdvancedAsync();
            // A new v2 session republishes retained metadata and optional slow discovery once.
            if (IsCurrent(epoch)) Interlocked.Exchange(ref _readyEpoch, epoch);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { _log("HA birth refresh failed: " + ex.GetType().Name); }
        finally
        {
            if (acquired) _setupLock.Release();
            Interlocked.Exchange(ref _birthPending, 0);
        }
    }

    private async Task PostConnectSetupAsync(long epoch)
    {
        bool acquired = false;
        try
        {
            await _setupLock.WaitAsync(_lifetime.Token); acquired = true;
            if (!IsCurrent(epoch)) return;
            await PublishAvailabilityAsync(true);
            await _client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(_settings.EffectiveBaseTopic + "/set").WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build());
            _log("MQTT subscribed: " + _settings.EffectiveBaseTopic + "/set");
            await _client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(_settings.EffectiveBaseTopic + "/parameter/+/set").WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build());
            _log("MQTT subscribed: " + _settings.EffectiveBaseTopic + "/parameter/+/set");
            await _client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(_settings.HomeAssistantDiscoveryPrefix.Trim('/') + "/status").Build());
            _log("MQTT subscribed: " + _settings.HomeAssistantDiscoveryPrefix.Trim('/') + "/status");

            if (!IsCurrent(epoch)) return;
            if (_settings.HomeAssistantDiscovery && _settings.PublishDiscoveryOnConnect) await _bridge.PublishDiscoveryAsync();
            if (!IsCurrent(epoch)) return;
            await _client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(_settings.EffectiveBaseTopic + "/v2/parameter/+/set").WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build());
            await _bridge.InitializeAdvancedAsync();
            await _bridge.PublishAllStateAsync();
            if (IsCurrent(epoch)) Interlocked.Exchange(ref _readyEpoch, epoch);
            _log("MQTT post-connect setup complete.");
        }
        catch (Exception ex)
        {
            _log("MQTT post-connect setup failed: " + ex);
        }
        finally { if (acquired) _setupLock.Release(); }
    }

    public async Task StartAsync() => await ConnectLoopAsync();

    public async Task StopAsync()
    {
        _manualDisconnect = true;
        _lifetime.Cancel();
        RevokeReadiness();
        await _bridge.SuspendTelemetryAsync();
        await _connectLock.WaitAsync(); _connectLock.Release();
        await _setupLock.WaitAsync(); _setupLock.Release();
        try
        {
            if (_client.IsConnected)
            {
                await PublishAvailabilityAsync(false);
                await _client.DisconnectAsync();
            }
        }
        catch (Exception ex) { _log("MQTT StopAsync failed: " + ex); }
    }

    public async Task ReconnectAsync()
    {
        if (_lifetime.IsCancellationRequested) return;
        RevokeReadiness();
        await _bridge.SuspendTelemetryAsync();
        _log("MQTT reconnect requested.");
        _manualDisconnect = true;
        try
        {
            if (_client.IsConnected) await _client.DisconnectAsync();
        }
        catch (Exception ex)
        {
            _log("MQTT disconnect during reconnect failed: " + ex);
        }
        finally
        {
            _manualDisconnect = false;
        }

        await ConnectLoopAsync();
    }

    private async Task ConnectLoopAsync()
    {
        if (!await _connectLock.WaitAsync(0))
        {
            _log("MQTT connect loop already running; duplicate request ignored.");
            return;
        }
        try
        {
            while (!_client.IsConnected && !_manualDisconnect && !_lifetime.IsCancellationRequested)
            {
                try
                {
                    StatusText = $"Connecting to {_settings.MqttHost}:{_settings.MqttPort}";
                    _log($"MQTT connecting. Host={_settings.MqttHost}; Port={_settings.MqttPort}; ClientId={_settings.EffectiveClientId}");
                    var builder = new MqttClientOptionsBuilder()
                        .WithClientId(_settings.EffectiveClientId)
                        .WithTcpServer(_settings.MqttHost, _settings.MqttPort)
                        .WithCleanSession()
                        .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                        .WithTimeout(TimeSpan.FromSeconds(10))
                        .WithWillTopic(_settings.EffectiveBaseTopic + "/availability")
                        .WithWillPayload("offline")
                        .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                        .WithWillRetain(true);
                    if (!string.IsNullOrWhiteSpace(_settings.MqttUsername)) builder.WithCredentials(_settings.MqttUsername, _settings.MqttPassword);
                    await _client.ConnectAsync(builder.Build(), _lifetime.Token);
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    StatusText = "Connection failed: " + ex.Message;
                    _log("MQTT connection failed: " + ex);
                    int delaySeconds = Math.Min(30, 3 + (++_connectAttempt * 2));
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), _lifetime.Token);
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { _connectLock.Release(); }
    }

    public async Task PublishAvailabilityAsync(bool online) => await PublishRawAsync(_settings.EffectiveBaseTopic + "/availability", online ? "online" : "offline", retain: true);

    public async Task<bool> PublishStateAsync(VmControl c, float value)
    {
        string payload = c.Kind == VmControlKind.Switch ? (Math.Abs(value) > 0.5 ? "ON" : "OFF") : value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return await PublishRawAsync(StateTopic(c), payload, retain: true);
    }

    public async Task<bool> PublishRawAsync(string topic, string payload, bool retain)
    {
        if (!_client.IsConnected) return false;
        try
        {
            var msg = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                .WithRetainFlag(retain)
                .Build();
            var result = await _client.PublishAsync(msg);
            if (result.IsSuccess) return true;
            _log($"MQTT publish failed. Topic={topic}; Reason={result.ReasonCode}");
        }
        catch (Exception ex)
        {
            _log($"MQTT publish failed. Topic={topic}; Retain={retain}; Error={ex}");
        }
        return false;
    }

    public async Task PublishDiscoveryAsync(List<VmControl> controls)
    {
        if (!_settings.HomeAssistantDiscovery || !_client.IsConnected) return;
        int count = 0;
        foreach (VmControl c in controls.Where(x => x.Discover))
        {
            string component = c.Kind == VmControlKind.Switch ? "switch" : "number";
            string configTopic = $"{_settings.HomeAssistantDiscoveryPrefix.Trim('/')}/{component}/voicemeeter_{_settings.ComputerName}_{c.Id}/config";
            var payload = new Dictionary<string, object?>
            {
                ["name"] = c.Name,
                ["unique_id"] = $"voicemeeter_{_settings.ComputerName}_{c.Id}",
                ["command_topic"] = CommandTopic(c),
                ["state_topic"] = StateTopic(c),
                ["availability_topic"] = _settings.EffectiveBaseTopic + "/availability",
                ["payload_available"] = "online",
                ["payload_not_available"] = "offline",
                ["icon"] = string.IsNullOrWhiteSpace(c.Icon) ? null : c.Icon,
                ["device"] = new Dictionary<string, object?>
                {
                    ["identifiers"] = new[] { "voicemeeter_mqtt_bridge_" + _settings.ComputerName },
                    ["name"] = "Voicemeeter " + Environment.MachineName,
                    ["manufacturer"] = "VB-Audio",
                    ["model"] = "Voicemeeter Potato MQTT Bridge",
                    ["sw_version"] = "1.0.1"
                }
            };
            if (c.Kind == VmControlKind.Switch)
            {
                payload["payload_on"] = "ON";
                payload["payload_off"] = "OFF";
            }
            else
            {
                payload["min"] = c.Min;
                payload["max"] = c.Max;
                payload["step"] = c.Step;
                payload["mode"] = "slider";
            }
            string json = JsonSerializer.Serialize(payload.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value), AppSettings.JsonOptions(true));
            await PublishRawAsync(configTopic, json, retain: true);
            count++;
            if (count % 20 == 0) await Task.Delay(50); // be gentle with HA/Mosquitto during retained discovery bursts
        }
        if (_settings.MeteringV2.Enabled ? _settings.MeteringV2.LegacyMetersEnabled : _settings.PublishMeters)
        {
            count += await PublishMeterDiscoveryAsync();
        }

        _log($"Home Assistant discovery published. Entities={count}");
    }

    private async Task<int> PublishMeterDiscoveryAsync()
    {
        int count = 0;
        // Home Assistant cannot use the raw JSON meter blob by itself, so expose common meters as MQTT sensors with value_template.
        // Compatibility only: these IDs select raw channels, NOT logical strips/buses.
        // Correct combined source meters must use new v2 IDs and topics (see docs/COMPATIBILITY-AUDIT.md).
        for (int i = 0; i < 8; i++)
        {
            await PublishMeterSensorDiscoveryAsync($"meter_in_{i}", $"Input Meter {i + 1}", $"in_{i}");
            count++;
        }

        for (int i = 0; i < 8; i++)
        {
            await PublishMeterSensorDiscoveryAsync($"meter_out_{i}", $"Bus Meter {i + 1}", $"out_{i}");
            count++;
        }

        return count;
    }

    private async Task PublishMeterSensorDiscoveryAsync(string id, string name, string jsonKey)
    {
        string configTopic = $"{_settings.HomeAssistantDiscoveryPrefix.Trim('/')}/sensor/voicemeeter_{_settings.ComputerName}_{id}/config";
        var payload = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["unique_id"] = $"voicemeeter_{_settings.ComputerName}_{id}",
            ["state_topic"] = _settings.EffectiveBaseTopic + "/meters",
            ["availability_topic"] = _settings.EffectiveBaseTopic + "/availability",
            ["payload_available"] = "online",
            ["payload_not_available"] = "offline",
            ["value_template"] = "{{ value_json." + jsonKey + " | default(0) }}",
            ["state_class"] = "measurement",
            ["icon"] = "mdi:volume-vibrate",
            ["device"] = new Dictionary<string, object?>
            {
                ["identifiers"] = new[] { "voicemeeter_mqtt_bridge_" + _settings.ComputerName },
                ["name"] = "Voicemeeter " + Environment.MachineName,
                ["manufacturer"] = "VB-Audio",
                ["model"] = "Voicemeeter Potato MQTT Bridge",
                ["sw_version"] = "1.0.1"
            }
        };
        string json = JsonSerializer.Serialize(payload.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value), AppSettings.JsonOptions(true));
        await PublishRawAsync(configTopic, json, retain: true);
    }

    private string StateTopic(VmControl c) => _settings.EffectiveBaseTopic + "/parameter/" + c.Id + "/state";
    private string CommandTopic(VmControl c) => _settings.EffectiveBaseTopic + "/parameter/" + c.Id + "/set";

    public static async Task<bool> TestConnectionAsync(AppSettings settings, TimeSpan timeout)
    {
        var factory = new MqttFactory();
        using IMqttClient testClient = factory.CreateMqttClient();
        var builder = new MqttClientOptionsBuilder()
            .WithClientId(settings.EffectiveClientId + "-test-" + Guid.NewGuid().ToString("N")[..6])
            .WithTcpServer(settings.MqttHost, settings.MqttPort)
            .WithCleanSession()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithTimeout(timeout);
        if (!string.IsNullOrWhiteSpace(settings.MqttUsername)) builder.WithCredentials(settings.MqttUsername, settings.MqttPassword);
        using var cts = new CancellationTokenSource(timeout);
        try { await testClient.ConnectAsync(builder.Build(), cts.Token); await testClient.DisconnectAsync(); return true; }
        catch (Exception ex) { Log.Write("MQTT test connection failed: " + ex); return false; }
    }
}

public sealed class VoicemeeterRemote : IVoicemeeterRemote, IVoicemeeterMetadata
{
    private readonly Action<string> _log;
    public VoicemeeterRemote(Action<string>? log = null) => _log = log ?? Log.Write;
    private IntPtr _lib;
    public bool IsLoaded => _lib != IntPtr.Zero;

    private LoginDelegate? _login;
    private LoginDelegate? _logout;
    private RunVoicemeeterDelegate? _run;
    private IsParametersDirtyDelegate? _dirty;
    private GetParameterFloatDelegate? _getFloat;
    private SetParameterFloatDelegate? _setFloat;
    private GetLevelDelegate? _getLevel;
    private GetTypeDelegate? _getType;
    private GetVersionDelegate? _getVersion;
    private GetStringDelegate? _getString;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int LoginDelegate();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int RunVoicemeeterDelegate(int voicemeeterType);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int IsParametersDirtyDelegate();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetParameterFloatDelegate([MarshalAs(UnmanagedType.LPStr)] string param, ref float value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetParameterFloatDelegate([MarshalAs(UnmanagedType.LPStr)] string param, float value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetLevelDelegate(int type, int channel, ref float value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetTypeDelegate(ref int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetVersionDelegate(ref int value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetStringDelegate([MarshalAs(UnmanagedType.LPStr)] string parameter, [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 512)] ushort[] value);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibrary(string fileName);
    [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)] private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    public void Load()
    {
        if (IsLoaded) return;
        string? dll = FindRemoteDll();
        if (dll == null) throw new FileNotFoundException("Could not find VoicemeeterRemote64.dll. Install Voicemeeter Potato or copy the DLL beside this EXE.");
        _lib = LoadLibrary(dll);
        if (_lib == IntPtr.Zero) throw new InvalidOperationException("LoadLibrary failed for " + dll + ": Win32 " + Marshal.GetLastWin32Error());
        _login = Get<LoginDelegate>("VBVMR_Login");
        _logout = Get<LoginDelegate>("VBVMR_Logout");
        _run = Get<RunVoicemeeterDelegate>("VBVMR_RunVoicemeeter");
        _dirty = Get<IsParametersDirtyDelegate>("VBVMR_IsParametersDirty");
        _getFloat = Get<GetParameterFloatDelegate>("VBVMR_GetParameterFloat");
        _setFloat = Get<SetParameterFloatDelegate>("VBVMR_SetParameterFloat");
        _getLevel = Get<GetLevelDelegate>("VBVMR_GetLevel");
        _getType = Get<GetTypeDelegate>("VBVMR_GetVoicemeeterType");
        _getVersion = Get<GetVersionDelegate>("VBVMR_GetVoicemeeterVersion");
        IntPtr labelExport = GetProcAddress(_lib, "VBVMR_GetParameterStringW");
        if (labelExport != IntPtr.Zero) _getString = Marshal.GetDelegateForFunctionPointer<GetStringDelegate>(labelExport);
        _log("Loaded Voicemeeter Remote DLL: " + dll);
    }

    private T Get<T>(string name) where T : Delegate
    {
        IntPtr p = GetProcAddress(_lib, name);
        if (p == IntPtr.Zero) throw new MissingMethodException("Voicemeeter Remote DLL is missing export " + name);
        return Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    public int Login() { Load(); return _login!(); }
    public int Logout() => _logout?.Invoke() ?? 0;
    public int RunVoicemeeter(int type) { Load(); return _run!(type); }
    public int IsParametersDirty() => _dirty?.Invoke() ?? throw new InvalidOperationException("Remote API is not loaded.");
    public float GetParameterFloat(string parameter)
    {
        float value = 0;
        int rc = _getFloat!(parameter, ref value);
        if (rc != 0) throw new InvalidOperationException($"GetParameterFloat({parameter}) returned {rc}");
        return value;
    }
    public int SetParameterFloat(string parameter, float value) => _setFloat!(parameter, value);
    public float GetLevel(int type, int channel)
    {
        float value = 0;
        int rc = _getLevel!(type, channel, ref value);
        if (rc != 0) throw new InvalidOperationException($"GetLevel({type},{channel}) returned {rc}");
        return value;
    }
    public int GetVoicemeeterType()
    {
        if (_getType is null) throw new InvalidOperationException("Remote API is not loaded.");
        int value = 0, result = _getType(ref value);
        if (result != 0) throw new InvalidOperationException($"GetVoicemeeterType returned {result}.");
        return value;
    }
    public int GetVoicemeeterVersion()
    {
        if (_getVersion is null) throw new InvalidOperationException("Remote API is not loaded.");
        int value = 0, result = _getVersion(ref value);
        if (result != 0) throw new InvalidOperationException($"GetVoicemeeterVersion returned {result}.");
        return value;
    }

    public EngineIdentity GetEngineIdentity()
    {
        int type = GetVoicemeeterType(), packed = GetVoicemeeterVersion();
        return new(type, new Version((packed >> 24) & 255, (packed >> 16) & 255,
            (packed >> 8) & 255, packed & 255));
    }

    // SDK: ASCII parameter name, 512 UTF-16 code units, stdcall/32-bit status.
    public string? GetLabel(SourceKind kind, int index)
    {
        _ = PotatoChannelMap.Get(kind, index);
        if (_getString is null) return null;
        var buffer = new ushort[512];
        string parameter = (kind == SourceKind.Strip ? "Strip" : "Bus") + "[" + index + "].Label";
        int result = _getString(parameter, buffer);
        if (result != 0) throw new InvalidOperationException("Label read failed: " + result);
        int length = Array.IndexOf(buffer, (ushort)0);
        if (length < 0) throw new InvalidOperationException("Unterminated native label.");
        string label = new(buffer.Take(length).Select(c => (char)c).ToArray());
        return label.Any(char.IsControl) ? null : label;
    }

    public static IEnumerable<string> PotatoExeCandidates()
    {
        string[] bases =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };
        foreach (string b in bases.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            yield return Path.Combine(b, "VB", "Voicemeeter", "voicemeeter8x64.exe");
            yield return Path.Combine(b, "VB", "Voicemeeter", "voicemeeter8.exe");
        }
    }

    private static string? FindRemoteDll()
    {
        string env = Environment.GetEnvironmentVariable("VOICEMEETER_REMOTE_DLL") ?? "";
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(env)) candidates.Add(env);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "VoicemeeterRemote64.dll"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "VoicemeeterRemote.dll"));
        string[] bases =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };
        foreach (string b in bases.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            candidates.Add(Path.Combine(b, "VB", "Voicemeeter", "VoicemeeterRemote64.dll"));
            candidates.Add(Path.Combine(b, "VB", "Voicemeeter", "VoicemeeterRemote.dll"));
        }
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\VB-Audio\Voicemeeter");
            string install = key?.GetValue("InstallPath")?.ToString() ?? "";
            if (!string.IsNullOrWhiteSpace(install)) candidates.Add(Path.Combine(install, "VoicemeeterRemote64.dll"));
        }
        catch { }
        return candidates.FirstOrDefault(File.Exists);
    }
}
