using System.Text.Json;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace VoicemeeterMqttBridge.Tests;

public sealed class MeterMetadataPublicationTests
{
    private readonly TelemetryBroker _broker = new();
    private readonly TelemetryTimerClock _clock = new();
    private readonly Levels _levels = new();
    private readonly MeteringV2Settings _settings = new()
    {
        Enabled = true, FastEnabled = true, SlowEnabled = false,
        Sources = new() { new() { Id = "strip:0", Enabled = true, MeterTaps = new() { "pre" } } }
    };
    private Task Run(MeterTelemetrySupervisor supervisor, CancellationToken token)
        => supervisor.RunAsync(SourceRegistry.Read(new SourceRegistryTests.Metadata(), _settings), _settings,
            "example/pc", "2.0.0-dev", TimeSpan.FromMilliseconds(750), TimeSpan.FromSeconds(3), token);

    [Fact]
    public async Task Metadata_is_retained_qos_one_and_precedes_sampling_with_matching_session()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _broker.BeforeSend = async (_, token) => { entered.TrySetResult(); await release.Task.WaitAsync(token); };
        using var stop = new CancellationTokenSource();
        var supervisor = new MeterTelemetrySupervisor(_broker.Client, _levels, _clock);
        var run = Run(supervisor, stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _clock.Advance(1000);
        Assert.Equal(0, _levels.Calls); Assert.Equal(0, _clock.ActiveTimers);
        release.SetResult();
        var metadata = await _broker.Sent.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("example/pc/v2/metadata", metadata.Topic);
        Assert.True(metadata.Retain);
        Assert.Equal(MqttQualityOfServiceLevel.AtLeastOnce, metadata.QualityOfServiceLevel);
        using var json = JsonDocument.Parse(metadata.PayloadSegment);
        Assert.Equal(16, json.RootElement.GetProperty("sources").GetArrayLength());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("bridge_version").GetString()));
        // Starting the timer, not a sleep, synchronizes metadata completion with the sampler.
        await _clock.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _clock.Advance(50);
        var frame = await _broker.Sent.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        using var meter = JsonDocument.Parse(frame.PayloadSegment);
        Assert.Equal(json.RootElement.GetProperty("session_id").GetString(), meter.RootElement.GetProperty("session_id").GetString());
        Assert.Equal(0, meter.RootElement.GetProperty("seq").GetInt64());
        stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Metadata_failure_never_starts_sampling(bool rejection)
    {
        if (rejection) _broker.Result = MqttClientPublishReasonCode.NotAuthorized;
        else _broker.BeforeSend = (_, _) => throw new IOException("metadata failed");
        var run = Run(new(_broker.Client, _levels, _clock), CancellationToken.None);
        if (rejection) await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        else await Assert.ThrowsAsync<IOException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, _levels.Calls); Assert.Equal(0, _clock.ActiveTimers);
        Assert.Equal(1, _broker.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_during_metadata_drains_actual_send_before_restart(bool ignoresCancellation)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _broker.BeforeSend = (_, token) => ignoresCancellation ? release.Task : release.Task.WaitAsync(token);
        var supervisor = new MeterTelemetrySupervisor(_broker.Client, _levels, _clock);
        using var stop = new CancellationTokenSource(); var run = Run(supervisor, stop.Token);
        Assert.Equal(1, _broker.Calls);
        stop.Cancel();
        if (ignoresCancellation)
        {
            Assert.False(run.IsCompleted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Run(supervisor, CancellationToken.None));
            release.SetResult();
        }
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, _clock.ActiveTimers); Assert.Equal(0, _levels.Calls);
        _settings.Enabled = false;
        await Run(supervisor, CancellationToken.None);
    }

    [Fact]
    public async Task Disconnect_after_metadata_prevents_sampling()
    {
        _broker.BeforeSend = (_, _) => { _broker.Connected = false; return Task.CompletedTask; };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(new(_broker.Client, _levels, _clock), CancellationToken.None));
        Assert.Equal(0, _levels.Calls); Assert.Equal(0, _clock.ActiveTimers);
    }

    [Fact]
    public async Task Restart_replaces_retained_metadata_with_new_session_and_updated_label()
    {
        var supervisor = new MeterTelemetrySupervisor(_broker.Client, _levels, _clock);
        using var firstStop = new CancellationTokenSource(); var first = Run(supervisor, firstStop.Token);
        using var original = JsonDocument.Parse((await _broker.Sent.Reader.ReadAsync()).PayloadSegment);
        firstStop.Cancel(); await first.WaitAsync(TimeSpan.FromSeconds(5));
        _settings.Sources[0].DisplayLabel = "Microphone 日本語";
        using var nextStop = new CancellationTokenSource(); var next = Run(supervisor, nextStop.Token);
        var message = await _broker.Sent.Reader.ReadAsync();
        using var updated = JsonDocument.Parse(message.PayloadSegment);
        Assert.True(message.Retain); Assert.Equal("example/pc/v2/metadata", message.Topic);
        Assert.NotEqual(original.RootElement.GetProperty("session_id").GetString(), updated.RootElement.GetProperty("session_id").GetString());
        var source = updated.RootElement.GetProperty("sources").EnumerateArray().Single(s => s.GetProperty("id").GetString() == "strip:0");
        Assert.Equal("Microphone 日本語", source.GetProperty("label").GetString());
        nextStop.Cancel(); await next.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Invalid_publication_budget_sends_no_metadata()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new MeterTelemetrySupervisor(_broker.Client, _levels, _clock)
            .RunAsync(SourceRegistry.Read(new SourceRegistryTests.Metadata(), _settings), _settings,
                "example/pc", "2.0.0-dev", TimeSpan.Zero, TimeSpan.FromSeconds(3)));
        Assert.Equal(0, _broker.Calls); Assert.Equal(0, _clock.ActiveTimers);
    }

    private sealed class Levels : IVoicemeeterLevels
    {
        public int Calls;
        public float GetLevel(int type, int channel) { Interlocked.Increment(ref Calls); return 1; }
    }
}
