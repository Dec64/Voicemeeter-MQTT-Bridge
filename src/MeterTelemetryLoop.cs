// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
namespace VoicemeeterMqttBridge;

/// <summary>
/// One session of timed sampling with independent bounded fast/slow windows.
/// Not connected to BridgeService. Native callers must supply an owner-backed levels
/// adapter; async continuations do not guarantee a fixed native thread.
/// </summary>
public sealed class MeterTelemetryLoop
{
    private readonly SourceRegistry _registry;
    private readonly MeteringV2Settings _settings;
    private readonly IVoicemeeterLevels _levels;
    private readonly TimeProvider _time;
    private SourcePeakSampler? _sampler;
    private int _started;

    public LatestMeterSnapshotQueue Fast { get; } = new();
    public LatestMeterSnapshotQueue Slow { get; } = new();

    public MeterTelemetryLoop(SourceRegistry registry, MeteringV2Settings settings,
        IVoicemeeterLevels levels, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(levels);
        _registry = registry;
        _settings = settings;
        _levels = levels;
        _time = timeProvider ?? TimeProvider.System;
    }

    public MeterSamplingDiagnostics GetDiagnostics() => Volatile.Read(ref _sampler)?.GetDiagnostics() ?? new(0, 0);

    /// <summary>
    /// Capture settings before the first await. Cancellation stops this session and
    /// completes both queues; reconnect/config changes require a new loop and registry.
    /// The caller must observe this task: unexpected sampling errors are propagated.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("A telemetry loop can only run once.");
        try
        {
            _settings.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            var sources = _registry.Sources.Where(s => s.Enabled).ToArray();
            if (!_settings.Enabled || (!_settings.FastEnabled && !_settings.SlowEnabled) || sources.Length == 0) return;

            var sampleInterval = TimeSpan.FromMilliseconds(_settings.SampleIntervalMs);
            var fastInterval = TimeSpan.FromMilliseconds(_settings.FastPublishIntervalMs);
            var slowInterval = TimeSpan.FromMilliseconds(_settings.SlowPublishIntervalMs);
            var sampler = new SourcePeakSampler(_levels, _settings.DisplayFloorDbfs);
            Volatile.Write(ref _sampler, sampler);
            var fast = _settings.FastEnabled ? new MeterWindowAccumulator(_registry, _settings, _time) : null;
            var slow = _settings.SlowEnabled ? new MeterWindowAccumulator(_registry, _settings, _time) : null;
            if (fast is null) Fast.Complete();
            if (slow is null) Slow.Complete();
            long origin = _time.GetTimestamp();
            var nextFast = fastInterval;
            var nextSlow = slowInterval;
            using var timer = new PeriodicTimer(sampleInterval, _time);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var source in sources)
                foreach (var tap in source.MeterTaps)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var peak = sampler.Sample(source.Kind, source.Index, tap);
                    cancellationToken.ThrowIfCancellationRequested();
                    fast?.Observe(peak);
                    slow?.Observe(peak);
                }
                // Complete once per elapsed cadence. Late ticks never replay historical windows.
                var elapsed = _time.GetElapsedTime(origin, _time.GetTimestamp());
                if (fast is not null && elapsed >= nextFast)
                {
                    Fast.TryWrite(fast.CompleteWindow());
                    nextFast = NextDeadline(elapsed, fastInterval);
                }
                if (slow is not null && elapsed >= nextSlow)
                {
                    Slow.TryWrite(slow.CompleteWindow());
                    nextSlow = NextDeadline(elapsed, slowInterval);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            Fast.Complete();
            Slow.Complete();
        }
    }

    // Anchor deadlines to session start, not completion time: variable read duration
    // must not silently halve the publishing rate or accumulate cadence drift.
    private static TimeSpan NextDeadline(TimeSpan elapsed, TimeSpan interval)
        => elapsed + interval - TimeSpan.FromTicks(elapsed.Ticks % interval.Ticks);
}
