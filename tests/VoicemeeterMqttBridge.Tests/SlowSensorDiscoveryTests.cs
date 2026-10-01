using System.Text.Json;
using MQTTnet.Protocol;

namespace VoicemeeterMqttBridge.Tests;

public sealed class SlowSensorDiscoveryTests
{
    private static MeteringV2Settings Settings() => new()
    {
        Enabled = true, Sources = new()
        {
            new() { Id = "strip:0", Enabled = true },
            new() { Id = "bus:5", Enabled = true },
            new() { Id = "strip:5", Enabled = false }
        }
    };
    private static IReadOnlyList<MQTTnet.MqttApplicationMessage> Build(MeteringV2Settings settings, string computer = "test_pc")
        => SlowSensorDiscovery.Build(SourceRegistry.Read(new SourceRegistryTests.Metadata(), settings), settings,
            computer, "test/bridge", "homeassistant", "2.0.0-dev", 5);

    [Fact]
    public void Enabled_sources_get_three_additive_entities_on_slow_topic()
    {
        var messages = Build(Settings());
        Assert.Equal(6, messages.Count);
        foreach (var message in messages)
        {
            using var json = JsonDocument.Parse(message.PayloadSegment); var root = json.RootElement;
            string id = root.GetProperty("unique_id").GetString()!;
            Assert.StartsWith("voicemeeter_test_pc_v2_", id);
            Assert.Equal($"homeassistant/{(id.EndsWith("_peak") ? "sensor" : "binary_sensor")}/{id}/config", message.Topic);
            Assert.True(message.Retain); Assert.Equal(MqttQualityOfServiceLevel.AtLeastOnce, message.QualityOfServiceLevel);
            Assert.Equal("test/bridge/v2/meters/slow", root.GetProperty("state_topic").GetString());
            Assert.Equal("voicemeeter_mqtt_bridge_test_pc", root.GetProperty("device").GetProperty("identifiers")[0].GetString());
            Assert.Equal("all", root.GetProperty("availability_mode").GetString());
            Assert.Equal("test/bridge/availability", root.GetProperty("availability")[0].GetProperty("topic").GetString());
            Assert.Equal(5, root.GetProperty("expire_after").GetInt32());
            Assert.False(root.TryGetProperty("json_attributes_topic", out _));
            Assert.False(root.TryGetProperty("default_entity_id", out _));
            if (id.EndsWith("_peak")) Assert.Equal("dBFS", root.GetProperty("unit_of_measurement").GetString());
        }
    }

    [Fact]
    public void Labels_and_aliases_never_change_ids_or_templates()
    {
        var settings = Settings(); var before = Build(settings);
        settings.Sources[0].DisplayLabel = "日本語 {{ bad() }}"; settings.Sources[0].Alias = "guest";
        var after = Build(settings);
        for (int i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].Topic, after[i].Topic);
            using var a = JsonDocument.Parse(before[i].PayloadSegment);
            using var b = JsonDocument.Parse(after[i].PayloadSegment);
            Assert.Equal(a.RootElement.GetProperty("value_template").GetString(), b.RootElement.GetProperty("value_template").GetString());
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Disabled_v2_or_slow_stream_emits_no_discovery(bool enabled, bool slow)
    {
        var settings = Settings(); settings.Enabled = enabled; settings.SlowEnabled = slow;
        Assert.Empty(Build(settings));
    }

    [Fact]
    public void Empty_profile_emits_no_discovery()
    {
        var settings = Settings(); settings.Sources.Clear(); Assert.Empty(Build(settings));
    }

    [Theory]
    [InlineData("bad/name")]
    [InlineData("bad+name")]
    [InlineData("")]
    public void Unsafe_computer_identity_is_rejected_without_lossy_sanitizing(string computer)
        => Assert.ThrowsAny<ArgumentException>(() => Build(Settings(), computer));

    [Fact]
    public void Expiry_must_exceed_the_slow_window()
    {
        var settings = Settings();
        Assert.Throws<ArgumentOutOfRangeException>(() => SlowSensorDiscovery.Build(
            SourceRegistry.Read(new SourceRegistryTests.Metadata(), settings), settings,
            "pc", "base", "homeassistant", "2", 1));
    }

    [Theory]
    [InlineData("post_mute")]
    [InlineData("post_fader")]
    public void Explicit_non_pre_tap_is_used_by_conventional_peak_sensor(string tap)
    {
        var settings = Settings(); settings.Sources[0].MeterTaps = new() { tap };
        using var payload = JsonDocument.Parse(Build(settings)[0].PayloadSegment);
        string template = payload.RootElement.GetProperty("value_template").GetString()!;
        Assert.Contains("s.get('" + tap + "_dbfs')", template);
        Assert.DoesNotContain("pre_dbfs", template);
    }

    [Fact]
    public void All_sources_have_distinct_ids()
    {
        var settings = Settings();
        settings.Sources = PotatoChannelMap.All.Select(s => new SourceProfile { Id = s.Id, Enabled = true }).ToList();
        var messages = Build(settings);
        Assert.Equal(48, messages.Count); Assert.Equal(48, messages.Select(m => m.Topic).Distinct().Count());
    }
}
