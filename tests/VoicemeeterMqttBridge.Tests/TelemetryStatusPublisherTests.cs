using System.Text.Json;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace VoicemeeterMqttBridge.Tests;

public sealed class TelemetryStatusPublisherTests
{
    private readonly TelemetryBroker _broker = new();
    private static AggregateFrameBuilder Frames()
    {
        var settings = new MeteringV2Settings();
        return new(SourceRegistry.Read(new SourceRegistryTests.Metadata(), settings), settings);
    }

    [Theory]
    [InlineData(TelemetrySessionState.Starting, "starting")]
    [InlineData(TelemetrySessionState.Running, "running")]
    [InlineData(TelemetrySessionState.Stopped, "stopped")]
    [InlineData(TelemetrySessionState.Faulted, "faulted")]
    public async Task Status_is_retained_and_shares_metadata_session_without_claiming_engine_health(
        TelemetrySessionState state, string expected)
    {
        var frames = Frames();
        await new TelemetryStatusPublisher(_broker.Client, frames, "test/pc", "2.0-dev").PublishAsync(state);
        Assert.True(_broker.Sent.Reader.TryRead(out var message));
        Assert.Equal("test/pc/v2/status", message.Topic);
        Assert.True(message.Retain);
        Assert.Equal(MqttQualityOfServiceLevel.AtLeastOnce, message.QualityOfServiceLevel);
        using var json = JsonDocument.Parse(message.PayloadSegment);
        using var metadata = JsonDocument.Parse(frames.BuildMetadata("2.0-dev"));
        var root = json.RootElement;
        Assert.Equal(metadata.RootElement.GetProperty("session_id").GetString(), root.GetProperty("session_id").GetString());
        Assert.Equal(expected, root.GetProperty("session_state").GetString());
        Assert.Equal(2, root.GetProperty("schema").GetInt32());
        Assert.Equal("2.0-dev", root.GetProperty("bridge_version").GetString());
        Assert.True(root.GetProperty("broker_connected_at_send").GetBoolean());
        foreach (var field in new[] { "engine_state", "actual_fast_hz", "actual_slow_hz", "diagnostics" })
            Assert.Equal(JsonValueKind.Null, root.GetProperty(field).ValueKind);
        Assert.Equal(TimeSpan.Zero, root.GetProperty("published_at_utc").GetDateTimeOffset().Offset);
        Assert.NotEqual(frames.SessionId, Frames().SessionId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Failure_is_observable_and_releases_gate_without_retry(bool rejection)
    {
        var publisher = new TelemetryStatusPublisher(_broker.Client, Frames(), "test", "2");
        if (rejection) _broker.Result = MqttClientPublishReasonCode.NotAuthorized;
        else _broker.BeforeSend = (_, _) => throw new IOException("transport failed");
        if (rejection) await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(TelemetrySessionState.Running));
        else await Assert.ThrowsAsync<IOException>(() => publisher.PublishAsync(TelemetrySessionState.Running));
        Assert.Equal(1, _broker.Calls);
        _broker.Result = MqttClientPublishReasonCode.Success;
        _broker.BeforeSend = null;
        await publisher.PublishAsync(TelemetrySessionState.Faulted);
        Assert.Equal(2, _broker.Calls);
    }

    [Fact]
    public async Task Cancellation_does_not_release_gate_until_uncooperative_send_finishes()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _broker.BeforeSend = (_, _) => release.Task;
        var publisher = new TelemetryStatusPublisher(_broker.Client, Frames(), "test", "2");
        using var stop = new CancellationTokenSource();
        var send = publisher.PublishAsync(TelemetrySessionState.Running, stop.Token);
        stop.Cancel();
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(TelemetrySessionState.Stopped));
        Assert.False(send.IsCompleted);
        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        await publisher.PublishAsync(TelemetrySessionState.Stopped);
        Assert.Equal(2, _broker.Calls);
    }

    [Fact]
    public async Task Precancellation_disconnection_and_invalid_state_never_send()
    {
        var publisher = new TelemetryStatusPublisher(_broker.Client, Frames(), "test", "2");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.PublishAsync(TelemetrySessionState.Starting, new(true)));
        _broker.Connected = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(TelemetrySessionState.Running));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => publisher.PublishAsync((TelemetrySessionState)99));
        Assert.Equal(0, _broker.Calls);
    }

    [Fact]
    public async Task Disconnect_during_send_is_not_reported_as_success()
    {
        _broker.BeforeSend = (_, _) => { _broker.Connected = false; return Task.CompletedTask; };
        var publisher = new TelemetryStatusPublisher(_broker.Client, Frames(), "test", "2");
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(TelemetrySessionState.Stopped));
        Assert.Equal(1, _broker.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("test/+")]
    [InlineData("test/#")]
    [InlineData("test\0")]
    public void Invalid_topics_are_rejected(string topic)
        => Assert.ThrowsAny<ArgumentException>(() => new TelemetryStatusPublisher(_broker.Client, Frames(), topic, "2"));
}
