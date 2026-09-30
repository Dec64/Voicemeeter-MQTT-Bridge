using System.Text.Json;
using MQTTnet.Protocol;

namespace VoicemeeterMqttBridge.Tests;

public sealed class LegacyCompatibilityTests
{
    private static AppSettings Settings() => new() { BaseTopic = "test/bridge", MqttHost = "unused.invalid" };
    private static BridgeService Bridge(AppSettings settings, FakeRemote remote, MqttRecorder recorder)
        => new(settings, remote, recorder.Client, _ => { });

    [Fact]
    public async Task Legacy_meters_keep_raw_keys_linear_values_and_non_retained_delivery()
    {
        var remote = new FakeRemote();
        for (int ch = 0; ch < 34; ch++) remote.Levels[(0, ch)] = (ch + 1) / 100f;
        for (int ch = 0; ch < 64; ch++) remote.Levels[(3, ch)] = (ch + 1) / 200f;
        var mqtt = new MqttRecorder();
        await Bridge(Settings(), remote, mqtt).PublishMetersAsync();
        var message = Assert.Single(mqtt.Messages);
        Assert.Equal("test/bridge/meters", message.Topic);
        Assert.False(message.Retain);
        Assert.Equal(MqttQualityOfServiceLevel.AtMostOnce, message.Qos);
        var values = JsonSerializer.Deserialize<Dictionary<string, float>>(message.Payload)!;
        Assert.Equal(98, values.Count);
        for (int ch = 0; ch < 34; ch++) Assert.Equal((ch + 1) / 100f, values[$"in_{ch}"]);
        for (int ch = 0; ch < 64; ch++) Assert.Equal((ch + 1) / 200f, values[$"out_{ch}"]);
    }

    [Fact]
    public async Task Discovery_keeps_all_155_legacy_identities_topics_and_components()
    {
        var settings = Settings();
        var mqtt = new MqttRecorder();
        await Bridge(settings, new FakeRemote(), mqtt).PublishDiscoveryAsync();
        // Independent legacy contract, including known unsupported virtual Comp/Gate IDs.
        var expected = new Dictionary<string, string>();
        for (int i = 0; i < 8; i++)
        {
            foreach (string key in new[] { "gain", "comp", "gate" }) expected[$"strip_{i}_{key}"] = "number";
            foreach (string key in new[] { "mute", "solo", "a1", "a2", "a3", "a4", "a5", "b1", "b2", "b3" })
                expected[$"strip_{i}_{key}"] = "switch";
            expected[$"bus_{i}_gain"] = "number";
            foreach (string key in new[] { "mute", "mono", "eq_on" }) expected[$"bus_{i}_{key}"] = "switch";
            expected[$"meter_in_{i}"] = "sensor";
            expected[$"meter_out_{i}"] = "sensor";
        }
        foreach (string key in new[] { "record", "play", "stop" }) expected[$"recorder_{key}"] = "switch";
        Assert.Equal(155, mqtt.Messages.Count);
        foreach (var message in mqtt.Messages)
        {
            using var json = JsonDocument.Parse(message.Payload);
            var value = json.RootElement;
            string uid = value.GetProperty("unique_id").GetString()!;
            string prefix = $"voicemeeter_{settings.ComputerName}_";
            Assert.StartsWith(prefix, uid);
            string id = uid[prefix.Length..];
            Assert.True(expected.Remove(id, out string? component), $"Duplicate/unexpected ID: {id}");
            Assert.Equal($"homeassistant/{component}/{uid}/config", message.Topic);
            Assert.True(message.Retain);
            Assert.Equal("test/bridge/availability", value.GetProperty("availability_topic").GetString());
            Assert.Equal("voicemeeter_mqtt_bridge_" + settings.ComputerName,
                value.GetProperty("device").GetProperty("identifiers")[0].GetString());
            if (component == "sensor")
            {
                Assert.Equal("test/bridge/meters", value.GetProperty("state_topic").GetString());
                Assert.Equal("{{ value_json." + id[6..] + " | default(0) }}", value.GetProperty("value_template").GetString());
            }
            else
            {
                Assert.Equal($"test/bridge/parameter/{id}/set", value.GetProperty("command_topic").GetString());
                Assert.Equal($"test/bridge/parameter/{id}/state", value.GetProperty("state_topic").GetString());
            }
        }
        Assert.Empty(expected);
    }

