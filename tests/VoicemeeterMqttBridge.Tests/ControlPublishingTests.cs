using MQTTnet.Protocol;
using MQTTnet.Client;
using System.Text.Json;

namespace VoicemeeterMqttBridge.Tests;

public sealed class ControlPublishingTests
{
    private readonly AppSettings _settings = new() { BaseTopic = "test/bridge", MqttHost = "unused.invalid" };
    private readonly FakeRemote _remote = new();
    private readonly MqttRecorder _mqtt = new();
    private readonly List<string> _logs = new();
    private readonly ManualTimeProvider _time = new();
    private BridgeService Bridge() => new(_settings, _remote, _mqtt.Client, _logs.Add, _time);

    [Theory]
    [InlineData(-1)]
    [InlineData(-2)]
    [InlineData(-3)]
    public async Task Failed_set_does_not_publish_or_read_back_success(int result)
    {
        _remote.SetResult = result;
        await Bridge().SetParameterAsync("Strip[0].Gain", -6, publish: true);
        Assert.Single(_remote.Writes);
        Assert.Empty(_remote.ParameterReads);
        Assert.Empty(_mqtt.Messages);
        Assert.Contains(_logs, line => line.Contains($"rc={result}"));
    }

    [Theory]
    [InlineData("strip_0_gain", "NaN")]
    [InlineData("strip_0_gain", "Infinity")]
    [InlineData("strip_0_gain", "-Infinity")]
    [InlineData("strip_0_mute", "NaN")]
    [InlineData("strip_0_mute", "Infinity")]
    public async Task Nonfinite_command_never_reaches_remote(string id, string payload)
    {
        await Bridge().HandleMqttCommandAsync($"test/bridge/parameter/{id}/set", payload);
        Assert.Empty(_remote.Writes);
        Assert.Empty(_mqtt.Messages);
        Assert.NotEmpty(_logs);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public async Task Nonfinite_read_never_becomes_retained_state(float value)
    {
        _remote.Parameters["Strip[0].Gain"] = value;
        await Bridge().PublishParameterStateAsync("Strip[0].Gain");
        Assert.Empty(_mqtt.Messages);
        Assert.NotEmpty(_logs);
    }

    [Fact]
    public async Task Command_publishes_actual_readback_instead_of_requested_value()
    {
        _remote.ReadbackAfterSet = _ => -5.9f;
        await Bridge().SetParameterAsync("Strip[0].Gain", -6, publish: true);
        var message = Assert.Single(_mqtt.Messages);
        Assert.Equal("-5.9", message.Payload);
        Assert.True(message.Retain);
        Assert.Equal(MqttQualityOfServiceLevel.AtMostOnce, message.Qos);
    }

    [Fact]
    public async Task Dirty_scan_sends_only_changed_controls_and_accumulates_small_changes()
    {
        var bridge = Bridge();
        await bridge.PublishAllStateAsync();
        Assert.Equal(139, _mqtt.Messages.Count);
        _mqtt.Messages.Clear();
        _remote.DirtyResult = 1;
        await bridge.PollControlStateAsync();
        Assert.Empty(_mqtt.Messages);
        _remote.Parameters["Strip[0].Gain"] = 0.04f;
        await bridge.PollControlStateAsync();
        Assert.Empty(_mqtt.Messages);
        _remote.Parameters["Strip[0].Gain"] = 0.08f;
        _remote.Parameters["Bus[7].Mute"] = 1;
        await bridge.PollControlStateAsync();
        Assert.Equal(new[] { "test/bridge/parameter/strip_0_gain/state", "test/bridge/parameter/bus_7_mute/state" },
            _mqtt.Messages.Select(m => m.Topic));
        Assert.Equal(new[] { "0.08", "ON" }, _mqtt.Messages.Select(m => m.Payload));
    }

    [Fact]
    public async Task Switch_cache_compares_the_legacy_wire_representation()
    {
        var bridge = Bridge();
        await bridge.PublishAllStateAsync();
        _mqtt.Messages.Clear();
        _remote.DirtyResult = 1;
        _remote.Parameters["Bus[0].Mono"] = 0.4f;
        await bridge.PollControlStateAsync();
        Assert.Empty(_mqtt.Messages);
        _remote.Parameters["Bus[0].Mono"] = 2;
        await bridge.PollControlStateAsync();
        Assert.Equal("ON", Assert.Single(_mqtt.Messages).Payload);
        _mqtt.Messages.Clear();
        _remote.Parameters["Bus[0].Mono"] = 1;
        await bridge.PollControlStateAsync();
        Assert.Empty(_mqtt.Messages);
    }

    [Theory]
    [InlineData(-2, "Engine unavailable")]
    [InlineData(-1, "Remote API dirty error: -1")]
    public async Task Negative_dirty_never_reads_controls_and_recovery_forces_snapshot(int dirty, string status)
    {
        var bridge = Bridge();
        await bridge.PublishAllStateAsync();
        _mqtt.Messages.Clear();
        _remote.ParameterReads.Clear();
        _remote.DirtyResult = dirty;
        await bridge.PollControlStateAsync();
        await bridge.PollControlStateAsync();
        Assert.Empty(_remote.ParameterReads);
        Assert.Empty(_mqtt.Messages);
        Assert.Equal(status, bridge.VoicemeeterStatus);
        Assert.Single(_logs); // Repeated failures do not flood the log.
        _remote.DirtyResult = 0;
        await bridge.PollControlStateAsync();
        Assert.Equal("Connected", bridge.VoicemeeterStatus);
        Assert.Equal(139, _mqtt.Messages.Count);
    }

    [Fact]
    public async Task Zero_dirty_does_not_read_until_reconciliation_is_due()
    {
        var bridge = Bridge();
        await bridge.PublishAllStateAsync();
        _remote.ParameterReads.Clear();
        _mqtt.Messages.Clear();
        _remote.Parameters["Bus[1].Gain"] = -3;
        _time.Advance(TimeSpan.FromMilliseconds(29999));
        await bridge.PollControlStateAsync();
        Assert.Empty(_remote.ParameterReads);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        await bridge.PollControlStateAsync();
        Assert.Equal("-3", Assert.Single(_mqtt.Messages).Payload);
        Assert.Equal(139, _remote.ParameterReads.Count);
    }

    [Fact]
    public async Task Reconciliation_can_be_disabled_and_survives_settings_round_trip()
    {
        _settings.ControlReconcileIntervalMs = 0;
        Assert.Equal(0, JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_settings))!.ControlReconcileIntervalMs);
        var bridge = Bridge();
        await bridge.PublishAllStateAsync();
        _remote.ParameterReads.Clear();
        _time.Advance(TimeSpan.FromHours(1));
        await bridge.PollControlStateAsync();
        Assert.Empty(_remote.ParameterReads);
    }

    [Fact]
    public async Task Full_resync_bypasses_the_cache()
    {
        var bridge = Bridge();
        await bridge.PublishAllStateAsync();
        _mqtt.Messages.Clear();
        await bridge.PublishAllStateAsync();
        Assert.Equal(139, _mqtt.Messages.Count);
        Assert.All(_mqtt.Messages, m => Assert.True(m.Retain));
    }

    [Fact]
    public async Task Read_failure_is_isolated_and_recovery_republishes_even_the_same_value()
    {
        var bridge = Bridge();
        await bridge.PublishAllStateAsync();
        _mqtt.Messages.Clear();
        _remote.DirtyResult = 1;
        _remote.FailedReads.Add("Strip[0].Gain");
        _remote.Parameters["Bus[0].Gain"] = -3;
        await bridge.PollControlStateAsync();
        Assert.Equal("-3", Assert.Single(_mqtt.Messages).Payload);
        Assert.Equal(1, bridge.ControlReadErrorCount);
        await bridge.PollControlStateAsync();
        Assert.Single(_logs, l => l.StartsWith("Control read failed:"));
        _mqtt.Messages.Clear();
        _remote.FailedReads.Clear();
        await bridge.PollControlStateAsync();
        Assert.Equal("test/bridge/parameter/strip_0_gain/state", Assert.Single(_mqtt.Messages).Topic);
        Assert.Equal(0, bridge.ControlReadErrorCount);
    }

    [Theory]
    [InlineData("offline")]
    [InlineData("exception")]
    [InlineData("rejected")]
    public async Task Failed_send_is_not_cached_and_reconciliation_retries_latest_value(string failure)
    {
        var bridge = Bridge();
        await bridge.PublishAllStateAsync();
        _mqtt.Messages.Clear();
        _remote.DirtyResult = 1;
        _remote.Parameters["Strip[0].Gain"] = -3;
        _mqtt.IsConnected = failure != "offline";
        _mqtt.FailTopic = failure == "exception" ? "test/bridge/parameter/strip_0_gain/state" : null;
        _mqtt.PublishReason = failure == "rejected" ? MqttClientPublishReasonCode.UnspecifiedError : MqttClientPublishReasonCode.Success;
        await bridge.PollControlStateAsync();
        Assert.Empty(_mqtt.Messages);
        _mqtt.IsConnected = true;
        _mqtt.FailTopic = null;
        _mqtt.PublishReason = MqttClientPublishReasonCode.Success;
        _remote.DirtyResult = 0;
        _remote.Parameters["Strip[0].Gain"] = -6;
        _time.Advance(TimeSpan.FromSeconds(30));
        await bridge.PollControlStateAsync();
        Assert.Equal("-6", Assert.Single(_mqtt.Messages).Payload);
    }

    [Fact]
    public async Task Command_and_scan_cannot_publish_in_reverse_order()
    {
        var bridge = Bridge();
        await bridge.PublishAllStateAsync();
        _mqtt.Messages.Clear();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _mqtt.BeforePublish = _ => { entered.TrySetResult(); return release.Task; };
        _remote.DirtyResult = 1;
        _remote.Parameters["Strip[0].Gain"] = -3;
        Task scan = bridge.PollControlStateAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task command = bridge.SetParameterAsync("Strip[0].Gain", -6, publish: true);
        Assert.Empty(_remote.Writes);
        release.SetResult();
        await Task.WhenAll(scan, command).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "-3", "-6" }, _mqtt.Messages.Select(m => m.Payload));
        _mqtt.Messages.Clear();
        await bridge.PollControlStateAsync();
        Assert.Empty(_mqtt.Messages);
    }

    [Fact]
    public async Task Generic_legacy_endpoint_still_accepts_unmapped_finite_parameters()
    {
        await Bridge().HandleMqttCommandAsync("test/bridge/set", """{"parameter":"Bus[0].Mono","value":2}""");
        Assert.Equal(("Bus[0].Mono", 2f), Assert.Single(_remote.Writes));
        Assert.Equal("ON", Assert.Single(_mqtt.Messages).Payload);
        _remote.Writes.Clear();
        _mqtt.Messages.Clear();
        await Bridge().HandleMqttCommandAsync("test/bridge/set", """{"parameter":"Strip[0].EQGain1","value":3}""");
        Assert.Equal(("Strip[0].EQGain1", 3f), Assert.Single(_remote.Writes));
        Assert.Empty(_mqtt.Messages);
    }

    [Fact]
    public async Task New_MQTT_connection_forces_snapshot_after_cache_is_populated()
    {
        _settings.HomeAssistantDiscovery = false;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bridge = new BridgeService(_settings, _remote, _mqtt.Client, line =>
        {
            if (line == "MQTT post-connect setup complete.") completed.TrySetResult();
        }, _time);
        await bridge.PublishAllStateAsync();
        _mqtt.Messages.Clear();
        await _mqtt.RaiseConnectedAsync();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("online", _mqtt.Messages[0].Payload);
        Assert.Equal(139, _mqtt.Messages.Count(m => m.Topic.EndsWith("/state")));
        Assert.Equal(140, _mqtt.Messages.Count);
    }

    [Fact]
    public async Task Failed_forced_snapshot_of_unchanged_value_is_retried()
    {
        var bridge = Bridge();
        await bridge.PublishAllStateAsync();
        _mqtt.Messages.Clear();
        _mqtt.FailTopic = "test/bridge/parameter/strip_0_gain/state";
        await bridge.PublishAllStateAsync();
        Assert.Equal(138, _mqtt.Messages.Count);
        _mqtt.Messages.Clear();
        _mqtt.FailTopic = null;
        _time.Advance(TimeSpan.FromSeconds(30));
        await bridge.PollControlStateAsync();
        Assert.Equal("test/bridge/parameter/strip_0_gain/state", Assert.Single(_mqtt.Messages).Topic);
    }

    [Fact]
    public async Task Dirty_exception_reports_failure_and_releases_lock_for_recovery()
    {
        var bridge = Bridge();
        await bridge.PublishAllStateAsync();
        _mqtt.Messages.Clear();
        _remote.ParameterReads.Clear();
        _remote.DirtyException = new InvalidOperationException("Synthetic API failure");
        await bridge.PollControlStateAsync();
        Assert.Empty(_mqtt.Messages);
        Assert.Empty(_remote.ParameterReads);
        Assert.Equal("Remote API poll error: Synthetic API failure", bridge.VoicemeeterStatus);
        _remote.DirtyException = null;
        await bridge.PollControlStateAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Connected", bridge.VoicemeeterStatus);
        Assert.Equal(139, _mqtt.Messages.Count);
    }

    [Fact]
    public async Task Unloaded_native_adapter_cannot_report_a_healthy_poll()
    {
        var remote = new VoicemeeterRemote(); // No Load/Login or native call.
        var bridge = new BridgeService(_settings, remote, _mqtt.Client, _logs.Add, _time);
        await bridge.PollControlStateAsync();
        Assert.False(remote.IsLoaded);
        Assert.Equal("Remote API poll error: Remote API is not loaded.", bridge.VoicemeeterStatus);
        Assert.Empty(_mqtt.Messages);
    }
}
