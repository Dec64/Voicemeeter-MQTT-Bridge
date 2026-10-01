using TimerClock = VoicemeeterMqttBridge.Tests.TelemetryTimerClock;
using System.Threading.Channels;

namespace VoicemeeterMqttBridge.Tests;

public sealed class MeterTelemetryLoopTests
{
    private static MeteringV2Settings Settings() => new()
    {
        Enabled = true, FastEnabled = true, SlowEnabled = true,
        SampleIntervalMs = 50, FastPublishIntervalMs = 50, SlowPublishIntervalMs = 250,
        Sources = new() { new() { Id = "strip:0", Enabled = true, MeterTaps = new() { "pre", "post_mute" } },
            new() { Id = "bus:7", Enabled = true } }
    };
    private static MeterTelemetryLoop Loop(MeteringV2Settings settings, Levels levels, TimerClock clock)
        => new(SourceRegistry.Read(new SourceRegistryTests.Metadata(), settings), settings, levels, clock);
    private static async Task<MeterWindowSnapshot> Read(LatestMeterSnapshotQueue queue)
        => await queue.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    [Fact]
    public async Task One_sampling_pass_feeds_independent_fast_and_slow_peaks()
    {
        var clock = new TimerClock(); var levels = new Levels();
        var loop = Loop(Settings(), levels, clock);
        using var stop = new CancellationTokenSource();
        var run = loop.RunAsync(stop.Token);
        for (int i = 1; i <= 5; i++)
        {
            levels.Value = i == 1 ? 1 : 0.1f;
            clock.Advance(50);
            var fast = await Read(loop.Fast);
            Assert.Equal(TimeSpan.FromMilliseconds(50), fast.Duration);
            Assert.Equal(levels.Value, fast.Readings[0].Peak.LinearPeak);
        }
        var slow = await Read(loop.Slow);
        Assert.Equal(TimeSpan.FromMilliseconds(250), slow.Duration);
        Assert.Equal(1f, slow.Readings[0].Peak.LinearPeak);
        Assert.Equal(60, levels.Calls.Count); // (2 pre + 2 post-mute + 8 bus) * five samples.
        Assert.All(levels.Calls, call => Assert.True(call.Type == 3 ? call.Channel is >= 56 and <= 63
            : call.Type is 0 or 2 && call.Channel is 0 or 1));
        stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, clock.ActiveTimers);
        await Assert.ThrowsAsync<ChannelClosedException>(() => loop.Fast.ReadAsync().AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => loop.RunAsync());
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    public async Task Disabled_metering_does_no_sampling(bool enabled, bool fast, bool slow)
    {
        var settings = Settings(); settings.Enabled = enabled; settings.FastEnabled = fast; settings.SlowEnabled = slow;
        var clock = new TimerClock(); var levels = new Levels();
        var loop = Loop(settings, levels, clock);
        await loop.RunAsync();
        Assert.Empty(levels.Calls); Assert.Equal(0, clock.ActiveTimers);
        await Assert.ThrowsAsync<ChannelClosedException>(() => loop.Fast.ReadAsync().AsTask());
        await Assert.ThrowsAsync<ChannelClosedException>(() => loop.Slow.ReadAsync().AsTask());
    }

    [Fact]
    public async Task Slow_only_and_no_sources_do_not_start_fast_work()
    {
        var settings = Settings(); settings.FastEnabled = false;
        var clock = new TimerClock(); var levels = new Levels();
        var loop = Loop(settings, levels, clock);
        using var stop = new CancellationTokenSource(); var run = loop.RunAsync(stop.Token);
        clock.Advance(250);
        Assert.Equal(TimeSpan.FromMilliseconds(250), (await Read(loop.Slow)).Duration);
        Assert.Equal(12, levels.Calls.Count); // Missed ticks are coalesced, never replayed.
        stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ChannelClosedException>(() => loop.Fast.ReadAsync().AsTask());
        settings.Sources.Clear();
        await Loop(settings, levels, clock).RunAsync();
        Assert.Equal(12, levels.Calls.Count);
    }

