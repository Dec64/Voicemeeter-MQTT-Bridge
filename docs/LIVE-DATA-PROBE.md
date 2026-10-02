# Read-only live meter probe

This development console tool uses the bridge's existing Remote DLL bindings and
the v2 channel mapper against a running Potato engine. It does not run the tray app,
load/save bridge settings, write the installed bridge log, set audio parameters,
start Voicemeeter, connect to MQTT or alter Home Assistant.

The scope is a bounded capture, not live dashboard streaming. Each of 20 passes reads
pre-fader and post-mute peaks for eight strips plus output peaks for eight buses.
Native calls stay on one synchronous thread. The probe checks engine identity before
each pass, rejects failed dirty/identity reads, and logs out after a successful client
registration, including registration with no running engine. Unavailable channels
remain null rather than becoming silence. The default floor is -90 dBFS.

From the repository root, using the workspace .NET 8 SDK:

```powershell
& '../.local-phase0/dotnet/dotnet.exe' build tools/MeterProbe/MeterProbe.csproj --verbosity quiet
& './tools/MeterProbe/bin/Debug/net8.0-windows/win-x64/MeterProbe.exe' --read-live |
    Set-Content '../.local-phase0/meter-probe-live.private.json' -Encoding utf8
```

Without the exact `--read-live` argument it prints usage and exits before accessing
the DLL. Exit 0 means all captured readings were available; 1 means incorrect usage;
2 means a probe failure or at least one unavailable reading. Keep captures outside Git.
The command uses the existing DLL lookup order, including VOICEMEETER_REMOTE_DLL if set.

## Verified on 2026-10-02

- Live native handshake: type 3, version 3.1.3.0.
- 20 passes, 24 source/tap readings per pass: 480 available, zero unavailable.
- Distinct changing input/output readings observed. One input's pre-fader values
  varied while its post-mute readings remained at the floor. This is not a controlled
  mute test and does not infer a control's on/off state.
- Read-only Home Assistant Connection MCP access works for overview and template
  evaluation. HA Core 2026.9.4; 155 matching legacy entities, 16 legacy meter sensors.
  Two snapshots at 13:07:56 and 13:10:50 Europe/London had six changed meter values,
  no unavailable/unknown meter states, and no meter units. Web HA MCP's search endpoint
  and the old bulk-state tool returned Unknown tool; those failures do not mean HA
  itself is inaccessible.

The legacy sensors still describe raw channels. They must not be used as eight
correctly mapped strip or bus peaks, or presented as dBFS without validated conversion.
No new v2 MQTT publisher was started. The local card preview still uses mock fixtures.

## Evidence limits and next step

No matching installed header/manual has been located. The pinned public header and
manual in SDK-REFERENCE.md were reread; no new native export/signature was introduced.
This capture demonstrates the existing binding calls on the installed engine, not
general SDK compatibility, Unicode label support or advanced parameter capability.

Owner source labels/order, isolated-source audio tests, atomicity across sequential
channel reads and behavior across engine restart remain unverified. The 50 ms pause
between passes is diagnostic pacing, not measured 20 Hz throughput. No latency or
HA streaming claim follows from this short capture.

The [v2 runtime](V2-RUNTIME.md) now connects the session supervisor to the application's
owned native thread and MQTT lifecycle behind disabled-by-default flags, with runtime
identity checks and reconnect/shutdown tests. Prepare an isolated development publisher before
an approved broker run. Then capture native HA WebSocket events and run the blueprint's
10/20 Hz benchmarks. New native label APIs still require resolving the SDK evidence gap.
