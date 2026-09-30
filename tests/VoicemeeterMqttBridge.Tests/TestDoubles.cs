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
    public List<string> ParameterReads { get; } = new();
    public HashSet<string> FailedReads { get; } = new();
    public int DirtyResult { get; set; }
    public Exception? DirtyException { get; set; }
    public int SetResult { get; set; }
    public Func<float, float>? ReadbackAfterSet { get; set; }
    public bool IsLoaded => true;
    public float GetLevel(int type, int channel)
    {
        Reads.Add((type, channel));
        return Levels.TryGetValue((type, channel), out float value)
            ? value : throw new InvalidOperationException("Synthetic unavailable channel");
    }
    public float GetParameterFloat(string parameter)
    {
        ParameterReads.Add(parameter);
        if (FailedReads.Contains(parameter)) throw new InvalidOperationException("Synthetic read failure");
        return Parameters.GetValueOrDefault(parameter);
    }
    public int SetParameterFloat(string parameter, float value)
    {
        Writes.Add((parameter, value));
        if (SetResult == 0) Parameters[parameter] = ReadbackAfterSet?.Invoke(value) ?? value;
        return SetResult;
    }
    // These tests never start the bridge, engine or background polling.
    public void Load() => throw new NotSupportedException();
    public int Login() => throw new NotSupportedException();
    public int Logout() => throw new NotSupportedException();
    public int RunVoicemeeter(int type) => throw new NotSupportedException();
    public int IsParametersDirty() => DirtyException is null ? DirtyResult : throw DirtyException;
}

internal sealed record PublishedMessage(string Topic, string Payload, bool Retain,
    MqttQualityOfServiceLevel Qos);

internal sealed class MqttRecorder
{
    public List<PublishedMessage> Messages { get; } = new();
    public IMqttClient Client { get; }
    public bool IsConnected { get; set; } = true;
    public string? FailTopic { get; set; }
    public MqttClientPublishReasonCode PublishReason { get; set; } = MqttClientPublishReasonCode.Success;
    public Func<MqttApplicationMessage, Task>? BeforePublish { get; set; }
    private Func<MqttClientConnectedEventArgs, Task>? _connected;
    public Task RaiseConnectedAsync() => _connected!(new MqttClientConnectedEventArgs(new MqttClientConnectResult()));

    public MqttRecorder()
    {
        var mock = new Mock<IMqttClient>(MockBehavior.Strict);
        mock.SetupAdd(c => c.ConnectedAsync += It.IsAny<Func<MqttClientConnectedEventArgs, Task>>())
            .Callback<Func<MqttClientConnectedEventArgs, Task>>(handler => _connected += handler);
        mock.Setup(c => c.SubscribeAsync(It.IsAny<MqttClientSubscribeOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MqttClientSubscribeResult)null!); // The legacy setup does not inspect subscription results.
        mock.SetupGet(c => c.IsConnected).Returns(() => IsConnected);
        mock.Setup(c => c.PublishAsync(It.IsAny<MqttApplicationMessage>(), It.IsAny<CancellationToken>()))
            .Returns<MqttApplicationMessage, CancellationToken>(async (message, _) =>
            {
                if (BeforePublish != null) await BeforePublish(message);
                if (message.Topic == FailTopic) throw new IOException("Synthetic send failure");
                if (PublishReason == MqttClientPublishReasonCode.Success)
                    Messages.Add(new(message.Topic, Encoding.UTF8.GetString(message.PayloadSegment),
                        message.Retain, message.QualityOfServiceLevel));
                return new MqttClientPublishResult(null, PublishReason, null, null);
            });
        Client = mock.Object;
    }
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private long _milliseconds;
    public override long TimestampFrequency => 1000;
    public override long GetTimestamp() => _milliseconds;
    public void Advance(TimeSpan elapsed) => _milliseconds += (long)elapsed.TotalMilliseconds;
}
