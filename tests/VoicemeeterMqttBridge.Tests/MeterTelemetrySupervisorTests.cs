using System.Text.Json;

namespace VoicemeeterMqttBridge.Tests;

public sealed class MeterTelemetrySupervisorTests
{
    private readonly TelemetryTimerClock _clock = new();
    private readonly TelemetryBroker _broker = new();
    private readonly Levels _levels = new();
    private readonly MeteringV2Settings _settings = new()
    {
        Enabled = true, FastEnabled = true, SlowEnabled = false,
        Sources = new() { new() { Id = "strip:0", Enabled = true, MeterTaps = new() { "pre" } } }
    };
    private MeterTelemetrySupervisor Supervisor() => new(_broker.Client, _levels, _clock);
    private Task Run(MeterTelemetrySupervisor supervisor, CancellationToken token = default)
        => supervisor.RunAsync(SourceRegistry.Read(new SourceRegistryTests.Metadata(), _settings), _settings,
            "example/pc", "2.0.0-dev", TimeSpan.FromMilliseconds(750), TimeSpan.FromSeconds(3), token);
    private async Task<JsonDocument> Read()
    {
        while (true)
        {
            var message = await _broker.Sent.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            if (!message.Topic.EndsWith("/metadata")) return JsonDocument.Parse(message.PayloadSegment);
        }
    }

    [Fact]
    public async Task Restart_gets_new_session_and_does_not_replay_pending_old_peak()
    {
        var supervisor = Supervisor();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? oldSession = null;
        _broker.BeforeSend = async (message, token) =>
        {
            if (message.Topic.EndsWith("/metadata")) return;
            using var json = JsonDocument.Parse(message.PayloadSegment);
            oldSession = json.RootElement.GetProperty("session_id").GetString();
            entered.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token);
        };
        using var firstStop = new CancellationTokenSource();
        var first = Run(supervisor, firstStop.Token);
        _clock.Advance(50); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _clock.Advance(50); // Another old peak remains pending while the first send stalls.
        firstStop.Cancel(); await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, _clock.ActiveTimers);
        _broker.BeforeSend = null; _levels.Value = 0.1f;
        using var secondStop = new CancellationTokenSource(); var second = Run(supervisor, secondStop.Token);
        _clock.Advance(50); using var fresh = await Read();
        Assert.NotEqual(oldSession, fresh.RootElement.GetProperty("session_id").GetString());
        Assert.Equal(0, fresh.RootElement.GetProperty("seq").GetInt64());
        Assert.Equal(-20, fresh.RootElement.GetProperty("sources").GetProperty("strip:0").GetProperty("pre_dbfs").GetDouble(), 4);
        secondStop.Cancel(); await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(_broker.Sent.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Publishing_failure_stops_sampling_and_allows_explicit_restart()
    {
        var supervisor = Supervisor(); _broker.BeforeSend = (message, _) => message.Topic.EndsWith("/metadata") ? Task.CompletedTask : throw new IOException("fake failure");
        var failed = Run(supervisor); _clock.Advance(50);
        await Assert.ThrowsAsync<IOException>(() => failed.WaitAsync(TimeSpan.FromSeconds(5)));
        int calls = _levels.Calls; _clock.Advance(1000);
        Assert.Equal(calls, _levels.Calls); Assert.Equal(0, _clock.ActiveTimers);
        _broker.BeforeSend = null;
        using var stop = new CancellationTokenSource(); var recovered = Run(supervisor, stop.Token);
        _clock.Advance(50); using var _ = await Read();
        stop.Cancel(); await recovered.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Sampling_failure_stops_idle_publisher_and_preserves_exception()
    {
        _levels.Error = new IOException("sample failure");
        var run = Run(Supervisor()); _clock.Advance(50);
        var error = await Assert.ThrowsAsync<IOException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Same(_levels.Error, error);
        Assert.Equal(0, _clock.ActiveTimers); Assert.Equal(1, _broker.Calls);
    }

    [Fact]
    public async Task Uncooperative_send_blocks_restart_until_it_actually_finishes()
    {
        var supervisor = Supervisor();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _broker.BeforeSend = async (message, _) => { if (message.Topic.EndsWith("/metadata")) return; entered.TrySetResult(); await release.Task; };
        using var stop = new CancellationTokenSource(); var run = Run(supervisor, stop.Token);
        _clock.Advance(50); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(supervisor));
        Assert.False(run.IsCompleted);
        release.SetResult(); await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, _clock.ActiveTimers);
    }

    [Fact]
    public async Task Publisher_failure_stops_sampler_even_while_sibling_send_ignores_cancellation()
    {
        _settings.SlowEnabled = true; _settings.SlowPublishIntervalMs = 250;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _broker.BeforeSend = async (message, _) =>
        {
            if (message.Topic.EndsWith("/metadata")) return;
            if (message.Topic.EndsWith("/slow")) { failed.TrySetResult(); throw new IOException("slow failed"); }
            entered.TrySetResult(); await release.Task;
        };
        var supervisor = Supervisor(); var run = Run(supervisor);
        _clock.Advance(50); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _clock.Advance(200); await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Wait for cancellation to reach the actual sampling timer, not a fixed sleep.
        await _clock.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        int calls = _levels.Calls; _clock.Advance(1000); Assert.Equal(calls, _levels.Calls);
        Assert.False(run.IsCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(supervisor));
        release.SetResult();
        await Assert.ThrowsAsync<IOException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabled_or_empty_profile_does_not_use_disconnected_client(bool empty)
    {
        if (empty) _settings.Sources.Clear(); else _settings.Enabled = false;
        _broker.Connected = false;
        await Run(Supervisor()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, _levels.Calls); Assert.Equal(0, _broker.Calls); Assert.Equal(0, _clock.ActiveTimers);
    }

    [Fact]
    public async Task Disconnected_start_and_invalid_budget_do_not_start_sampler()
    {
        var supervisor = Supervisor(); _broker.Connected = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(supervisor));
        _broker.Connected = true;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => supervisor.RunAsync(
            SourceRegistry.Read(new SourceRegistryTests.Metadata(), _settings), _settings, "example/pc", "2.0.0-dev",
            TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(3)));
        Assert.Equal(0, _levels.Calls); Assert.Equal(0, _clock.ActiveTimers);
    }

    [Fact]
    public async Task Already_cancelled_start_does_not_sample_and_releases_start_gate()
    {
        var supervisor = Supervisor(); using var stop = new CancellationTokenSource(); stop.Cancel();
        await Run(supervisor, stop.Token);
        Assert.Equal(0, _levels.Calls); Assert.Equal(0, _clock.ActiveTimers);
        _settings.Enabled = false;
        await Run(supervisor);
    }

    [Fact]
    public async Task Unrequested_transport_cancellation_is_a_failure_not_normal_shutdown()
    {
        _broker.BeforeSend = (message, _) => message.Topic.EndsWith("/metadata") ? Task.CompletedTask : Task.FromCanceled(new CancellationToken(true));
        var run = Run(Supervisor()); _clock.Advance(50);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        Assert.Equal(0, _clock.ActiveTimers);
    }

    private sealed class Levels : IVoicemeeterLevels
    {
        public int Calls;
        public float Value = 1;
        public Exception? Error;
        public float GetLevel(int type, int channel)
        {
            Interlocked.Increment(ref Calls);
            if (Error is not null) throw Error;
            return Value;
        }
    }
}