    [Fact]
    public async Task Delayed_ticks_emit_one_actual_window_and_slow_consumer_does_not_block_fast()
    {
        var clock = new TimerClock(); var levels = new Levels();
        var loop = Loop(Settings(), levels, clock);
        using var stop = new CancellationTokenSource(); var run = loop.RunAsync(stop.Token);
        clock.Advance(1000);
        var delayed = await Read(loop.Fast);
        Assert.Equal(TimeSpan.FromMilliseconds(1000), delayed.Duration);
        Assert.False(delayed.IsFresh(TimeSpan.FromMilliseconds(750)));
        for (int i = 0; i < 10; i++)
        {
            clock.Advance(50);
            Assert.Equal(TimeSpan.FromMilliseconds(50), (await Read(loop.Fast)).Duration);
        }
        // Slow queue was unread throughout; only the newest 250 ms snapshot remains.
        Assert.Equal(TimeSpan.FromMilliseconds(250), (await Read(loop.Slow)).Duration);
        Assert.Equal(132, levels.Calls.Count);
        stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Read_failure_isolated_to_source_and_next_window_recovers()
    {
        var clock = new TimerClock(); var levels = new Levels { FailInput = true };
        var loop = Loop(Settings(), levels, clock);
        using var stop = new CancellationTokenSource(); var run = loop.RunAsync(stop.Token);
        clock.Advance(50);
        var failed = await Read(loop.Fast);
        Assert.All(failed.Readings.Where(r => r.Peak.SourceId == "strip:0"), r => Assert.False(r.Peak.Available));
        Assert.True(failed.Readings.Single(r => r.Peak.SourceId == "bus:7").Peak.Available);
        levels.FailInput = false; clock.Advance(50);
        Assert.All((await Read(loop.Fast)).Readings, r => Assert.True(r.Peak.Available));
        stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Cancellation_during_sample_prevents_frame_and_later_reads()
    {
        var clock = new TimerClock(); var levels = new Levels();
        using var stop = new CancellationTokenSource();
        levels.OnRead = () => stop.Cancel();
        var loop = Loop(Settings(), levels, clock); var run = loop.RunAsync(stop.Token);
        clock.Advance(50); await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, levels.Calls.Count); // Finish the in-flight source/tap, then stop.
        await Assert.ThrowsAsync<ChannelClosedException>(() => loop.Fast.ReadAsync().AsTask());
        Assert.Equal(0, clock.ActiveTimers);
    }

    [Fact]
    public async Task Unexpected_sample_exception_closes_queues_and_faults_run()
    {
        var clock = new TimerClock(); var levels = new Levels { OnRead = () => throw new IOException("fake") };
        var loop = Loop(Settings(), levels, clock); var run = loop.RunAsync();
        clock.Advance(50);
        await Assert.ThrowsAsync<IOException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAsync<ChannelClosedException>(() => loop.Fast.ReadAsync().AsTask());
        Assert.Equal(0, clock.ActiveTimers);
    }

    [Fact]
    public async Task Variable_sample_duration_does_not_shift_publish_deadlines()
    {
        var clock = new TimerClock(); var levels = new Levels();
        levels.OnRead = () => clock.Advance(1);
        var loop = Loop(Settings(), levels, clock);
        using var stop = new CancellationTokenSource(); var run = loop.RunAsync(stop.Token);
        try
        {
            clock.Advance(50);
            Assert.Equal(TimeSpan.FromMilliseconds(62), (await Read(loop.Fast)).Duration);
            levels.OnRead = null;
            clock.Advance(38); // Exactly the next 100 ms tick; only 38 ms since completion.
            Assert.Equal(TimeSpan.FromMilliseconds(38), (await Read(loop.Fast)).Duration);
        }
        finally { stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task Non_multiple_cadence_is_quantized_without_drift_and_settings_are_captured()
    {
        var settings = Settings(); settings.FastPublishIntervalMs = 75; settings.SlowEnabled = false;
        var clock = new TimerClock(); var levels = new Levels();
        var loop = Loop(settings, levels, clock);
        using var stop = new CancellationTokenSource(); var run = loop.RunAsync(stop.Token);
        settings.Enabled = false; settings.FastPublishIntervalMs = 5000;
        try
        {
            clock.Advance(100);
            Assert.Equal(TimeSpan.FromMilliseconds(100), (await Read(loop.Fast)).Duration);
            clock.Advance(50);
            Assert.Equal(TimeSpan.FromMilliseconds(50), (await Read(loop.Fast)).Duration);
            await Assert.ThrowsAsync<ChannelClosedException>(() => loop.Slow.ReadAsync().AsTask());
        }
        finally { stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task Already_cancelled_session_does_not_read_or_create_timer()
    {
        var clock = new TimerClock(); var levels = new Levels();
        var loop = Loop(Settings(), levels, clock);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await loop.RunAsync(stop.Token);
        Assert.Empty(levels.Calls); Assert.Equal(0, clock.ActiveTimers);
        await Assert.ThrowsAsync<ChannelClosedException>(() => loop.Fast.ReadAsync().AsTask());
        await Assert.ThrowsAsync<ChannelClosedException>(() => loop.Slow.ReadAsync().AsTask());
    }

    [Fact]
    public async Task Concurrent_second_start_does_not_close_running_session()
    {
        var clock = new TimerClock(); var levels = new Levels();
        var loop = Loop(Settings(), levels, clock);
        using var stop = new CancellationTokenSource(); var run = loop.RunAsync(stop.Token);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => loop.RunAsync());
            clock.Advance(50);
            Assert.True((await Read(loop.Fast)).Readings[0].Peak.Available);
        }
        finally { stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task Invalid_settings_fault_session_and_complete_both_queues()
    {
        var settings = Settings(); var clock = new TimerClock(); var levels = new Levels();
        var loop = Loop(settings, levels, clock);
        settings.SampleIntervalMs = 0;
        await Assert.ThrowsAsync<ArgumentException>(() => loop.RunAsync());
        Assert.Empty(levels.Calls); Assert.Equal(0, clock.ActiveTimers);
        await Assert.ThrowsAsync<ChannelClosedException>(() => loop.Fast.ReadAsync().AsTask());
        await Assert.ThrowsAsync<ChannelClosedException>(() => loop.Slow.ReadAsync().AsTask());
    }

    private sealed class Levels : IVoicemeeterLevels
    {
        public float Value = 0.5f;
        public bool FailInput;
        public Action? OnRead;
        public List<(int Type, int Channel)> Calls { get; } = new();
        public float GetLevel(int type, int channel)
        {
            Calls.Add((type, channel)); OnRead?.Invoke();
            if (FailInput && type == 0 && channel == 0) throw new InvalidOperationException("fake unavailable");
            return Value;
        }
    }

}
