namespace VoicemeeterMqttBridge.Tests;

public sealed class BridgeLifecycleTests
{
    private static AppSettings Settings(bool launch = false) => new()
    {
        MqttHost = "unused.invalid", BaseTopic = "test/lifecycle", StartPotatoWithApp = launch,
        PublishMeters = false, HomeAssistantDiscovery = false, ControlReconcileIntervalMs = 0
    };

    [Theory]
    [InlineData(0, true, 0)]
    [InlineData(1, true, 1)]
    [InlineData(1, false, 0)]
    [InlineData(-1, true, 0)]
    [InlineData(-2, true, 0)]
    public async Task Start_and_stop_are_idempotent_and_login_one_is_registration(int login, bool launch, int runs)
    {
        var remote = new RecordingRemote { LoginResult = login, DirtyResult = -2 };
        var mqtt = new MqttRecorder();
        int fallbacks = 0;
        var bridge = new BridgeService(Settings(launch), remote, mqtt.Client, _ => { },
            startPotatoFallback: () => fallbacks++);
        Task start = bridge.StartAsync();
        Assert.Same(start, bridge.StartAsync());
        await start.WaitAsync(TimeSpan.FromSeconds(5));
        Task stop = bridge.StopAsync();
        Assert.Same(stop, bridge.StopAsync());
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, remote.Count("Login"));
        Assert.Equal(runs, remote.Count("Run"));
        Assert.Equal(0, fallbacks);
        Assert.Equal(login >= 0 ? 1 : 0, remote.Count("Logout"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.StartAsync());
    }

    [Fact]
    public async Task Failed_registration_is_retried_after_monotonic_backoff()
    {
        var remote = new RecordingRemote { LoginResult = -1 };
        var mqtt = new MqttRecorder();
        var time = new ManualTimeProvider();
        var retried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        remote.BeforeCall = name =>
        {
            if (name == "Login" && remote.Count("Login") == 2) retried.TrySetResult();
        };
        var bridge = new BridgeService(Settings(), remote, mqtt.Client, _ => { }, time);
        await bridge.StartAsync();
        Assert.Equal("Login failed: -1", bridge.VoicemeeterStatus);
        Assert.Equal(0, remote.Count("Dirty"));
        remote.LoginResult = 0;
        time.Advance(TimeSpan.FromSeconds(5));
        try { await retried.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { await bridge.StopAsync(); }
        Assert.Equal(2, remote.Count("Login"));
        Assert.Equal(1, remote.Count("Logout"));
        Assert.Equal("Logout", remote.Calls.Last().Name);
    }

    [Fact]
    public async Task Stop_before_start_never_initializes_remote()
    {
        var remote = new RecordingRemote();
        var mqtt = new MqttRecorder();
        var bridge = new BridgeService(Settings(), remote, mqtt.Client, _ => { });
        await bridge.StopAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.StartAsync());
        Assert.Empty(remote.Calls);
    }

    [Fact]
    public async Task Engine_loss_and_return_keep_the_same_registration()
    {
        var remote = new RecordingRemote { LoginResult = 1, DirtyResult = -2 };
        using var owner = new RemoteApiOwner(remote, _ => { });
        var mqtt = new MqttRecorder();
        var bridge = new BridgeService(Settings(), owner, mqtt.Client, _ => { });
        await bridge.StartAsync();
        await bridge.PollControlStateAsync();
        Assert.Equal("Engine unavailable", bridge.VoicemeeterStatus);
        remote.DirtyResult = 0;
        await bridge.PollControlStateAsync();
        Assert.Equal("Connected", bridge.VoicemeeterStatus);
        await bridge.SetParameterAsync("Strip[0].Gain", -3, publish: false);
        await bridge.StopAsync();
        Assert.Equal(1, remote.Count("Login"));
        Assert.Equal(1, remote.Count("Logout"));
        Assert.Single(remote.Calls.Select(c => c.Thread).Distinct());
        Assert.Equal("Logout", remote.Calls.Last().Name);
    }

    [Fact]
    public async Task Failed_engine_launch_uses_fallback_without_logging_in_again()
    {
        var remote = new RecordingRemote { LoginResult = 1, RunResult = -1, DirtyResult = -2 };
        var mqtt = new MqttRecorder();
        int fallbacks = 0;
        var bridge = new BridgeService(Settings(true), remote, mqtt.Client, _ => { },
            startPotatoFallback: () => fallbacks++);
        await bridge.StartAsync();
        await bridge.StopAsync();
        Assert.Equal(1, fallbacks);
        Assert.Equal(1, remote.Count("Login"));
        Assert.Equal(1, remote.Count("Logout"));
    }

    [Fact]
    public async Task Shutdown_during_login_waits_for_registration_then_logs_out()
    {
        var remote = new RecordingRemote { LoginResult = 1 };
        var mqtt = new MqttRecorder();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        remote.BeforeCall = name =>
        {
            if (name == "Login") { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); }
        };
        var bridge = new BridgeService(Settings(), remote, mqtt.Client, _ => { });
        Task start = bridge.StartAsync();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Task stop = bridge.StopAsync();
        try { Assert.False(stop.IsCompleted); }
        finally { release.Set(); }
        await Task.WhenAll(start, stop).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, remote.Count("Logout"));
        Assert.Equal("Logout", remote.Calls.Last().Name);
        Assert.Equal(0, remote.Count("Dirty"));
    }

    [Fact]
    public async Task Stop_drains_command_and_rejects_all_late_native_work()
    {
        var remote = new RecordingRemote();
        var mqtt = new MqttRecorder();
        var bridge = new BridgeService(Settings(), remote, mqtt.Client, _ => { });
        await bridge.StartAsync();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        remote.BeforeCall = name =>
        {
            if (name == "Set") { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); }
        };
        Task command = Task.Run(() => bridge.SetParameterAsync("Strip[0].Gain", -3, publish: false));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Task stop = bridge.StopAsync();
        try { Assert.Equal(0, remote.Count("Logout")); }
        finally { release.Set(); }
        await Task.WhenAll(command, stop).WaitAsync(TimeSpan.FromSeconds(5));
        int count = remote.Calls.Count;
        await bridge.SetParameterAsync("Strip[0].Gain", -6, publish: true);
        await bridge.PollControlStateAsync();
        await bridge.PublishAllStateAsync();
        await bridge.PublishParameterStateAsync("Strip[0].Gain");
        await bridge.PublishMetersAsync();
        await bridge.ReconnectAsync();
        Assert.Equal(count, remote.Calls.Count);
        Assert.Equal("Logout", remote.Calls.Last().Name);
        Assert.Equal(1, remote.Count("Logout"));
    }
}
