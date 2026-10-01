using MQTTnet.Client;

namespace VoicemeeterMqttBridge.Tests;

public sealed class DiscoveryStartupTests
{
    private readonly TelemetryBroker _broker = new();
    private readonly TelemetryTimerClock _clock = new();
    private readonly Levels _levels = new();
    private readonly MeteringV2Settings _settings = new()
    {
        Enabled = true, FastEnabled = true, SlowEnabled = true,
        Sources = new() { new() { Id = "strip:0", Enabled = true } }
    };
    private Task Run(MeterTelemetrySupervisor supervisor, CancellationToken token = default)
        => supervisor.RunAsync(SourceRegistry.Read(new SourceRegistryTests.Metadata(), _settings), _settings,
            "test/bridge", "2-dev", TimeSpan.FromMilliseconds(750), TimeSpan.FromSeconds(3), token,
            new SlowDiscoveryOptions("test_pc", "homeassistant", 5));

    [Fact]
    public async Task Metadata_then_all_discovery_finish_before_sampling()
    {
        var topics = new List<string>();
        _broker.BeforeSend = (message, _) =>
        {
            Assert.Equal(0, _levels.Calls); Assert.Equal(0, _clock.ActiveTimers);
            topics.Add(message.Topic); return Task.CompletedTask;
        };
        using var stop = new CancellationTokenSource();
        var run = Run(new(_broker.Client, _levels, _clock), stop.Token);
        Assert.Equal(new[] { "test/bridge/v2/metadata",
            "homeassistant/sensor/voicemeeter_test_pc_v2_strip_0_peak/config",
            "homeassistant/binary_sensor/voicemeeter_test_pc_v2_strip_0_active/config",
            "homeassistant/binary_sensor/voicemeeter_test_pc_v2_strip_0_clip/config" }, topics);
        Assert.Equal(1, _clock.ActiveTimers);
        stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_discovery_stops_startup_without_sampling(bool reject)
    {
        _broker.BeforeSend = (message, _) =>
        {
            if (message.Topic.EndsWith("_active/config"))
            {
                if (reject) _broker.Result = MqttClientPublishReasonCode.NotAuthorized;
                else throw new IOException("discovery failed");
            }
            return Task.CompletedTask;
        };
        var run = Run(new(_broker.Client, _levels, _clock));
        if (reject) await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        else await Assert.ThrowsAsync<IOException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(3, _broker.Calls); Assert.Equal(0, _clock.ActiveTimers); Assert.Equal(0, _levels.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discovery_cancellation_waits_for_actual_send_and_prevents_later_messages(bool ignoresCancellation)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _broker.BeforeSend = (message, token) => message.Topic.EndsWith("/config")
            ? ignoresCancellation ? release.Task : release.Task.WaitAsync(token) : Task.CompletedTask;
        var supervisor = new MeterTelemetrySupervisor(_broker.Client, _levels, _clock);
        using var stop = new CancellationTokenSource(); var run = Run(supervisor, stop.Token);
        Assert.Equal(2, _broker.Calls); stop.Cancel();
        if (ignoresCancellation)
        {
            Assert.False(run.IsCompleted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Run(supervisor));
            release.SetResult();
        }
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, _broker.Calls); Assert.Equal(0, _clock.ActiveTimers); Assert.Equal(0, _levels.Calls);
    }

    [Fact]
    public async Task Fast_only_skips_discovery()
    {
        _settings.SlowEnabled = false;
        using var stop = new CancellationTokenSource(); var run = Run(new(_broker.Client, _levels, _clock), stop.Token);
        Assert.Equal(1, _broker.Calls);
        stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Invalid_discovery_options_fail_before_any_network_write()
    {
        var supervisor = new MeterTelemetrySupervisor(_broker.Client, _levels, _clock);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => supervisor.RunAsync(
            SourceRegistry.Read(new SourceRegistryTests.Metadata(), _settings), _settings,
            "test/bridge", "2-dev", TimeSpan.FromMilliseconds(750), TimeSpan.FromSeconds(3),
            discovery: new SlowDiscoveryOptions("pc", "homeassistant", 1)));
        Assert.Equal(0, _broker.Calls); Assert.Equal(0, _clock.ActiveTimers);
    }

    [Fact]
    public async Task Explicit_restart_after_partial_failure_republishes_the_whole_set()
    {
        var topics = new List<string>();
        _broker.BeforeSend = (message, _) =>
        {
            topics.Add(message.Topic);
            if (message.Topic.EndsWith("_active/config")) throw new IOException("partial failure");
            return Task.CompletedTask;
        };
        var supervisor = new MeterTelemetrySupervisor(_broker.Client, _levels, _clock);
        await Assert.ThrowsAsync<IOException>(() => Run(supervisor));
        topics.Clear();
        _broker.BeforeSend = (message, _) => { topics.Add(message.Topic); return Task.CompletedTask; };
        using var stop = new CancellationTokenSource(); var restarted = Run(supervisor, stop.Token);
        Assert.Equal(4, topics.Count);
        Assert.Equal("test/bridge/v2/metadata", topics[0]);
        Assert.EndsWith("_peak/config", topics[1]);
        Assert.EndsWith("_active/config", topics[2]);
        Assert.EndsWith("_clip/config", topics[3]);
        stop.Cancel(); await restarted.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Disconnect_during_discovery_prevents_remaining_configs_and_sampling()
    {
        _broker.BeforeSend = (message, _) =>
        {
            if (message.Topic.EndsWith("/config")) _broker.Connected = false;
            return Task.CompletedTask;
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(new(_broker.Client, _levels, _clock)));
        Assert.Equal(2, _broker.Calls); Assert.Equal(0, _clock.ActiveTimers);
    }

    private sealed class Levels : IVoicemeeterLevels
    {
        public int Calls;
        public float GetLevel(int type, int channel) { Interlocked.Increment(ref Calls); return 1; }
    }
}
