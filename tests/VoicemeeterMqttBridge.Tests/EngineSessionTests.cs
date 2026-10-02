using System.Threading.Channels;

namespace VoicemeeterMqttBridge.Tests;

public sealed class EngineSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Engine_change_before_or_during_a_pass_cannot_publish_a_window(bool during)
    {
        var remote = new RecordingRemote();
        using var owner = new RemoteApiOwner(remote, _ => { }); owner.Load(); owner.Login();
        var settings = new MeteringV2Settings { Enabled = true, FastEnabled = true,
            Sources = new() { new() { Id = "strip:0", Enabled = true } } };
        var registry = SourceRegistry.Read(owner, settings);
        var clock = new TelemetryTimerClock();
        var loop = new MeterTelemetryLoop(registry, settings, owner, clock);
        using var stop = new CancellationTokenSource();
        var run = loop.RunAsync(stop.Token);
        if (during) remote.BeforeCall = name => { if (name == "GetLevel") remote.Identity = new(2, new Version(2, 1, 3, 0)); };
        else remote.Identity = new(2, new Version(2, 1, 3, 0));
        clock.Advance(50);
        try { await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(TimeSpan.FromSeconds(2))); }
        finally { stop.Cancel(); await run.ContinueWith(_ => { }); }
        await Assert.ThrowsAsync<ChannelClosedException>(() => loop.Fast.ReadAsync().AsTask());
        Assert.Equal(during ? 4 : 0, remote.Count("GetLevel"));
        Assert.Equal(0, clock.ActiveTimers);
    }
}
