// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
namespace VoicemeeterMqttBridge;

public sealed record MeterSamplingDiagnostics(
    [property: System.Text.Json.Serialization.JsonPropertyName("api_read_count")] long ApiReadCount,
    [property: System.Text.Json.Serialization.JsonPropertyName("invalid_read_count")] long InvalidReadCount);

/// <summary>A single source/tap reading, not a v2 MQTT wire frame or timed peak window.</summary>
public sealed record SourcePeak(string SourceId, MeterTap Tap, bool Available, float? LinearPeak, double? Dbfs);

public static class PeakMath
{
    public static double ToDbfs(float linearPeak, double floorDbfs = -90)
    {
        ValidateFloor(floorDbfs);
        if (!float.IsFinite(linearPeak) || linearPeak < 0) throw new ArgumentOutOfRangeException(nameof(linearPeak));
        return linearPeak == 0 ? floorDbfs : Math.Max(floorDbfs, 20 * Math.Log10(linearPeak));
    }

    internal static void ValidateFloor(double floorDbfs)
    {
        if (!double.IsFinite(floorDbfs) || floorDbfs > 0) throw new ArgumentOutOfRangeException(nameof(floorDbfs));
    }
}

/// <summary>
/// Synchronous testable sampling primitive, not yet connected to the running bridge.
/// A future runtime caller must verify Potato and own GetLevel calls on one thread.
/// </summary>
public sealed class SourcePeakSampler
{
    private readonly IVoicemeeterLevels _remote;
    private readonly double _floorDbfs;
    private long _reads, _invalidReads;

    public MeterSamplingDiagnostics GetDiagnostics() => new(Interlocked.Read(ref _reads), Interlocked.Read(ref _invalidReads));

    public SourcePeakSampler(IVoicemeeterLevels remote, double floorDbfs = -90)
    {
        ArgumentNullException.ThrowIfNull(remote);
        PeakMath.ValidateFloor(floorDbfs);
        _remote = remote;
        _floorDbfs = floorDbfs;
    }

    public SourcePeak Sample(SourceKind kind, int index, MeterTap tap)
    {
        var source = PotatoChannelMap.Get(kind, index);
        bool supported = kind == SourceKind.Bus
            ? tap == MeterTap.Output
            : tap is MeterTap.PreFader or MeterTap.PostFader or MeterTap.PostMute;
        if (!supported) throw new ArgumentOutOfRangeException(nameof(tap));

        float peak = 0;
        bool valid = true;
        foreach (int channel in source.Channels)
        {
            try
            {
                Interlocked.Increment(ref _reads);
                float value = _remote.GetLevel((int)tap, channel);
                if (!float.IsFinite(value) || value < 0)
                {
                    Interlocked.Increment(ref _invalidReads);
                    valid = false;
                }
                else peak = Math.Max(peak, value);
            }
            catch (InvalidOperationException)
            {
                // Existing Remote adapter uses this exception for nonzero GetLevel results.
                Interlocked.Increment(ref _invalidReads);
                valid = false;
            }
            catch
            {
                Interlocked.Increment(ref _invalidReads);
                throw; // Diagnostics must not turn an unexpected failure into a valid reading.
            }
        }

        // Conservative completeness policy: a missing channel could conceal the hottest peak.
        // Never present a partial source as a valid combined peak, nor reuse old values.
        return valid
            ? new(source.Id, tap, true, peak, PeakMath.ToDbfs(peak, _floorDbfs))
            : new(source.Id, tap, false, null, null);
    }
}
