using Moq;
using System.Text.Json;
namespace VoicemeeterMqttBridge.Tests;

public sealed class AdvancedCommandTests
{
    private static (AdvancedControlService Service, FakeRemote Remote, MqttRecorder Mqtt) Create()
    {
        var settings = new AppSettings { BaseTopic = "test/bridge", HomeAssistantDiscovery = true,
            MeteringV2 = new() { Enabled = true, AdvancedDiscoveryGroups = ["mono", "compressor"],
                Sources = [new() { Id = "strip:0", Enabled = true }] } };
        var remote = new FakeRemote();
        remote.Parameters["Strip[0].Comp.Threshold"] = -20;
        remote.Parameters["Strip[0].Comp.Ratio"] = 2;
        var mock = new Mock<IVoicemeeterRemote>();
        mock.As<IVoicemeeterMetadata>().Setup(r => r.GetEngineIdentity()).Returns(new EngineIdentity(3, new Version(3,1,3,0)));
        mock.Setup(r => r.GetParameterFloat(It.IsAny<string>())).Returns((string parameter) => remote.GetParameterFloat(parameter));
        mock.Setup(r => r.SetParameterFloat(It.IsAny<string>(), It.IsAny<float>())).Returns((string parameter, float value) => remote.SetParameterFloat(parameter, value));
        var mqtt = new MqttRecorder();
        return (new(settings, mock.Object, new MqttBridge(settings, null!, mqtt.Client, _ => { })), remote, mqtt);
    }

    [Theory]
    [InlineData("-10", true, "retained_command_rejected")]
    [InlineData("NaN", false, "out_of_range")]
    [InlineData("-41", false, "out_of_range")]
    [InlineData("bad", false, "invalid_value")]
    public async Task Rejected_commands_never_write_native_parameters(string payload, bool retain, string expected)
    {
        var (service, remote, mqtt) = Create(); await service.InitializeAsync(); mqtt.Messages.Clear();
        await service.CommandAsync("strip_0_comp_threshold", payload, retain);
        Assert.Empty(remote.Writes);
        var message = Assert.Single(mqtt.Messages);
        Assert.False(message.Retain);
        using var result = JsonDocument.Parse(message.Payload);
        Assert.Equal(expected, result.RootElement.GetProperty("error").GetString());
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Readback_is_native_and_unsupported_ids_cannot_cross_the_allowlist()
    {
        var (service, remote, mqtt) = Create(); await service.InitializeAsync(); mqtt.Messages.Clear();
        remote.ReadbackAfterSet = _ => -12;
        await service.CommandAsync("strip_0_comp_threshold", "-10", false);
        Assert.Equal(("Strip[0].Comp.Threshold", -10f), Assert.Single(remote.Writes));
        Assert.Contains(mqtt.Messages, m => m.Topic.EndsWith("/state") && m.Payload == "-12" && m.Retain);
        using var result = JsonDocument.Parse(mqtt.Messages.Last().Payload);
        Assert.Equal(-12, result.RootElement.GetProperty("readback").GetSingle());
        remote.Writes.Clear(); mqtt.Messages.Clear();
        await service.CommandAsync("strip_1_comp_threshold", "-10", false);
        await service.CommandAsync("Strip[0].Gain", "-10", false);
        Assert.Empty(remote.Writes); Assert.Empty(mqtt.Messages);
    }

    [Fact]
    public async Task Failed_set_and_invalid_readback_never_report_success()
    {
        var (service, remote, mqtt) = Create(); await service.InitializeAsync(); mqtt.Messages.Clear();
        remote.SetResult = -1;
        await service.CommandAsync("strip_0_comp_threshold", "-10", false);
        Assert.Contains(mqtt.Messages, m => m.Payload.Contains("set_failed"));
        remote.SetResult = 0; remote.ReadbackAfterSet = _ => float.NaN; mqtt.Messages.Clear();
        await service.CommandAsync("strip_0_comp_threshold", "-10", false);
        Assert.Contains(mqtt.Messages, m => m.Topic.EndsWith("/availability") && m.Payload == "offline");
        Assert.Contains(mqtt.Messages, m => m.Payload.Contains("readback_failed"));
        Assert.DoesNotContain(mqtt.Messages, m => m.Topic.EndsWith("/state"));
    }
}
