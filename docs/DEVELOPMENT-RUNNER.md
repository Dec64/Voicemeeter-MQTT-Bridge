# Development bridge runner

Run the real native/MQTT pipeline from an explicit private configuration without
installing the tray application. Keep credentials outside Git. The runner never
loads or saves the installed AppData configuration. A missing or invalid file fails.

Build and validate from the repository root:

```powershell
& '../.local-phase0/dotnet/dotnet.exe' build tools/DevelopmentBridge/DevelopmentBridge.csproj --verbosity quiet
& './tools/DevelopmentBridge/bin/Debug/net8.0-windows/win-x64/DevelopmentBridge.exe' --config 'C:\private\development-settings.json' --dry-run
& './tools/DevelopmentBridge/bin/Debug/net8.0-windows/win-x64/DevelopmentBridge.exe' --config 'C:\private\development-settings.json' --run 10
```

Dry run validates settings and prints identities, stream settings and enabled source
IDs without initializing native code or MQTT. It omits broker credentials. Run accepts
1–1800 seconds including bridge startup, followed by orderly shutdown; draining an
uncooperative native call or send can exceed that duration. Ctrl+C requests shutdown.

Use a distinct clientId and baseTopic while the installed bridge is running. Set
startPotatoWithApp=false and use an already running supported Potato engine. Enable
meteringV2, at least one stream and at least one canonical source. For the initial
smoke test, homeAssistantDiscovery=false and publishMeters=false avoid discovery and
legacy meter publication. Existing legacy control-state publication and subscriptions
still run under the selected development topic. No control commands are issued by
the runner itself. Do not expose that topic to untrusted command publishers.

An independent MQTT client subscribes before bridge startup. It counts matching
schema/session frames with increasing per-stream sequences, rejecting retained fast
frames. Exit 0 requires metadata, each enabled stream, available source readings and
zero rejected messages. Exit 2 indicates invalid setup, failure or missing evidence.
This counter is a smoke check, not the full frontend validator or a rate/latency test.

Retained metadata, status and legacy state may remain under the development topic
after exit. Normal shutdown marks status/availability stopped/offline; the runner
does not delete retained topics. Discovery, if explicitly enabled, can create HA
entities and must be included in migration/rollback planning. A separate base topic
does not isolate discovery identities: unique IDs and device IDs include the same
computer identity as the installed bridge. Keep discovery disabled while the installed
bridge runs. Enabling it can replace retained discovery configs used by existing
dashboards, even when the development topic is different. Test actual discovery only
in an approved migration window or a separate HA/broker environment.

Before the first live run, private backups of installed settings/executable, an
installer and the HA dashboard/resources were stored outside Git with an owner-only
ACL. The run did not replace those files. For this separate-topic test, rollback is
simply stopping the runner; installed bridge operation continues. Any later live HA
replacement should record exactly which discovery topics/files were changed and
restore only those items from the backup if needed.

On 2026-10-02 a requested 10-second run received 1 metadata message, 121 fast frames,
6 slow frames and 2032 available source readings, with zero rejected frames or
unavailable readings. Startup is included, so these counts do not establish a
sustained 20 Hz rate. HA WebSocket delivery, live card rendering, physical source
identity and long-running recovery remain unverified. The browser preview is mock.
