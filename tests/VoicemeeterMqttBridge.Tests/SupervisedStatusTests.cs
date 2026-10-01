using System.Text.Json;
using MQTTnet;

namespace VoicemeeterMqttBridge.Tests;

public sealed class SupervisedStatusTests
{
    private readonly TelemetryBroker _broker = new();
    private readonly TelemetryTimerClock _clock = new();
    private readonly Levels _levels = new();
    private readonly MeteringV2Settings _settings = new()
    {
        Enabled = true, FastEnabled = true, SlowEnabled = false,
        Sources = new() { new() { Id = "strip:0", Enabled = true, MeterTaps = new() { "pre" } } }
    };
    private MeterTelemetrySupervisor Supervisor() => new(_broker.Client, _levels, _clock);
    private Task Run(MeterTelemetrySupervisor supervisor, CancellationToken token = default, int timeoutMs = 5000)
        => supervisor.RunAsync(SourceRegistry.Read(new SourceRegistryTests.Metadata(), _settings), _settings,
            "test/pc", "2-dev", TimeSpan.FromMilliseconds(750), TimeSpan.FromSeconds(3), token,
            status: new(TimeSpan.FromMilliseconds(timeoutMs)));
    private static string State(MqttApplicationMessage message)
    {
        if (!message.Topic.EndsWith("/status")) return message.Topic.Split('/').Last();
        using var json = JsonDocument.Parse(message.PayloadSegment);
        return json.RootElement.GetProperty("session_state").GetString()!;
    }

