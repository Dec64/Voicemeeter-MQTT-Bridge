// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
using System.Text.Json;

namespace VoicemeeterMqttBridge;

/// <summary>
/// Pure v2 serialization for supplied window samples. No timer, native calls or MQTT.
/// One future telemetry owner must supply each window, gate enable flags and own this builder.
/// Activity/clip are observed-window threshold flags; hysteresis/holds remain runtime work.
/// </summary>
public sealed class AggregateFrameBuilder
{
    private readonly SourceRegistry _registry;
    private readonly double _floor;
    private readonly double _activityThreshold;
    private readonly double _clipThreshold;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private long _sequence;

    public AggregateFrameBuilder(SourceRegistry registry, MeteringV2Settings settings)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        _registry = registry;
        _floor = settings.DisplayFloorDbfs;
        _activityThreshold = settings.ActivityThresholdDbfs;
        _clipThreshold = settings.ClipThresholdDbfs;
    }

    public string BuildFastFrame(IEnumerable<SourcePeak> samples, int sampleWindowMs, DateTimeOffset publishedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (sampleWindowMs <= 0) throw new ArgumentOutOfRangeException(nameof(sampleWindowMs));
        var enabled = _registry.Sources.Where(s => s.Enabled).ToDictionary(s => s.Id, StringComparer.Ordinal);
        var grouped = samples.ToLookup(s => (s.SourceId, s.Tap));
        foreach (var group in grouped)
            if (!enabled.TryGetValue(group.Key.SourceId, out var owner) || !owner.MeterTaps.Contains(group.Key.Tap))
                throw new ArgumentException("Sample does not belong to an enabled source/tap.", nameof(samples));

        var sources = new Dictionary<string, Dictionary<string, object?>>();
        foreach (var source in enabled.Values)
        {
            var levels = new Dictionary<MeterTap, double>();
            foreach (var tap in source.MeterTaps)
            {
                bool seen = false, valid = true;
                float peak = 0;
                foreach (var sample in grouped[(source.Id, tap)])
                {
                    seen = true;
                    if (!sample.Available || sample.LinearPeak is not float value || !float.IsFinite(value) || value < 0)
                        valid = false;
                    else peak = Math.Max(peak, value);
                }
                if (seen && valid) levels[tap] = PeakMath.ToDbfs(peak, _floor);
            }
            bool available = levels.Count == source.MeterTaps.Count;
            var entry = new Dictionary<string, object?> { ["available"] = available };
            foreach (var tap in source.MeterTaps)
                entry[MeterTapNames.Format(tap) + "_dbfs"] = available ? levels[tap] : null;
            var activityTap = ActivityTap(source);
            entry["active"] = available ? levels[activityTap] > _activityThreshold : null;
            entry["clipping"] = available ? levels[activityTap] >= _clipThreshold : null;
            sources.Add(source.Id, entry);
        }
        long next = checked(_sequence + 1); // Restart the telemetry session before exhaustion; never wrap silently.
        string json = JsonSerializer.Serialize(new
        {
            schema = 2, session_id = _sessionId, seq = _sequence,
            published_at_utc = publishedAtUtc.ToUniversalTime(), sample_window_ms = sampleWindowMs, sources
        });
        _sequence = next;
        return json;
    }

    public string BuildMetadata(string bridgeVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bridgeVersion);
        return JsonSerializer.Serialize(new
        {
            schema = 2, session_id = _sessionId, bridge_version = bridgeVersion,
            engine = "potato", engine_version = _registry.Engine.Version.ToString(),
            sources = _registry.Sources.Select(s => new
            {
                id = s.Id, kind = s.Kind == SourceKind.Strip ? "strip" : "bus", index = s.Index,
                engine_label = s.EngineLabel, label = s.DisplayLabel, alias = s.Alias, enabled = s.Enabled,
                channels = s.Channels, taps = s.MeterTaps.Select(MeterTapNames.Format),
                activity_tap = MeterTapNames.Format(ActivityTap(s)), capability_groups = s.CapabilityGroups
            })
        });
    }

    private static MeterTap ActivityTap(SourceDescriptor source) => source.Kind == SourceKind.Bus ? MeterTap.Output
        : source.MeterTaps.Contains(MeterTap.PreFader) ? MeterTap.PreFader : source.MeterTaps[0];
}
