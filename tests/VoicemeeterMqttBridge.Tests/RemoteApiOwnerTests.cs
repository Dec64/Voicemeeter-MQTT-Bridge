using System.Collections.Concurrent;

namespace VoicemeeterMqttBridge.Tests;

public sealed class RemoteApiOwnerTests
{
    [Fact]
    public async Task Metadata_is_registered_and_runs_on_the_same_owner_thread()
    {
        var remote = new RecordingRemote();
        using var owner = new RemoteApiOwner(remote, _ => { });
        Assert.Throws<InvalidOperationException>(() => owner.GetEngineIdentity());
        owner.Load(); owner.Login();
        await Task.Run(() => Assert.Equal(remote.Identity, owner.GetEngineIdentity()));
        await Task.Run(() => Assert.Null(owner.GetLabel(SourceKind.Strip, 0)));
        owner.GetLevel(0, 0);
        Assert.Single(remote.Calls.Select(c => c.Thread).Distinct());
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Successful_registration_is_reused_and_logged_out_once(int result)
    {
        var remote = new RecordingRemote { LoginResult = result };
        using (var owner = new RemoteApiOwner(remote, _ => { }))
        {
            owner.Load();
            Assert.Equal(result, owner.Login());
            Assert.Equal(result, owner.Login());
            Assert.Equal(1, remote.Count("Login"));
            owner.Logout();
            owner.Logout();
        }
        Assert.Equal(1, remote.Count("Logout"));
    }

    [Fact]
    public async Task All_calls_from_different_callers_use_one_native_thread()
    {
        var remote = new RecordingRemote();
        using (var owner = new RemoteApiOwner(remote, _ => { }))
        {
            owner.Load();
            owner.Login();
            await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() =>
            {
                Assert.True(owner.IsLoaded);
                owner.IsParametersDirty();
                owner.SetParameterFloat($"Strip[{i}].Gain", i);
                Assert.Equal(i, owner.GetParameterFloat($"Strip[{i}].Gain"));
                Assert.Equal(0.5f, owner.GetLevel(0, i));
            })));
        }
        Assert.Single(remote.Calls.Select(c => c.Thread).Distinct());
        Assert.Equal("Logout", remote.Calls.Last().Name);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-2)]
    public void Failed_login_does_not_grant_reads_or_require_logout(int result)
    {
        var remote = new RecordingRemote { LoginResult = result };
        using (var owner = new RemoteApiOwner(remote, _ => { }))
        {
            owner.Load();
            Assert.Equal(result, owner.Login());
            Assert.Throws<InvalidOperationException>(() => owner.GetLevel(0, 0));
        }
        Assert.Equal(0, remote.Count("GetLevel"));
        Assert.Equal(0, remote.Count("Logout"));
    }

    [Fact]
    public void Call_exception_does_not_kill_owner_and_failed_login_can_be_retried()
    {
        var remote = new RecordingRemote { LoginResult = -1 };
        using var owner = new RemoteApiOwner(remote, _ => { });
        owner.Load();
        Assert.Equal(-1, owner.Login());
        remote.LoginResult = 0;
        Assert.Equal(0, owner.Login());
        remote.BeforeCall = name => { if (name == "GetLevel") throw new InvalidOperationException("Synthetic failure"); };
        Assert.Throws<InvalidOperationException>(() => owner.GetLevel(0, 0));
        remote.BeforeCall = null;
        Assert.Equal(0.5f, owner.GetLevel(0, 0));
    }

    [Fact]
    public void Throwing_logout_and_observer_do_not_kill_worker_or_repeat_logout()
    {
        var remote = new RecordingRemote();
        var owner = new RemoteApiOwner(remote, _ => throw new IOException("Observer failed"));
        owner.Load();
        owner.Login();
        remote.BeforeCall = name => { if (name == "Logout") throw new IOException("Logout failed"); };
        owner.Dispose();
        owner.Dispose();
        Assert.Equal(1, remote.Count("Logout"));
    }

    [Fact]
    public async Task Shutdown_waits_for_active_call_then_logs_out_and_rejects_later_calls()
    {
        var remote = new RecordingRemote();
        var owner = new RemoteApiOwner(remote, _ => { });
        owner.Load();
        owner.Login();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        remote.BeforeCall = name =>
        {
            if (name == "GetLevel") { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); }
        };
        Task read = Task.Run(() => owner.GetLevel(0, 0));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Task stop = Task.Run(owner.Dispose);
        Assert.Equal(0, remote.Count("Logout"));
        release.Set();
        await Task.WhenAll(read, stop).WaitAsync(TimeSpan.FromSeconds(5));
        owner.Dispose();
        Assert.Throws<ObjectDisposedException>(() => owner.IsParametersDirty());
        Assert.Equal(1, remote.Count("Logout"));
        Assert.Equal("Logout", remote.Calls.Last().Name);
    }
}

internal sealed class RecordingRemote : IVoicemeeterRemote, IVoicemeeterMetadata
{
    public ConcurrentQueue<(string Name, int Thread)> Calls { get; } = new();
    private readonly ConcurrentDictionary<string, float> _values = new();
    private bool _loaded;
    public int LoginResult { get; set; }
    public int DirtyResult { get; set; }
    public int RunResult { get; set; }
    public EngineIdentity Identity { get; set; } = new(3, new Version(3, 1, 3, 0));
    public EngineIdentity GetEngineIdentity() { Record("Identity"); return Identity; }
    public string? GetLabel(SourceKind kind, int index) { Record("Label"); return null; }
    public Action<string>? BeforeCall { get; set; }
    public int Count(string name) => Calls.Count(c => c.Name == name);
    private void Record(string name) { Calls.Enqueue((name, Environment.CurrentManagedThreadId)); BeforeCall?.Invoke(name); }
    public bool IsLoaded { get { Record("IsLoaded"); return _loaded; } }
    public void Load() { Record("Load"); _loaded = true; }
    public int Login() { Record("Login"); return LoginResult; }
    public int Logout() { Record("Logout"); return 0; }
    public int RunVoicemeeter(int type) { Record("Run"); return RunResult; }
    public int IsParametersDirty() { Record("Dirty"); return DirtyResult; }
    public float GetLevel(int type, int channel) { Record("GetLevel"); return 0.5f; }
    public float GetParameterFloat(string parameter) { Record("Get"); return _values.GetValueOrDefault(parameter); }
    public int SetParameterFloat(string parameter, float value) { Record("Set"); _values[parameter] = value; return 0; }
}