    [Fact]
    public async Task Normal_lifecycle_has_matching_session_and_stopped_after_timer_disposal()
    {
        var states = new List<string>(); var sessions = new List<string>();
        _broker.BeforeSend = (message, token) =>
        {
            var state = State(message); states.Add(state);
            using var json = JsonDocument.Parse(message.PayloadSegment);
            sessions.Add(json.RootElement.GetProperty("session_id").GetString()!);
            if (state == "starting") Assert.Equal(0, _clock.ActiveTimers);
            if (state == "running") Assert.Equal(1, _clock.ActiveTimers);
            if (state == "stopped") Assert.False(token.IsCancellationRequested);
            return Task.CompletedTask;
        };
        using var stop = new CancellationTokenSource();
        var run = Run(Supervisor(), stop.Token);
        Assert.Equal(new[] { "starting", "metadata", "running" }, states);
        stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "starting", "metadata", "running", "stopped" }, states);
        Assert.Single(sessions.Distinct()); Assert.Equal(0, _clock.ActiveTimers);
    }

    [Fact]
    public async Task Metadata_failure_reports_faulted_without_running_and_preserves_original()
    {
        var error = new IOException("metadata failure"); var states = new List<string>();
        _broker.BeforeSend = (message, _) =>
        {
            var state = State(message); states.Add(state);
            if (state == "metadata") throw error;
            return Task.CompletedTask;
        };
        Assert.Same(error, await Assert.ThrowsAsync<IOException>(() => Run(Supervisor())));
        Assert.Equal(new[] { "starting", "metadata", "faulted" }, states);
        Assert.Equal(0, _clock.ActiveTimers);
    }

    [Fact]
    public async Task Sampling_failure_and_terminal_failure_are_both_preserved()
    {
        var primary = new IOException("native failure"); var terminal = new IOException("status failure");
        _levels.Error = primary;
        _broker.BeforeSend = (message, _) => State(message) == "faulted" ? throw terminal : Task.CompletedTask;
        var run = Run(Supervisor()); _clock.Advance(50);
        var error = await Assert.ThrowsAsync<AggregateException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Same(primary, error.InnerExceptions[0]);
        Assert.Same(terminal, error.InnerExceptions[1].InnerException);
        Assert.Equal(0, _clock.ActiveTimers);
    }

    [Theory]
    [InlineData("starting")]
    [InlineData("running")]
    public async Task Startup_status_failure_stops_sampler_and_attempts_faulted(string failingState)
    {
        var states = new List<string>(); var failure = new IOException("status rejected");
        _broker.BeforeSend = (message, _) =>
        {
            var state = State(message); states.Add(state);
            if (state == failingState) throw failure;
            return Task.CompletedTask;
        };
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => Run(Supervisor())));
        Assert.Equal("faulted", states.Last()); Assert.Equal(0, _clock.ActiveTimers);
    }

    [Fact]
    public async Task Terminal_timeout_remains_visible_after_normal_cancellation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _broker.BeforeSend = (message, token) =>
        {
            if (State(message) != "stopped") return Task.CompletedTask;
            entered.SetResult(); return Task.Delay(Timeout.InfiniteTimeSpan, token);
        };
        using var stop = new CancellationTokenSource(); var run = Run(Supervisor(), stop.Token, 100);
        stop.Cancel(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _clock.Advance(100);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsAssignableFrom<OperationCanceledException>(failure.InnerException);
        Assert.Equal(0, _clock.ActiveTimers);
    }

    [Fact]
    public async Task Uncooperative_terminal_send_blocks_restart_even_after_deadline()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _broker.BeforeSend = (message, _) =>
        {
            if (State(message) != "stopped") return Task.CompletedTask;
            entered.TrySetResult(); return release.Task;
        };
        var supervisor = Supervisor();
        using var stop = new CancellationTokenSource(); var run = Run(supervisor, stop.Token, 100);
        stop.Cancel(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); _clock.Advance(100);
        Assert.False(run.IsCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(supervisor));
        release.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        _broker.BeforeSend = null;
        using var restartStop = new CancellationTokenSource(); var restart = Run(supervisor, restartStop.Token);
        restartStop.Cancel(); await restart.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Sampling_failure_cancels_pending_running_status_and_preserves_native_error()
    {
        var failure = new IOException("native failed during status send");
        _levels.Error = failure;
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _broker.BeforeSend = async (message, token) =>
        {
            if (State(message) != "running") return;
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { canceled.TrySetResult(); }
        };
        var run = Run(Supervisor()); _clock.Advance(50);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => run.WaitAsync(TimeSpan.FromSeconds(5))));
        Assert.Equal(0, _clock.ActiveTimers);
    }

    [Fact]
    public async Task Cancellation_during_starting_drains_before_stopped_and_never_starts_sampling()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var states = new List<string>();
        _broker.BeforeSend = (message, _) =>
        {
            var state = State(message); states.Add(state);
            return state == "starting" ? release.Task : Task.CompletedTask;
        };
        var supervisor = Supervisor();
        using var stop = new CancellationTokenSource(); var run = Run(supervisor, stop.Token);
        stop.Cancel(); Assert.False(run.IsCompleted);
        Assert.Equal(new[] { "starting" }, states);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(supervisor));
        release.SetResult(); await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "starting", "stopped" }, states);
        Assert.Equal(0, _clock.ActiveTimers);
    }

    [Fact]
    public async Task Disconnected_shutdown_skips_impossible_terminal_send()
    {
        using var stop = new CancellationTokenSource(); var run = Run(Supervisor(), stop.Token);
        _broker.Connected = false; stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, _broker.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(60001)]
    public async Task Invalid_shutdown_budget_fails_before_any_send(int timeoutMs)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Run(Supervisor(), timeoutMs: timeoutMs));
        Assert.Equal(0, _broker.Calls); Assert.Equal(0, _clock.ActiveTimers);
    }

    [Fact]
    public async Task Disabled_session_does_not_overwrite_retained_status()
    {
        _settings.Enabled = false; await Run(Supervisor()); Assert.Equal(0, _broker.Calls);
    }

    private sealed class Levels : IVoicemeeterLevels
    {
        public Exception? Error;
        public float GetLevel(int type, int channel) => Error is null ? 1 : throw Error;
    }
}
