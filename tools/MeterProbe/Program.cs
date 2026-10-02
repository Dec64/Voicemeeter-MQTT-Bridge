using System.Text.Json;
using System.Text.Json.Serialization;
using VoicemeeterMqttBridge;

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
