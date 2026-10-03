using System.Text.Json;
using System.Text.Json.Serialization;
using VoicemeeterMqttBridge;

if (args.Length == 1 && args[0] is "--read-labels" or "--read-capabilities")
{
    using var owner = new RemoteApiOwner(new VoicemeeterRemote(_ => { }), _ => { });
    owner.Load();
    if (owner.Login() is not (0 or 1)) return 2;
    var settings = new MeteringV2Settings();
    if (args[0] == "--read-capabilities") {
        settings.AdvancedDiscoveryGroups = AdvancedControlRegistry.Groups.ToList();
        settings.Sources = PotatoChannelMap.All.Select(s => new SourceProfile { Id = s.Id, Enabled = true }).ToList();
    }
    var registry = SourceRegistry.Read(owner, settings);
    Console.WriteLine(JsonSerializer.Serialize(new { engine = registry.Engine, sources = registry.Sources.Select(s => new { id = s.Id, label = s.EngineLabel, controls = s.Controls.Select(c => new { c.Id, c.Parameter, c.Group }) }) }));
    return 0;
}
if (args.Length != 1 || args[0] != "--read-live")
{
    Console.Error.WriteLine("Usage: MeterProbe --read-live\nReads 20 passes of live Potato meters. No control writes, app launch, settings or MQTT.");
    return 1;
}
try
{
    // All native calls execute synchronously on this thread. Suppress the bridge's AppData log.
    var remote = new VoicemeeterRemote(_ => { });
    var report = ReadOnlyMeterProbe.Capture(remote, remote.GetEngineIdentity);
    var json = new JsonSerializerOptions { WriteIndented = true };
    json.Converters.Add(new JsonStringEnumConverter());
    Console.WriteLine(JsonSerializer.Serialize(report, json));
    return report.Frames.Any(frame => frame.Peaks.Any(peak => !peak.Available)) ? 2 : 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("Meter probe failed: " + error.Message);
    return 2;
}
