// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
using System.Globalization;
using System.Text.Json;
namespace VoicemeeterMqttBridge;
public sealed class AdvancedControlService(AppSettings settings, IVoicemeeterRemote remote, MqttBridge mqtt)
{
    private readonly Dictionary<string, AdvancedControl> _supported = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _sent = new(StringComparer.Ordinal);
    public IReadOnlyList<AdvancedControl> Supported => _supported.Values.ToArray();
    public async Task InitializeAsync()
    {
        _supported.Clear(); _sent.Clear();
        if (!settings.MeteringV2.Enabled || settings.MeteringV2.AdvancedDiscoveryGroups.Count == 0) return;
        settings.MeteringV2.Validate();
        var metadata = remote as IVoicemeeterMetadata;
        if (metadata is null) return;
        var engine = metadata.GetEngineIdentity();
        if (engine.Type != 3 || engine.Version.Major != 3 || engine.Version < new Version(3,1,0,1)) return;
        foreach (var source in settings.MeteringV2.Sources.Where(s => s.Enabled))
            foreach (var entry in AdvancedControlRegistry.Probe(AdvancedControlRegistry.Build(source.Id, settings.MeteringV2.AdvancedDiscoveryGroups), remote))
                _supported.Add(entry.Id, entry);
        if (settings.HomeAssistantDiscovery)
        {
            int count = 0;
            foreach (var control in _supported.Values)
            {
                var payload = new Dictionary<string, object?> {
                    ["name"] = control.SourceId + " " + control.Name, ["unique_id"] = control.UniqueId(settings.ComputerName),
                    ["command_topic"] = Topic(control) + "/set", ["state_topic"] = Topic(control) + "/state",
                    ["availability_mode"] = "all", ["availability"] = new[] {
                        new { topic = settings.EffectiveBaseTopic + "/availability", payload_available = "online", payload_not_available = "offline" },
                        new { topic = Topic(control) + "/availability", payload_available = "online", payload_not_available = "offline" } },
                    ["device"] = new { identifiers = new[] { "voicemeeter_mqtt_bridge_" + settings.ComputerName }, name = "Voicemeeter " + settings.ComputerName },
                    ["entity_category"] = "config", ["qos"] = 1, ["retain"] = false };
                if (control.Switch) { payload["payload_on"] = "ON"; payload["payload_off"] = "OFF"; }
                else { payload["min"] = control.Min; payload["max"] = control.Max; payload["step"] = control.Step; payload["mode"] = "box"; if (control.Unit is not null) payload["unit_of_measurement"] = control.Unit; }
                await mqtt.PublishRawAsync(settings.HomeAssistantDiscoveryPrefix.Trim('/') + "/" + (control.Switch ? "switch" : "number") + "/" + control.UniqueId(settings.ComputerName) + "/config", JsonSerializer.Serialize(payload), true);
                if (++count % 20 == 0) await Task.Delay(50);
            }
        }
        await PollAsync(true);
    }
    private string Topic(AdvancedControl control) => settings.EffectiveBaseTopic + "/v2/parameter/" + control.Id;
    public async Task PollAsync(bool force = false)
    {
        foreach (var control in _supported.Values) await ReadAsync(control, force);
    }
    private async Task<float?> ReadAsync(AdvancedControl control, bool force)
    {
        try {
            float value = remote.GetParameterFloat(control.Parameter);
            if (!control.Accepts(value)) throw new InvalidOperationException("Invalid readback");
            if (force || !_sent.TryGetValue(control.Id, out float old) || Math.Abs(value-old) > control.Step/2)
            {
                string payload = control.Switch ? value == 1 ? "ON" : "OFF" : value.ToString("0.###", CultureInfo.InvariantCulture);
                if (await mqtt.PublishRawAsync(Topic(control)+"/state", payload, true)) {
                    if (await mqtt.PublishRawAsync(Topic(control)+"/availability", "online", true)) _sent[control.Id] = value;
                }
            }
            return value;
        } catch (InvalidOperationException) { _sent.Remove(control.Id); await mqtt.PublishRawAsync(Topic(control)+"/availability", "offline", true); return null; }
    }
    public async Task CommandAsync(string id, string payload, bool retained)
    {
        if (!_supported.TryGetValue(id, out var control)) return;
        string? error = retained ? "retained_command_rejected" : null; float value = 0; float? readback = null;
        if (error is null && control.Switch) { if (payload.Trim() == "ON") value=1; else if (payload.Trim() == "OFF") value=0; else error="invalid_value"; }
        else if (error is null && !float.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) error="invalid_value";
        if (error is null && !control.Accepts(value)) error="out_of_range";
        if (error is null) {
            try { if (remote.SetParameterFloat(control.Parameter,value) != 0) error="set_failed";
                else { readback = await ReadAsync(control,true); if (readback is null) error="readback_failed"; } }
            catch (InvalidOperationException) { error="remote_unavailable"; }
        }
        await mqtt.PublishRawAsync(Topic(control)+"/result", JsonSerializer.Serialize(new { schema=2, id, success=error is null, error, readback }), false);
    }
}