using System.Text.Json;

namespace VoicemeeterMqttBridge.Tests;

public sealed class SettingsMigrationTests
{
    [Fact]
    [Trait("Requirement", "CFG-01")]
    public void V1_settings_gain_disabled_v2_defaults_without_changing_existing_values()
    {
        const string fixture = """
        {"mqttHost":"broker.invalid","mqttPort":2883,"mqttUsername":"test-user",
         "mqttPassword":"synthetic-only","clientId":"owner-{computer}","baseTopic":"custom/{computer}",
         "publishMeters":false,"publishMetersEveryMs":2500,"publishAllMappedControls":false,
         "publishDiscoveryOnConnect":false,"startPotatoWithApp":false}
        """;
        var settings = JsonSerializer.Deserialize<AppSettings>(fixture, AppSettings.JsonOptions())!;
        using var original = JsonDocument.Parse(fixture);
        using var saved = JsonDocument.Parse(JsonSerializer.Serialize(settings, AppSettings.JsonOptions()));
        foreach (var property in original.RootElement.EnumerateObject())
            Assert.Equal(property.Value.ToString(), saved.RootElement.GetProperty(property.Name).ToString());
        var v2 = saved.RootElement.GetProperty("meteringV2");
        Assert.False(v2.GetProperty("enabled").GetBoolean());
        Assert.False(v2.GetProperty("fastEnabled").GetBoolean());
        Assert.True(v2.GetProperty("slowEnabled").GetBoolean());
        Assert.False(v2.GetProperty("legacyMetersEnabled").GetBoolean());
        Assert.Equal(2500, v2.GetProperty("legacyMetersIntervalMs").GetInt32());
        Assert.Equal(50, v2.GetProperty("sampleIntervalMs").GetInt32());
        Assert.Equal(50, v2.GetProperty("fastPublishIntervalMs").GetInt32());
        Assert.Equal(1000, v2.GetProperty("slowPublishIntervalMs").GetInt32());
        Assert.Equal(-90, v2.GetProperty("displayFloorDbfs").GetDouble());
        Assert.Empty(v2.GetProperty("sources").EnumerateArray());
    }

    [Fact]
    public void V2_and_unknown_fields_survive_round_trip_with_unicode_labels()
    {
        const string fixture = """
        {"futureRoot":{"preserve":true},"meteringV2":{"enabled":false,"fastEnabled":true,
         "futureMeter":42,"sources":[{"id":"strip:7","enabled":true,"alias":"music",
         "displayLabel":"音楽 🎵","meterTaps":["pre","post_mute"],"futureSource":"keep"}]}}
        """;
        var settings = JsonSerializer.Deserialize<AppSettings>(fixture, AppSettings.JsonOptions())!;
        using var saved = JsonDocument.Parse(JsonSerializer.Serialize(settings, AppSettings.JsonOptions()));
        Assert.True(saved.RootElement.GetProperty("futureRoot").GetProperty("preserve").GetBoolean());
        var v2 = saved.RootElement.GetProperty("meteringV2");
        Assert.True(v2.GetProperty("fastEnabled").GetBoolean());
        Assert.Equal(42, v2.GetProperty("futureMeter").GetInt32());
        var source = Assert.Single(v2.GetProperty("sources").EnumerateArray());
        Assert.Equal("音楽 🎵", source.GetProperty("displayLabel").GetString());
        Assert.Equal("keep", source.GetProperty("futureSource").GetString());
    }

    [Theory]
    [InlineData("{\"meteringV2\":null,\"publishMeters\":false,\"publishMetersEveryMs\":1500}")]
    [InlineData("{\"publishMeters\":false,\"publishMetersEveryMs\":1500,\"meteringV2\":null}")]
    public void Null_v2_migration_is_order_independent_and_idempotent(string fixture)
    {
        var first = JsonSerializer.Deserialize<AppSettings>(fixture, AppSettings.JsonOptions())!;
        Assert.False(first.MeteringV2.LegacyMetersEnabled);
        Assert.Equal(1500, first.MeteringV2.LegacyMetersIntervalMs);
        string saved = JsonSerializer.Serialize(first, AppSettings.JsonOptions());
        var second = JsonSerializer.Deserialize<AppSettings>(saved, AppSettings.JsonOptions())!;
        Assert.Equal(saved, JsonSerializer.Serialize(second, AppSettings.JsonOptions()));
    }

    [Theory]
    [InlineData("{\"sampleIntervalMs\":0}")]
    [InlineData("{\"fastPublishIntervalMs\":1}")]
    [InlineData("{\"slowPublishIntervalMs\":0}")]
    [InlineData("{\"displayFloorDbfs\":0}")]
    [InlineData("{\"clipThresholdDbfs\":-80}")]
    [InlineData("{\"activityThresholdDbfs\":-95}")]
    [InlineData("{\"sources\":null}")]
    [InlineData("{\"sources\":[null]}")]
    [InlineData("{\"sources\":[{\"id\":\"strip:0\",\"alias\":\"bad/alias\"}]}")]
    [InlineData("{\"sources\":[{\"id\":\"strip:0\",\"meterTaps\":[\"pre\",\"pre\"]}]}")]
    public void Invalid_telemetry_configuration_is_rejected_before_use(string json)
    {
        var settings = JsonSerializer.Deserialize<MeteringV2Settings>(json, AppSettings.JsonOptions())!;
        Assert.Throws<ArgumentException>(settings.Validate);
    }
}
