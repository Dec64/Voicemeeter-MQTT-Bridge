using Broker = VoicemeeterMqttBridge.Tests.TelemetryBroker;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace VoicemeeterMqttBridge.Tests;

public sealed class MeterTelemetryPublisherTests
{
    private readonly ManualTimeProvider _clock = new();
    private readonly MeteringV2Settings _settings = new()
    {
        Enabled = true, FastEnabled = true, SlowEnabled = true,
        Sources = new() { new() { Id = "strip:0", Enabled = true, MeterTaps = new() { "pre", "post_mute" } } }
    };
    private readonly LatestMeterSnapshotQueue _fast = new(), _slow = new();
    private static readonly TimeSpan FastAge = TimeSpan.FromMilliseconds(750), SlowAge = TimeSpan.FromSeconds(3);
    private SourceRegistry Registry() => SourceRegistry.Read(new SourceRegistryTests.Metadata(), _settings);
    private MeterWindowSnapshot Capture(float value = 0.5f)
    {
        var window = new MeterWindowAccumulator(Registry(), _settings, _clock);
        window.Observe(new("strip:0", MeterTap.PreFader, true, value, null));
        window.Observe(new("strip:0", MeterTap.PostMute, true, 0, null));
        _clock.Advance(TimeSpan.FromMilliseconds(50));
        return window.CompleteWindow();
    }
    private MeterTelemetryPublisher Publisher(Broker broker, string topic = "example/pc", TimeProvider? clock = null)
        => new(broker.Client, new AggregateFrameBuilder(Registry(), _settings), topic, clock);
    private Task Run(MeterTelemetryPublisher publisher, CancellationToken token = default)
        => publisher.RunAsync(_fast, _slow, _settings, FastAge, SlowAge, token);
    private static async Task<MqttApplicationMessage> Read(Broker broker)
        => await broker.Sent.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    [Fact]
    public async Task Diagnostics_count_each_stream_and_freeze_after_completion()
    {
        var broker = new Broker(); var publisher = Publisher(broker, clock: _clock);
        var snapshot = Capture(); _fast.TryWrite(snapshot); _slow.TryWrite(snapshot);
        _fast.Complete(); _slow.Complete(); await Run(publisher);
        var measured = publisher.GetDiagnostics();
        Assert.Equal(1, measured.FastPublishCount); Assert.Equal(1, measured.SlowPublishCount);
        Assert.Equal(0.05, measured.ElapsedSeconds, 6);
        _clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(measured, publisher.GetDiagnostics());
        var fresh = Publisher(broker, clock: _clock).GetDiagnostics();
        Assert.Equal(0, fresh.FastPublishCount); Assert.Equal(0, fresh.SlowPublishCount);
        Assert.Equal(0, fresh.ElapsedSeconds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Diagnostics_do_not_count_failed_or_rejected_sends(bool reject)
    {
        var broker = new Broker(); var publisher = Publisher(broker, clock: _clock);
        if (reject) broker.Result = MqttClientPublishReasonCode.NotAuthorized;
        else broker.BeforeSend = (_, _) => throw new IOException("send failed");
        _fast.TryWrite(Capture()); _fast.Complete(); _slow.Complete();
        if (reject) await Assert.ThrowsAsync<InvalidOperationException>(() => Run(publisher));
        else await Assert.ThrowsAsync<IOException>(() => Run(publisher));
        Assert.Equal(0, publisher.GetDiagnostics().FastPublishCount);
        Assert.Equal(0, publisher.GetDiagnostics().SlowPublishCount);
    }

    [Fact]
    public async Task Diagnostics_do_not_count_stale_frames()
    {
        var broker = new Broker(); var publisher = Publisher(broker, clock: _clock);
        _fast.TryWrite(Capture()); _clock.Advance(FastAge);
        _fast.Complete(); _slow.Complete(); await Run(publisher);
        Assert.Equal(0, broker.Calls); Assert.Equal(0, publisher.GetDiagnostics().FastPublishCount);
        Assert.Equal(new MeterQueueDiagnostics(0, 0, 1), publisher.GetDiagnostics().FastQueue);
        Assert.Equal(new MeterQueueDiagnostics(0, 0, 0), publisher.GetDiagnostics().SlowQueue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Diagnostics_count_only_successful_completion_even_when_shutdown_was_requested(bool ignoresCancellation)
    {
        var broker = new Broker(); var publisher = Publisher(broker, clock: _clock);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        broker.BeforeSend = (_, token) =>
        {
            entered.TrySetResult(); return ignoresCancellation ? release.Task : release.Task.WaitAsync(token);
        };
        using var stop = new CancellationTokenSource(); var run = Run(publisher, stop.Token);
        _fast.TryWrite(Capture()); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, publisher.GetDiagnostics().FastPublishCount);
        stop.Cancel(); release.TrySetResult(); await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ignoresCancellation ? 1 : 0, publisher.GetDiagnostics().FastPublishCount);
    }

    [Fact]
    public async Task Both_streams_use_versioned_nonretained_qos_zero_and_distinct_window_contracts()
    {
        var broker = new Broker(); var publisher = Publisher(broker);
        var snapshot = Capture(); _fast.TryWrite(snapshot); _slow.TryWrite(snapshot);
        _fast.Complete(); _slow.Complete();
        await Run(publisher).WaitAsync(TimeSpan.FromSeconds(5));
        var messages = new[] { await Read(broker), await Read(broker) };
        Assert.All(messages, m => { Assert.False(m.Retain); Assert.Equal(MqttQualityOfServiceLevel.AtMostOnce, m.QualityOfServiceLevel); });
        using var fast = JsonDocument.Parse(messages.Single(m => m.Topic.EndsWith("/fast")).PayloadSegment);
        using var slow = JsonDocument.Parse(messages.Single(m => m.Topic.EndsWith("/slow")).PayloadSegment);
        Assert.Equal("example/pc/v2/meters/fast", messages.Single(m => m.Topic.EndsWith("/fast")).Topic);
        Assert.Equal("example/pc/v2/meters/slow", messages.Single(m => m.Topic.EndsWith("/slow")).Topic);
        Assert.Equal(50, fast.RootElement.GetProperty("sample_window_ms").GetInt32());
        Assert.Equal(1000, slow.RootElement.GetProperty("window_ms").GetInt32());
        Assert.Equal("pre", slow.RootElement.GetProperty("sources").GetProperty("strip:0").GetProperty("sensor_tap").GetString());
        Assert.Equal(fast.RootElement.GetProperty("session_id").GetString(), slow.RootElement.GetProperty("session_id").GetString());
        Assert.NotEqual(fast.RootElement.GetProperty("seq").GetInt64(), slow.RootElement.GetProperty("seq").GetInt64());
    }

    [Fact]
    public async Task Stalled_send_coalesces_pending_data_while_other_stream_can_publish()
    {
        var broker = new Broker(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        broker.BeforeSend = async (message, token) =>
        {
            if (message.Topic.EndsWith("/fast") && !release.Task.IsCompleted)
            { entered.TrySetResult(); await release.Task.WaitAsync(token); }
        };
        using var stop = new CancellationTokenSource(); var run = Run(Publisher(broker), stop.Token);
        _fast.TryWrite(Capture(1)); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (int i = 0; i < 100; i++) _fast.TryWrite(Capture(0.25f));
        _slow.TryWrite(Capture(0.1f));
        Assert.EndsWith("/slow", (await Read(broker)).Topic);
        release.TrySetResult();
        await Read(broker); // The already-started send cannot be recalled.
        using var latest = JsonDocument.Parse((await Read(broker)).PayloadSegment);
        Assert.Equal(-12.0412, latest.RootElement.GetProperty("sources").GetProperty("strip:0").GetProperty("pre_dbfs").GetDouble(), 4);
        Assert.Equal(3, broker.Calls);
        stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Stale_snapshots_are_skipped_without_consuming_sequence()
    {
        var broker = new Broker(); _fast.TryWrite(Capture()); _clock.Advance(FastAge); _fast.Complete();
        _slow.TryWrite(Capture()); _slow.Complete();
        await Run(Publisher(broker)).WaitAsync(TimeSpan.FromSeconds(5));
        using var json = JsonDocument.Parse((await Read(broker)).PayloadSegment);
        Assert.Equal(0, json.RootElement.GetProperty("seq").GetInt64()); Assert.Equal(1, broker.Calls);
    }

    [Fact]
    public async Task Cancelling_a_stalled_send_passes_token_and_awaits_its_exit()
    {
        var broker = new Broker(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool exited = false;
        broker.BeforeSend = async (_, token) =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { exited = true; }
        };
        using var stop = new CancellationTokenSource(); var run = Run(Publisher(broker), stop.Token);
        _fast.TryWrite(Capture()); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(exited); Assert.False(broker.Sent.Reader.TryRead(out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Send_failure_or_rejection_faults_session_and_cancels_idle_sibling(bool reject)
    {
        var broker = new Broker();
        if (reject) broker.Result = MqttClientPublishReasonCode.UnspecifiedError;
        else broker.BeforeSend = (_, _) => throw new IOException("synthetic failure");
        var run = Run(Publisher(broker)); _fast.TryWrite(Capture());
        if (reject) await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        else await Assert.ThrowsAsync<IOException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(run.IsFaulted); Assert.Equal(1, broker.Calls);
    }

    [Fact]
    public async Task Disconnected_client_is_not_used_and_publisher_cannot_restart()
    {
        var broker = new Broker { Connected = false }; var publisher = Publisher(broker);
        _fast.TryWrite(Capture());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(publisher).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, broker.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(publisher));
    }

    [Fact]
    public async Task Disabled_v2_does_not_wait_for_queues_or_use_client()
    {
        _settings.Enabled = false; var broker = new Broker { Connected = false };
        await Run(Publisher(broker)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, broker.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("example/+")]
    [InlineData("example/#")]
    [InlineData("example/\0")]
    public void Invalid_publish_base_is_rejected(string topic)
        => Assert.ThrowsAny<ArgumentException>(() => Publisher(new Broker(), topic));

    [Fact]
    public async Task Expiry_during_serialization_is_rechecked_before_send()
    {
        var broker = new Broker(); var clock = new PublishClock { BeforeTimestamp = () => _clock.Advance(FastAge) };
        _fast.TryWrite(Capture()); _fast.Complete(); _slow.Complete();
        await Run(Publisher(broker, clock: clock)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, broker.Calls);
    }

    [Fact]
    public async Task Timestamp_is_assigned_at_send_and_disabled_stream_is_not_consumed()
    {
        _settings.SlowEnabled = false;
        var broker = new Broker(); var clock = new PublishClock();
        var snapshot = Capture(); _fast.TryWrite(snapshot); _slow.TryWrite(snapshot);
        _clock.Advance(TimeSpan.FromMilliseconds(100)); _fast.Complete();
        await Run(Publisher(broker, clock: clock)).WaitAsync(TimeSpan.FromSeconds(5));
        using var payload = JsonDocument.Parse((await Read(broker)).PayloadSegment);
        Assert.Equal(clock.Utc, payload.RootElement.GetProperty("published_at_utc").GetDateTimeOffset());
        Assert.Same(snapshot, await _slow.ReadAsync()); Assert.Equal(1, broker.Calls);
    }

    [Fact]
    public async Task One_send_failure_cancels_and_drains_the_other_inflight_send()
    {
        var broker = new Broker(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool exited = false;
        broker.BeforeSend = async (message, token) =>
        {
            if (message.Topic.EndsWith("/slow")) throw new IOException("synthetic failure");
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { exited = true; }
        };
        var run = Run(Publisher(broker)); _fast.TryWrite(Capture());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _slow.TryWrite(Capture());
        await Assert.ThrowsAsync<IOException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(exited); Assert.Equal(2, broker.Calls);
    }

    [Theory]
    [InlineData("strip:0", "post_mute", MeterTap.PostMute)]
    [InlineData("bus:7", "output", MeterTap.Output)]
    public void Slow_sensor_tap_is_explicit_even_when_unavailable(string id, string tapName, MeterTap tap)
    {
        _settings.Sources = new() { new() { Id = id, Enabled = true, MeterTaps = new() { tapName } } };
        var builder = new AggregateFrameBuilder(Registry(), _settings);
        var snapshot = new MeterWindowSnapshot(TimeSpan.FromSeconds(1),
            new[] { new MeterWindowReading(new(id, tap, false, null, null), null, null) });
        using var json = JsonDocument.Parse(builder.BuildSlowFrame(snapshot, 1000, DateTimeOffset.UtcNow));
        var source = json.RootElement.GetProperty("sources").GetProperty(id);
        Assert.Equal(tapName, source.GetProperty("sensor_tap").GetString());
        Assert.False(source.GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, source.GetProperty(tapName + "_dbfs").ValueKind);
    }

    [Theory]
    [InlineData(50, 3000)]
    [InlineData(750, 1000)]
    public async Task Budget_must_exceed_each_enabled_stream_window(int fastMs, int slowMs)
    {
        var broker = new Broker(); var publisher = Publisher(broker);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => publisher.RunAsync(_fast, _slow, _settings,
            TimeSpan.FromMilliseconds(fastMs), TimeSpan.FromMilliseconds(slowMs)));
        Assert.Equal(0, broker.Calls);
    }

    [Fact]
    public async Task Fast_and_slow_cannot_share_a_single_reader_queue()
    {
        var broker = new Broker(); var publisher = Publisher(broker); _fast.Complete();
        await Assert.ThrowsAsync<ArgumentException>(() => publisher.RunAsync(_fast, _fast, _settings, FastAge, SlowAge));
        Assert.Equal(0, broker.Calls);
    }

    private sealed class PublishClock : TimeProvider
    {
        public DateTimeOffset Utc { get; } = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        public Action? BeforeTimestamp;
        public override DateTimeOffset GetUtcNow() { BeforeTimestamp?.Invoke(); return Utc; }
    }

}
