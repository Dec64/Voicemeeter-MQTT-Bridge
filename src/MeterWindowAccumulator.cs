// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
namespace VoicemeeterMqttBridge;

public sealed record MeterWindowReading(SourcePeak Peak, bool? Active, bool? Clipping);
public sealed record MeterWindowSnapshot(TimeSpan Duration, IReadOnlyList<MeterWindowReading> Readings);

/// <summary>
/// Single-caller accumulator with fixed storage per enabled source/tap. Create separate
/// instances for fast and slow windows; no native calls, timer or network work occurs here.
/// </summary>
public sealed class MeterWindowAccumulator
{
    private readonly SourceDescriptor[] _sources;
    private readonly Dictionary<(string, MeterTap), TapState> _states;
    private readonly TimeProvider _time;
    private readonly double _floor, _activityOn, _activityOff, _clipOn, _clipOff;
    private readonly TimeSpan _activityHold, _clipHold;
    private long _windowStart;

    public MeterWindowAccumulator(SourceRegistry registry, MeteringV2Settings settings, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        _sources = registry.Sources.Where(s => s.Enabled).ToArray();
        _states = _sources.SelectMany(s => s.MeterTaps.Select(t => (s.Id, t)))
            .ToDictionary(key => key, _ => new TapState());
        _time = timeProvider ?? TimeProvider.System;
        _floor = settings.DisplayFloorDbfs;
        _activityOn = settings.ActivityThresholdDbfs;
        _activityOff = _activityOn - settings.ActivityHysteresisDb;
        _clipOn = settings.ClipThresholdDbfs;
        _clipOff = _clipOn - settings.ClipHysteresisDb;
        _activityHold = TimeSpan.FromMilliseconds(settings.ActivityHoldMs);
        _clipHold = TimeSpan.FromMilliseconds(settings.ClipHoldMs);
        _windowStart = _time.GetTimestamp();
    }

    public void Observe(SourcePeak sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!_states.TryGetValue((sample.SourceId, sample.Tap), out var state))
            throw new ArgumentException("Sample does not belong to an enabled source/tap.", nameof(sample));
        state.Seen = true;
        if (!sample.Available || sample.LinearPeak is not float value || !float.IsFinite(value) || value < 0)
        {
            state.Invalid = true;
            state.ResetSignals();
            return;
        }
        long now = _time.GetTimestamp();
        state.Peak = Math.Max(state.Peak, value);
        state.Latest = PeakMath.ToDbfs(value, _floor);
        if (value == 0) { state.Active = false; state.LastActive = null; }
        else if (state.Latest > _activityOn) { state.Active = true; state.LastActive = now; }
        if (state.Latest >= _clipOn) { state.Clipping = true; state.LastClip = now; }
        ExpireHolds(state, now);
    }

    public MeterWindowSnapshot CompleteWindow()
    {
        long now = _time.GetTimestamp();
        TimeSpan duration = _time.GetElapsedTime(_windowStart, now);
        if (duration <= TimeSpan.Zero) throw new InvalidOperationException("The meter window must have positive duration.");
        var readings = new List<MeterWindowReading>(_states.Count);
        foreach (var source in _sources)
        {
            bool available = source.MeterTaps.All(t => _states[(source.Id, t)] is { Seen: true, Invalid: false });
            foreach (var tap in source.MeterTaps)
            {
                var state = _states[(source.Id, tap)];
                if (!available) state.ResetSignals();
                else ExpireHolds(state, now);
                readings.Add(new(new SourcePeak(source.Id, tap, available,
                    available ? state.Peak : null, available ? PeakMath.ToDbfs(state.Peak, _floor) : null),
                    available ? state.Active : null, available ? state.Clipping : null));
                state.Seen = state.Invalid = false;
                state.Peak = 0;
            }
        }
        _windowStart = now;
        return new(duration, readings.AsReadOnly());
    }

    private void ExpireHolds(TapState state, long now)
    {
        if (state.Active && state.Latest <= _activityOff &&
            state.LastActive is long active && _time.GetElapsedTime(active, now) >= _activityHold)
            state.Active = false;
        if (state.Clipping && state.Latest < _clipOff &&
            state.LastClip is long clip && _time.GetElapsedTime(clip, now) >= _clipHold)
            state.Clipping = false;
    }

    private sealed class TapState
    {
        public bool Seen, Invalid, Active, Clipping;
        public float Peak;
        public double Latest;
        public long? LastActive, LastClip;
        public void ResetSignals()
        {
            Active = Clipping = false;
            LastActive = LastClip = null;
        }
    }
}
