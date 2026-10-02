using System.Text.Json;
using MQTTnet;

namespace VoicemeeterMqttBridge.Tests;

public sealed class MeterTelemetryRuntimeTests
{
    private static AppSettings Settings(bool enabled = true) => new()
    {
        BaseTopic = "test/runtime", HomeAssistantDiscovery = false,
        MeteringV2 = new() { Enabled = enabled, FastEnabled = true, SlowEnabled = false,
            Sources = new() { new() { Id = "strip:0", Enabled = true } } }
    };
    private static async Task<MqttApplicationMessage> Metadata(TelemetryBroker broker)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var message = await broker.Sent.Reader.ReadAsync(timeout.Token);
            if (message.Topic.EndsWith("/metadata")) return message;
        }
    }
    [Fact]
    public async Task Disabled_configuration_never_touches_native_or_broker()
    {
        var remote = new RecordingRemote(); using var owner = new RemoteApiOwner(remote, _ => { });
        var broker = new TelemetryBroker();
        var runtime = new MeterTelemetryRuntime(Settings(false), owner, broker.Client, () => true, () => 1, _ => { });
        await runtime.RunAsync(CancellationToken.None);
        Assert.Equal("Disabled", runtime.Status); Assert.Empty(remote.Calls); Assert.Equal(0, broker.Calls);
    }
    [Fact]
    public async Task Changed_connection_epoch_replaces_session_and_shutdown_drains_before_logout()
    {
        var remote = new RecordingRemote(); using var owner = new RemoteApiOwner(remote, _ => { }); owner.Load(); owner.Login();
        var broker = new TelemetryBroker(); long epoch = 1;
        var runtime = new MeterTelemetryRuntime(Settings(), owner, broker.Client, () => true,
            () => Interlocked.Read(ref epoch), _ => { });
        using var stop = new CancellationTokenSource(); var run = runtime.RunAsync(stop.Token);
        try
        {
            var first = await Metadata(broker); Interlocked.Increment(ref epoch);
            var second = await Metadata(broker);
            using var a = JsonDocument.Parse(first.PayloadSegment); using var b = JsonDocument.Parse(second.PayloadSegment);
            Assert.NotEqual(a.RootElement.GetProperty("session_id").GetString(), b.RootElement.GetProperty("session_id").GetString());
        }
        finally { stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5)); }
        owner.Logout(); Assert.Equal("Logout", remote.Calls.Last().Name);
        Assert.Single(remote.Calls.Select(c => c.Thread).Distinct()); Assert.Equal("Stopped", runtime.Status);
    }
    [Fact]
    public async Task Cancellation_waits_for_an_uncooperative_startup_send()
    {
        var remote = new RecordingRemote(); using var owner = new RemoteApiOwner(remote, _ => { }); owner.Load(); owner.Login();
        var broker = new TelemetryBroker();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        broker.BeforeSend = async (message, _) =>
        {
            if (message.Topic.EndsWith("/metadata")) { entered.TrySetResult(); await release.Task; }
        };
        using var stop = new CancellationTokenSource();
        var runtime = new MeterTelemetryRuntime(Settings(), owner, broker.Client, () => true, () => 1, _ => { });
        var run = runtime.RunAsync(stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); stop.Cancel();
        try { Assert.False(run.IsCompleted); Assert.Equal(0, remote.Count("GetLevel")); }
        finally { release.TrySetResult(); await run.WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.Equal(0, remote.Count("GetLevel"));
    }
    [Fact]
    public async Task Unsupported_engine_never_publishes_metadata_or_reads_levels()
    {
        var remote = new RecordingRemote { Identity = new(2, new Version(2, 1, 3, 0)) };
        using var owner = new RemoteApiOwner(remote, _ => { }); owner.Load(); owner.Login();
        var broker = new TelemetryBroker(); using var stop = new CancellationTokenSource();
        var runtime = new MeterTelemetryRuntime(Settings(), owner, broker.Client, () => true, () => 1, _ => { });
        var run = runtime.RunAsync(stop.Token);
        Assert.StartsWith("Retrying", runtime.Status); stop.Cancel(); await run;
        Assert.Equal(0, broker.Calls); Assert.Equal(0, remote.Count("GetLevel"));
    }
}

