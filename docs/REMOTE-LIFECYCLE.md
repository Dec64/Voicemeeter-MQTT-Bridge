# Remote API ownership and registration

The production `BridgeService` constructs a `RemoteApiOwner` around the existing native adapter. Load, login, launch, dirty polling, float reads/writes, levels and logout execute on one dedicated physical thread. MQTT awaits remain on the calling tasks. This implements the pinned SDK's one-thread requirement for dirty polling and levels without adding a DLL binding.

The owner queues at most 64 pending calls. It rejects further calls with an explicit error rather than growing the native-call queue. This is a synchronous dispatch boundary, not the future fast telemetry scheduler or its latest-frame queue. Its overhead and command responsiveness under fast sampling have not been measured.

## Registration contract

- Login `0` means registered with the engine present; `1` means registered with the engine absent. Both own a registration and eventually require logout.
- A repeated login request reuses that registration. Engine loss (`IsParametersDirty() == -2`) marks the engine unavailable; a later successful poll recovers state without logging in again.
- Other login results leave the client unregistered. The bridge retries initialization after at least five seconds, measured with monotonic time, when its polling loop is running. As before, polling starts after MQTT startup completes.
- With `StartPotatoWithApp` enabled, login `1` requests `RunVoicemeeter(3)` once. A nonzero launch result invokes the existing EXE fallback. A thrown launch exception is logged. A launch failure does not revoke registration or cause another login.
- The bridge cancels polling, waits for initialization, polling and accepted control/meter operations, then logs out once. Late native work is rejected. Repeated start/stop calls reuse their tasks; restarting a stopped service requires a new instance.
- Owner disposal closes admission, drains accepted native calls and logs out on the owner thread. A failed or throwing logout is logged, not retried automatically. A throwing log observer cannot terminate the owner thread's host process.

An explicitly injected `IVoicemeeterRemote` remains caller-supplied: tests may pass a raw fake or an externally owned `RemoteApiOwner`. The bridge logs out a successfully registered injected client but does not dispose the injected object. Callers supplying a real native adapter must wrap it in an owner. The application uses the default constructor path.

## Evidence and limits

The official pinned header documents login `1` as success with no Voicemeeter engine, login `-2` as an unexpected login, and logout `0` as success. See [SDK reference](SDK-REFERENCE.md) for version, hashes and sources. No matching installed header/manual was located, and the installed native DLL was not loaded or exercised by these tests.

Nineteen fake-adapter cases cover physical thread identity, login `0`/`1`, negative login, retry, launch/fallback, exceptions, engine loss/recovery, repeated start/stop, stopping before startup, shutdown during login/commands, and late-call rejection. Thread identity includes logout. Tests never start Voicemeeter or connect to a broker. The full 495-test suite also retains the legacy protocol and channel-mapping checks.

Remaining verification:

- Installed-engine startup, restart, closure and actual audio/control behavior. A native call that never returns can still hold shutdown; safely aborting an in-process DLL call is not provided.
- Native queue saturation and throughput under load. The bounded owner queue does not bound the existing async MQTT command waiters.
- Existing MQTT transport lifecycle, including in-flight connection attempts and delayed reconnect callbacks during shutdown. This slice prevents late **native** work; it does not claim that all transport tasks have drained.
- Native labels/capabilities, timed v2 telemetry, and fast HA streaming. No 10/20 Hz or p95 publish-to-visible target has been demonstrated.
- The reusable one-strip-or-bus HACS card, visual editor and shared subscription remain required.

No live HA/SMB files, installed bridge or live settings were changed. The local executable was built, not launched or installed.
