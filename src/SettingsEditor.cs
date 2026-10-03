// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
using System.Text.Json;
namespace VoicemeeterMqttBridge;
public static class SettingsEditor
{
    public static AppSettings Clone(AppSettings settings) => JsonSerializer.Deserialize<AppSettings>(
        JsonSerializer.Serialize(settings, AppSettings.JsonOptions()), AppSettings.JsonOptions())!;
    public static MeteringV2Settings CloneMetering(MeteringV2Settings settings) => JsonSerializer.Deserialize<MeteringV2Settings>(
        JsonSerializer.Serialize(settings, AppSettings.JsonOptions()), AppSettings.JsonOptions())!;
    public static string ExportMetering(MeteringV2Settings settings)
    {
        settings.Validate();
        // Explicit projection: future extension fields may contain sensitive data.
        return JsonSerializer.Serialize(new { meteringV2 = new {
            settings.AdvancedDiscoveryGroups, settings.Enabled, settings.FastEnabled, settings.SlowEnabled, settings.SampleIntervalMs,
            settings.FastPublishIntervalMs, settings.SlowPublishIntervalMs, settings.LegacyMetersEnabled,
            settings.LegacyMetersIntervalMs, settings.DisplayFloorDbfs, settings.ActivityThresholdDbfs,
            settings.ClipThresholdDbfs, settings.ActivityHysteresisDb, settings.ClipHysteresisDb,
            settings.ActivityHoldMs, settings.ClipHoldMs, settings.HistorySeconds,
            sources = settings.Sources.Select(s => new { s.Id, s.Enabled, s.Alias, s.DisplayLabel, s.MeterTaps })
        }}, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
    }
}
