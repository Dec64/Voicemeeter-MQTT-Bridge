// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoicemeeterMqttBridge;

public sealed class MeteringV2Settings
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("sampleIntervalMs")] public int SampleIntervalMs { get; set; } = 50;
    [JsonPropertyName("fastEnabled")] public bool FastEnabled { get; set; }
    [JsonPropertyName("fastPublishIntervalMs")] public int FastPublishIntervalMs { get; set; } = 50;
    [JsonPropertyName("slowEnabled")] public bool SlowEnabled { get; set; } = true;
    [JsonPropertyName("slowPublishIntervalMs")] public int SlowPublishIntervalMs { get; set; } = 1000;
    [JsonPropertyName("legacyMetersEnabled")] public bool LegacyMetersEnabled { get; set; } = true;
    [JsonPropertyName("legacyMetersIntervalMs")] public int LegacyMetersIntervalMs { get; set; } = 1000;
    [JsonPropertyName("activityThresholdDbfs")] public double ActivityThresholdDbfs { get; set; } = -48;
    [JsonPropertyName("displayFloorDbfs")] public double DisplayFloorDbfs { get; set; } = -90;
    [JsonPropertyName("clipThresholdDbfs")] public double ClipThresholdDbfs { get; set; } = -0.1;
    [JsonPropertyName("historySeconds")] public int HistorySeconds { get; set; } = 5;
    [JsonPropertyName("sources")] public List<SourceProfile> Sources { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalSettings { get; set; }

    public void Validate()
    {
        Require(SampleIntervalMs is >= 10 and <= 1000, "sampleIntervalMs must be 10–1000.");
        Require(FastPublishIntervalMs is >= 50 and <= 5000 && FastPublishIntervalMs >= SampleIntervalMs,
            "fastPublishIntervalMs must be 50–5000 and at least sampleIntervalMs.");
        Require(SlowPublishIntervalMs is >= 250 and <= 60000 && SlowPublishIntervalMs >= SampleIntervalMs,
            "slowPublishIntervalMs must be 250–60000 and at least sampleIntervalMs.");
        Require(LegacyMetersIntervalMs > 0, "legacyMetersIntervalMs must be positive.");
        Require(double.IsFinite(DisplayFloorDbfs) && DisplayFloorDbfs < 0, "displayFloorDbfs must be finite and negative.");
        Require(double.IsFinite(ActivityThresholdDbfs) && ActivityThresholdDbfs > DisplayFloorDbfs && ActivityThresholdDbfs <= 0,
            "activityThresholdDbfs must be above the display floor and at most 0.");
        Require(double.IsFinite(ClipThresholdDbfs) && ClipThresholdDbfs > ActivityThresholdDbfs && ClipThresholdDbfs <= 0,
            "clipThresholdDbfs must be above activityThresholdDbfs and at most 0.");
        Require(HistorySeconds is >= 1 and <= 60, "historySeconds must be 1–60.");
        Require(Sources is not null && Sources.Count <= 16, "sources must contain at most 16 entries.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in Sources!)
        {
            Require(source is not null, "sources cannot contain null.");
            source!.Validate();
            Require(ids.Add(source.Id), "Duplicate canonical source ID.");
            if (!string.IsNullOrEmpty(source.Alias)) Require(aliases.Add(source.Alias), "Duplicate source alias.");
        }
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new ArgumentException(message);
    }
}

public sealed class SourceProfile
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("alias")] public string? Alias { get; set; }
    [JsonPropertyName("displayLabel")] public string? DisplayLabel { get; set; }
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("meterTaps")] public List<string>? MeterTaps { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalSettings { get; set; }

    public IReadOnlyList<MeterTap> ResolveTaps()
    {
        var source = PotatoChannelMap.Get(Id);
        var taps = MeterTaps is null
            ? source.Kind == SourceKind.Bus ? new[] { MeterTap.Output } : new[] { MeterTap.PreFader, MeterTap.PostMute }
            : MeterTaps.Select(MeterTapNames.Parse).ToArray();
        MeteringV2Settings.Require(taps.Length > 0 && taps.Distinct().Count() == taps.Length, "Select distinct meter taps.");
        MeteringV2Settings.Require(taps.All(t => source.Kind == SourceKind.Bus ? t == MeterTap.Output : t != MeterTap.Output),
            "The meter tap is incompatible with the source kind.");
        return Array.AsReadOnly(taps);
    }

    internal void Validate()
    {
        _ = PotatoChannelMap.Get(Id);
        MeteringV2Settings.Require(Alias is null || Alias.Length == 0 ||
            (Alias.Length <= 64 && Alias.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')),
            "alias must use up to 64 ASCII letters, digits, underscores or hyphens.");
        MeteringV2Settings.Require(DisplayLabel is null ||
            (DisplayLabel.Length <= 511 && !DisplayLabel.Any(char.IsControl)), "Invalid display label.");
        _ = ResolveTaps();
    }
}

public static class MeterTapNames
{
    public static string Format(MeterTap tap) => tap switch
    {
        MeterTap.PreFader => "pre", MeterTap.PostFader => "post_fader",
        MeterTap.PostMute => "post_mute", MeterTap.Output => "output",
        _ => throw new ArgumentOutOfRangeException(nameof(tap))
    };

    public static MeterTap Parse(string name) => name switch
    {
        "pre" => MeterTap.PreFader, "post_fader" => MeterTap.PostFader,
        "post_mute" => MeterTap.PostMute, "output" => MeterTap.Output,
        _ => throw new ArgumentException("Unknown meter tap.", nameof(name))
    };
}
