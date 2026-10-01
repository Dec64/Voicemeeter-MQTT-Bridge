using System.Threading.Channels;
using Moq;
using MQTTnet;
using MQTTnet.Client;

namespace VoicemeeterMqttBridge.Tests;

// Drives the real PeriodicTimer through TimeProvider; one callback per Advance
// deliberately coalesces elapsed ticks, matching the timer's no-backlog contract.
internal sealed class TelemetryTimerClock : TimeProvider
{
    private long _now;
    private readonly List<ManualTimer> _timers = new();
    public TaskCompletionSource Disposed { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int ActiveTimers => _timers.Count(t => !t.Disposed);
    public override long TimestampFrequency => 1000;
    public override long GetTimestamp() => Interlocked.Read(ref _now);
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = new ManualTimer(callback, state, _now + (long)dueTime.TotalMilliseconds, (long)period.TotalMilliseconds, Disposed);
        _timers.Add(timer);
        return timer;
    }
    public void Advance(int milliseconds)
    {
        Interlocked.Add(ref _now, milliseconds);
        foreach (var timer in _timers.ToArray()) timer.Fire(_now);
    }
    private sealed class ManualTimer(TimerCallback callback, object? state, long due, long period, TaskCompletionSource disposed) : ITimer
    {
        public bool Disposed { get; private set; }
        public void Fire(long now)
        {
            if (Disposed || now < due) return;
            due = now + period; callback(state);
        }
        public bool Change(TimeSpan dueTime, TimeSpan newPeriod) => throw new NotSupportedException();
        public void Dispose() { Disposed = true; disposed.TrySetResult(); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

internal sealed class TelemetryBroker
{
    public bool Connected = true;
    public int Calls;
    public MqttClientPublishReasonCode Result = MqttClientPublishReasonCode.Success;
    public Func<MqttApplicationMessage, CancellationToken, Task>? BeforeSend;
    public Channel<MqttApplicationMessage> Sent { get; } = Channel.CreateUnbounded<MqttApplicationMessage>();
    public IMqttClient Client { get; }
    public TelemetryBroker()
    {
        var mock = new Mock<IMqttClient>(MockBehavior.Strict);
        mock.SetupGet(m => m.IsConnected).Returns(() => Connected);
        mock.Setup(m => m.PublishAsync(It.IsAny<MqttApplicationMessage>(), It.IsAny<CancellationToken>()))
            .Returns<MqttApplicationMessage, CancellationToken>(async (message, token) =>
            {
                Interlocked.Increment(ref Calls);
                if (BeforeSend is not null) await BeforeSend(message, token);
                if (Result == MqttClientPublishReasonCode.Success) Sent.Writer.TryWrite(message);
                return new MqttClientPublishResult(null, Result, null, null);
            });
        Client = mock.Object;
    }
}
