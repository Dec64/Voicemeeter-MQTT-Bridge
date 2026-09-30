// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
using System.Collections.Concurrent;

namespace VoicemeeterMqttBridge;

/// <summary>Serializes synchronous DLL calls on one physical thread for the client lifetime.</summary>
public sealed class RemoteApiOwner : IVoicemeeterRemote, IDisposable
{
    private readonly IVoicemeeterRemote _remote;
    private readonly Action<string> _log;
    private readonly BlockingCollection<Action> _work = new(64);
    private readonly object _gate = new();
    private readonly Thread _thread;
    private bool _closed;
    private bool _disposed;
    private bool _registered;
    private int _loginResult;

    public RemoteApiOwner(IVoicemeeterRemote remote, Action<string> log)
    {
        _remote = remote;
        _log = log;
        _thread = new Thread(Run) { IsBackground = true, Name = "Voicemeeter Remote API" };
        _thread.Start();
    }

    private T Invoke<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (Environment.CurrentManagedThreadId == _thread.ManagedThreadId) return action();
            if (!_work.TryAdd(() =>
            {
                try { completion.SetResult(action()); }
                catch (Exception ex) { completion.SetException(ex); }
            })) throw new InvalidOperationException("Remote API queue is full.");
        }
        return completion.Task.GetAwaiter().GetResult();
    }

    private T Registered<T>(Func<T> action) => Invoke(() =>
        _registered ? action() : throw new InvalidOperationException("Remote API client is not registered."));

    public bool IsLoaded => Invoke(() => _remote.IsLoaded);
    public void Load() => Invoke(() => { _remote.Load(); return 0; });
    public int Login() => Invoke(() =>
    {
        if (_registered) return _loginResult;
        int result = _remote.Login();
        if (result is 0 or 1) { _registered = true; _loginResult = result; }
        return result;
    });
    public int Logout() => Invoke(LogoutCore);
    private int LogoutCore()
    {
        if (!_registered) return 0;
        _registered = false;
        return _remote.Logout();
    }
    public int RunVoicemeeter(int type) => Registered(() => _remote.RunVoicemeeter(type));
    public int IsParametersDirty() => Registered(_remote.IsParametersDirty);
    public float GetParameterFloat(string parameter) => Registered(() => _remote.GetParameterFloat(parameter));
    public int SetParameterFloat(string parameter, float value) => Registered(() => _remote.SetParameterFloat(parameter, value));
    public float GetLevel(int type, int channel) => Registered(() => _remote.GetLevel(type, channel));

    private void Run()
    {
        try { foreach (var action in _work.GetConsumingEnumerable()) action(); }
        finally
        {
            try
            {
                int result = LogoutCore();
                if (result != 0) LogShutdown("Remote API logout returned " + result);
            }
            catch (Exception ex) { LogShutdown("Remote API logout failed: " + ex.Message); }
        }
    }

    private void LogShutdown(string message)
    {
        // An observer must not terminate the process from the background owner thread.
        try { _log(message); } catch { }
    }

    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId == _thread.ManagedThreadId)
            throw new InvalidOperationException("Cannot stop the Remote API owner from its own thread.");
        lock (_gate)
        {
            if (!_closed) { _closed = true; _work.CompleteAdding(); }
        }
        _thread.Join(); // Accepted calls drain before the worker logs out.
        lock (_gate)
        {
            if (!_disposed) { _work.Dispose(); _disposed = true; }
        }
    }
}
