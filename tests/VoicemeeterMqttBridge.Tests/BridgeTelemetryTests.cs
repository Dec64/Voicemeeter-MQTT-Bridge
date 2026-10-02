using System.Text.Json;
using MQTTnet;

namespace VoicemeeterMqttBridge.Tests;

public sealed class BridgeTelemetryTests
{
    internal static AppSettings Settings(bool enabled = true) => new()
    {
        BaseTopic = "test/bridge-runtime", MqttHost = "unused.invalid", HomeAssistantDiscovery = false,
        PublishMeters = false, StartPotatoWithApp = false,
        MeteringV2 = new() { Enabled = enabled, FastEnabled = true, SlowEnabled = false,
            Sources = new() { new() { Id = "strip:0", Enabled = true } } }
    };
    internal static async Task<MqttApplicationMessage> ReadTopic(TelemetryBroker broker, string suffix)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var message = await broker.Sent.Reader.ReadAsync(timeout.Token);
            if (message.Topic.EndsWith(suffix)) return message;
        }
    }
    [Fact]
    public async Task Application_startup_publishes_metadata_then_real_pipeline_frames_and_reconnects()
    {
        var broker = new TelemetryBroker { Connected = false }; var remote = new RecordingRemote();
        var bridge = new BridgeService(Settings(), remote, broker.Client, _ => { });
        await bridge.StartAsync();
        try
        {
            var metadata = await ReadTopic(broker, "/v2/metadata");
            var frame = await ReadTopic(broker, "/v2/meters/fast");
            using var doc = JsonDocument.Parse(frame.PayloadSegment);
            Assert.True(doc.RootElement.GetProperty("sources").GetProperty("strip:0").GetProperty("available").GetBoolean());
            Assert.False(frame.Retain);
            await bridge.ReconnectAsync();
            var replacement = await ReadTopic(broker, "/v2/metadata");
            using var a = JsonDocument.Parse(metadata.PayloadSegment); using var b = JsonDocument.Parse(replacement.PayloadSegment);
            Assert.NotEqual(a.RootElement.GetProperty("session_id").GetString(), b.RootElement.GetProperty("session_id").GetString());
        }
        finally { await bridge.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.Equal("Logout", remote.Calls.Last().Name); Assert.Equal(1, remote.Count("Login"));
        Assert.Single(remote.Calls.Select(c => c.Thread).Distinct()); Assert.False(broker.Connected);
    }
    [Fact]
    public async Task Stop_drains_fast_send_before_mqtt_disconnect_and_native_logout()
    {
        var broker = new TelemetryBroker { Connected = false }; var remote = new RecordingRemote();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        broker.BeforeSend = async (message, _) =>
        {
            if (message.Topic.EndsWith("/meters/fast")) { entered.TrySetResult(); await release.Task; }
        };
        var bridge = new BridgeService(Settings(), remote, broker.Client, _ => { });
        await bridge.StartAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var stop = bridge.StopAsync();
            Assert.False(stop.IsCompleted); Assert.True(broker.Connected); Assert.Equal(0, remote.Count("Logout"));
            release.TrySetResult(); await stop.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { release.TrySetResult(); await bridge.StopAsync(); }
        Assert.Equal("Logout", remote.Calls.Last().Name); Assert.False(broker.Connected);
    }
    [Fact]
    public async Task Default_disabled_flag_preserves_legacy_startup_without_v2_reads_or_publications()
    {
        var broker = new TelemetryBroker { Connected = false }; var remote = new RecordingRemote();
        var bridge = new BridgeService(Settings(false), remote, broker.Client, _ => { });
        await bridge.StartAsync();
        await ReadTopic(broker, "/parameter/strip_0_gain/state");
        await bridge.StopAsync();
        Assert.Equal("Disabled", bridge.TelemetryStatus);
        Assert.Equal(0, remote.Count("Identity")); Assert.Equal(0, remote.Count("GetLevel"));
        while (broker.Sent.Reader.TryRead(out var message)) Assert.DoesNotContain("/v2/", message.Topic);
    }
}
