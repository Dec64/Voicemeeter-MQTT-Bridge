// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
using System.Text.Json;

namespace VoicemeeterMqttBridge;

/// <summary>
/// Pure v2 serialization for completed timed snapshots. No timer, native calls or MQTT.
/// One future telemetry owner must supply each window, gate enable flags and own this builder.
/// Activity/clip come from the accumulator; they are not inferred from window maxima.
/// </summary>
public sealed class AggregateFrameBuilder
{
    private readonly SourceRegistry _registry;
    private readonly double _floor;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private long _sequence;

    public AggregateFrameBuilder(SourceRegistry registry, MeteringV2Settings settings)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        _registry = registry;
        _floor = settings.DisplayFloorDbfs;
    }

    public string BuildFastFrame(MeterWindowSnapshot snapshot, int sampleWindowMs, DateTimeOffset publishedAtUtc)
        => BuildFrame(snapshot, sampleWindowMs, publishedAtUtc, slow: false);

    public string BuildSlowFrame(MeterWindowSnapshot snapshot, int sampleWindowMs, DateTimeOffset publishedAtUtc)
        => BuildFrame(snapshot, sampleWindowMs, publishedAtUtc, slow: true);

    private string BuildFrame(MeterWindowSnapshot snapshot, int sampleWindowMs, DateTimeOffset publishedAtUtc, bool slow)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Readings);
        if (snapshot.Duration <= TimeSpan.Zero) throw new ArgumentException("Snapshot duration must be positive.", nameof(snapshot));
        if (sampleWindowMs <= 0) throw new ArgumentOutOfRangeException(nameof(sampleWindowMs));
        var enabled = _registry.Sources.Where(s => s.Enabled).ToDictionary(s => s.Id, StringComparer.Ordinal);
        var readings = new Dictionary<(string, MeterTap), MeterWindowReading>();
        foreach (var reading in snapshot.Readings)
        {
            if (reading?.Peak is not SourcePeak peak || peak.SourceId is null ||
                !enabled.TryGetValue(peak.SourceId, out var owner) || !owner.MeterTaps.Contains(peak.Tap))
                throw new ArgumentException("Reading does not belong to an enabled source/tap.", nameof(snapshot));
            if (!readings.TryAdd((peak.SourceId, peak.Tap), reading))
                throw new ArgumentException("Snapshot contains a duplicate source/tap.", nameof(snapshot));
        }

        var sources = new Dictionary<string, Dictionary<string, object?>>();
        foreach (var source in enabled.Values)
        {
            var levels = new Dictionary<MeterTap, double>();
            foreach (var tap in source.MeterTaps)
            {
                if (readings.TryGetValue((source.Id, tap), out var reading) && reading.Peak.Available &&
                    reading.Peak.LinearPeak is float value && float.IsFinite(value) && value >= 0 &&
                    reading.Active.HasValue && reading.Clipping.HasValue)
                    levels[tap] = PeakMath.ToDbfs(value, _floor);
            }
            bool available = levels.Count == source.MeterTaps.Count;
            var entry = new Dictionary<string, object?> { ["available"] = available };
            foreach (var tap in source.MeterTaps)
                entry[MeterTapNames.Format(tap) + "_dbfs"] = available ? levels[tap] : null;
            var activityTap = ActivityTap(source);
            if (slow) entry["sensor_tap"] = MeterTapNames.Format(activityTap);
            entry["active"] = available ? readings[(source.Id, activityTap)].Active : null;
            entry["clipping"] = available ? readings[(source.Id, activityTap)].Clipping : null;
            sources.Add(source.Id, entry);
        }
        long next = checked(_sequence + 1); // Restart the telemetry session before exhaustion; never wrap silently.
        string json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["schema"] = 2, ["session_id"] = _sessionId, ["seq"] = _sequence,
            ["published_at_utc"] = publishedAtUtc.ToUniversalTime(),
            [slow ? "window_ms" : "sample_window_ms"] = sampleWindowMs, ["sources"] = sources
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