    [Theory]
    [InlineData("parameter/strip_5_gain/set", "-6.25", "Strip[5].Gain", -6.25f, "strip_5_gain", "-6.25")]
    [InlineData("parameter/strip_7_mute/set", "ON", "Strip[7].Mute", 1f, "strip_7_mute", "ON")]
    [InlineData("parameter/bus_7_mute/set", "false", "Bus[7].Mute", 0f, "bus_7_mute", "OFF")]
    [InlineData("set", "{\"parameter\":\"Strip[0].Gain\",\"value\":-12}", "Strip[0].Gain", -12f, "strip_0_gain", "-12")]
    public async Task Legacy_commands_write_the_mapped_parameter_and_publish_readback(
        string topic, string payload, string parameter, float value, string id, string state)
    {
        var remote = new FakeRemote();
        var mqtt = new MqttRecorder();
        await Bridge(Settings(), remote, mqtt).HandleMqttCommandAsync("test/bridge/" + topic, payload);
        Assert.Equal((parameter, value), Assert.Single(remote.Writes));
        var message = Assert.Single(mqtt.Messages);
        Assert.Equal($"test/bridge/parameter/{id}/state", message.Topic);
        Assert.Equal(state, message.Payload);
        Assert.True(message.Retain);
    }

    [Fact]
    public async Task Legacy_availability_remains_retained()
    {
        var settings = Settings();
        var recorder = new MqttRecorder();
        var bridge = Bridge(settings, new FakeRemote(), recorder);
        var mqtt = new MqttBridge(settings, bridge, recorder.Client, _ => { });
        await mqtt.PublishAvailabilityAsync(true);
        await mqtt.PublishAvailabilityAsync(false);
        Assert.Equal(new[] { "online", "offline" }, recorder.Messages.Select(m => m.Payload));
        Assert.All(recorder.Messages, m => { Assert.Equal("test/bridge/availability", m.Topic); Assert.True(m.Retain); });
    }

    [Fact]
    public void Existing_settings_round_trip_without_touching_AppData()
    {
        const string fixture = """
        {"mqttHost":"broker.example.invalid","mqttPort":2883,"mqttUsername":"synthetic-user",
        "mqttPassword":"synthetic-test-only","clientId":"custom-{computer}","baseTopic":" /custom/{computer}/ ",
        "homeAssistantDiscovery":false,"homeAssistantDiscoveryPrefix":"ha-test","publishDiscoveryOnConnect":false,
        "startPotatoWithApp":false,"pollIntervalMs":750,"publishMeters":false,"publishMetersEveryMs":2000,
        "publishAllMappedControls":false,"enableStripRoutingDiscovery":false,"enableStripGainDiscovery":false,
        "enableBusDiscovery":false,"enableRecorderDiscovery":false}
        """;
        var settings = JsonSerializer.Deserialize<AppSettings>(fixture, AppSettings.JsonOptions())!;
        var actual = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(settings))!;
        var expected = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(fixture)!;
        Assert.Equal(expected.Count + 2, actual.Count); // v2 and optional control reconciliation.
        Assert.True(actual.ContainsKey("meteringV2"));
        Assert.Equal(30000, actual["controlReconcileIntervalMs"].GetInt32());
        foreach (var (key, value) in expected) Assert.Equal(value.ToString(), actual[key].ToString());
        Assert.Equal("custom/" + settings.ComputerName, settings.EffectiveBaseTopic);
        Assert.Equal("custom-" + settings.ComputerName, settings.EffectiveClientId);
    }

    [Fact]
    public void Existing_defaults_and_discovery_switches_are_characterized()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.Equal(250, settings.PollIntervalMs);
        Assert.Equal(1000, settings.PublishMetersEveryMs);
        Assert.True(settings.PublishMeters);
        Assert.Equal(139, VmControl.BuildPotatoControls(settings).Count);
        settings.EnableStripGainDiscovery = settings.EnableStripRoutingDiscovery = false;
        settings.EnableBusDiscovery = settings.EnableRecorderDiscovery = false;
        Assert.Empty(VmControl.BuildPotatoControls(settings));
    }
}
