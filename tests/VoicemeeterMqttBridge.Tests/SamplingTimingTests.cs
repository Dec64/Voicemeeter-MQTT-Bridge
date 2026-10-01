namespace VoicemeeterMqttBridge.Tests;

public sealed class SamplingTimingTests
{
    [Fact]
    public async Task Maximum_survives_a_subsequent_shorter_pass()
    {
        var clock = new TelemetryTimerClock(); var levels = new Levels(clock, false);
        var settings = new MeteringV2Settings
        {
            Enabled = true, FastEnabled = true, SlowEnabled = false,
            Sources = new() { new() { Id = "strip:0", Enabled = true, MeterTaps = new() { "pre" } } }
        };
        var loop = new MeterTelemetryLoop(SourceRegistry.Read(new SourceRegistryTests.Metadata(), settings), settings, levels, clock);
        using var stop = new CancellationTokenSource(); var run = loop.RunAsync(stop.Token);
        clock.Advance(50); await loop.Fast.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(SpinWait.SpinUntil(() => loop.GetDiagnostics().SamplePassCount == 1, TimeSpan.FromSeconds(5)));
        levels.DelayMs = 1;
        clock.Advance(50); await loop.Fast.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, loop.GetDiagnostics().SamplePassCount);
        Assert.Equal(2, loop.GetDiagnostics().LastPassMs);
        Assert.Equal(6, loop.GetDiagnostics().MaxPassMs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pass_duration_excludes_timer_wait_and_includes_failed_native_work(bool fail)
    {
        var clock = new TelemetryTimerClock();
        var settings = new MeteringV2Settings
        {
            Enabled = true, FastEnabled = true, SlowEnabled = false,
            Sources = new() { new() { Id = "strip:0", Enabled = true, MeterTaps = new() { "pre" } } }
        };
        var levels = new Levels(clock, fail);
        var loop = new MeterTelemetryLoop(SourceRegistry.Read(new SourceRegistryTests.Metadata(), settings), settings, levels, clock);
        Assert.Null(loop.GetDiagnostics().LastPassMs); Assert.Equal(0, loop.GetDiagnostics().SamplePassCount);
        using var stop = new CancellationTokenSource(); var run = loop.RunAsync(stop.Token);
        clock.Advance(50);
        if (fail) await Assert.ThrowsAsync<IOException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        else
        {
            await loop.Fast.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
        var metrics = loop.GetDiagnostics();
        Assert.Equal(1, metrics.SamplePassCount);
        Assert.Equal(fail ? 3 : 6, metrics.LastPassMs);
        Assert.Equal(metrics.LastPassMs, metrics.MaxPassMs);
        Assert.Equal(fail ? 1 : 2, metrics.ApiReadCount);
        clock.Advance(1000); Assert.Equal(metrics, loop.GetDiagnostics());
    }

    private sealed class Levels(TelemetryTimerClock clock, bool fail) : IVoicemeeterLevels
    {
        public int DelayMs = 3;
        public float GetLevel(int type, int channel)
        {
            clock.Advance(DelayMs);
            if (fail) throw new IOException("sample failure");
            return 1;
        }
    }
}
