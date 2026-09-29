using System.Text;
using Moq;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace VoicemeeterMqttBridge.Tests;

internal sealed class FakeRemote : IVoicemeeterRemote
{
    public Dictionary<(int Type, int Channel), float> Levels { get; } = new();
    public List<(int Type, int Channel)> Reads { get; } = new();
    public Dictionary<string, float> Parameters { get; } = new();
    public List<(string Parameter, float Value)> Writes { get; } = new();
    public bool IsLoaded => true;
    public float GetLevel(int type, int channel)
    {
        Reads.Add((type, channel));
        return Levels.TryGetValue((type, channel), out float value)
            ? value : throw new InvalidOperationException("Synthetic unavailable channel");
    }
    public float GetParameterFloat(string parameter) => Parameters.GetValueOrDefault(parameter);
    public int SetParameterFloat(string parameter, float value)
    {
        Writes.Add((parameter, value));
        Parameters[parameter] = value;
        return 0;
    }
    // These tests never start the bridge, engine or background polling.
    public void Load() => throw new NotSupportedException();
    public int Login() => throw new NotSupportedException();
    public int Logout() => throw new NotSupportedException();
    public int RunVoicemeeter(int type) => throw new NotSupportedException();
    public int IsParametersDirty() => throw new NotSupportedException();
}

internal sealed record PublishedMessage(string Topic, string Payload, bool Retain,
    MqttQualityOfServiceLevel Qos);

internal sealed class MqttRecorder
{
    public List<PublishedMessage> Messages { get; } = new();
    public IMqttClient Client { get; }

    public MqttRecorder()
    {
        var mock = new Mock<IMqttClient>(MockBehavior.Strict);
        mock.SetupGet(c => c.IsConnected).Returns(true);
        mock.Setup(c => c.PublishAsync(It.IsAny<MqttApplicationMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MqttApplicationMessage, CancellationToken>((message, _) => Messages.Add(new(
                message.Topic, Encoding.UTF8.GetString(message.PayloadSegment),
                message.Retain, message.QualityOfServiceLevel)))
            .ReturnsAsync((MqttClientPublishResult)null!); // Production ignores this result.
        Client = mock.Object;
    }
}
