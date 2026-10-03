# Release candidate 2.0.0-rc.1

The bridge and reusable card are published as **2.0.0-rc.1**. At the owner's explicit request, the Windows bridge was upgraded from 1.0.1, the card was installed through HACS, and a dedicated production Voicemeeter dashboard was configured. Private settings and the previous installation were backed up. Office Hub remains unchanged. Physical/audio and target-device acceptance still gate a stable release. See [deployment results](DEPLOYMENT-RESULTS.md).

## Implemented and verified

- Correct canonical 8-strip/8-bus peak registry, independent pre/post-mute input taps and output taps.
- Opt-in independent sample/fast/slow schedules, bounded pending telemetry, availability, metadata and reconnect generation ownership.
- Legacy command/discovery compatibility and configurable legacy raw cadence.
- SDK-qualified Unicode labels and version/readback-probed advanced controls with vendor bounds, retained-command rejection and actual readback.
- Cloned advanced settings/source editor, read-only source tests, counters, redacted export and atomic backup saves.
- One-source reusable card, visual editor, exact entity-registry suggestions, optional controls, shared native subscription, slow-sensor fallback, measured history, peak hold/decay, clipping and hidden cleanup.
- Standalone HACS repository exporter and self-contained Windows installer build, with explicit package allowlists excluding secrets/logs/live settings.
- 704 backend tests, 88 frontend tests, seven meter browser scenarios and eight control/editor browser scenarios pass. Atomic file backup and invalid-draft preservation are tested in isolated temporary directories. See [test results](TEST-RESULTS.md).
- Production and isolated-test installers compile. The isolated installer installs the exact published executable and uninstalls cleanly; the live AppData settings hash remains unchanged. The application is not launched by this test.

## Acceptance still required

- Confirm physical/audio assignments and mute/gain/routing/advanced audible behaviour on the owner's hardware. [Native labels](ACTUAL-SOURCE-MAPPING.md) are evidence of labels only.
- Complete the sustained HA matrix and target Fire tablet checks, including visible animation, multiple tabs, recovery, control responsiveness and browser CPU/memory. Desktop emulation cannot certify a Fire tablet.
- Test a subsequent HACS version update and exercise the documented live rollback when appropriate. The initial custom-repository install and backed-up live Windows upgrade are complete.
- Obtain separate approval before changing Office Hub; the dedicated production Voicemeeter dashboard is already deployed.
- Sign the release if desired. Current artifacts are unsigned; checksums detect changes, not publisher trust.

## Build and package

```powershell
dotnet test tests/VoicemeeterMqttBridge.Tests
node --test frontend/channel-card/test/*.test.js
dotnet publish VoicemeeterMqttBridge.csproj -c Release
ISCC installer/VoicemeeterMqttBridge.iss
./tools/PackageCard.ps1 -OutputPath artifacts/voicemeeter-channel-card-rc1
```

The exporter refuses an existing output directory and emits a standalone tree plus ZIP/SHA-256. It includes `dist/*.js`, `hacs.json`, the complete plain-language README/user guide, icon and attribution. That tree is published as [Dec64/voicemeeter-channel-card](https://github.com/Dec64/voicemeeter-channel-card), separate from the bridge repository. An actual HACS custom-repository installation succeeded; a subsequent version update has not yet been exercised.

Built local artifacts: `installer/output/VoicemeeterMqttBridgeSetup-2.0.0-rc.1.exe` and `artifacts/voicemeeter-channel-card-github-rc1.zip`, each with an adjacent `.sha256` file. The checksum files describe the final packages, including the icon and complete user guide. The isolated test installer is not a distribution artifact.

Inno Setup 6.7.3 was obtained from the [official download page](https://jrsoftware.org/isdl.php) and its valid Pyrsys signature checked before use. `/DIsolatedTestBuild` compiles an installer with a separate AppId/name for an isolated installation test. Production AppId remains compatible with the legacy installer. The installer copies only explicit safe files and does not delete AppData settings.

## Migration and rollback

1. Exit the installed bridge before replacing it. Back up `%APPDATA%/Voicemeeter MQTT Bridge/appsettings.json` and the old executable/installer. Keep credentials private.
2. V1 configuration loads with v2 disabled. Legacy enable/cadence inherit v1 fields; root/v2/source extension fields survive saves. No source mapping is invented.
3. In Settings, open Advanced metering/source mapping. Select verified sources and optional groups, apply the draft, save in the main dialog and restart. Fast is opt-in. Select 100 ms for a 10 Hz trial before increasing to 50 ms.
4. Save writes a temporary complete draft, atomically replaces the file and stores the prior settings as `appsettings.json.bak`. A failed disk save does not mutate active settings. Reset v2 only preserves broker configuration.
5. For rollback, exit the bridge, restore the private prior configuration and old executable/installer, then restart. Do not restore all HA storage to undo one resource or dashboard change.
6. HA test rollback restores the prior resource URL and the specific saved test-dashboard configuration via the Lovelace API/UI. Old staged module directories are retained. A disabled v2 profile may leave retained metadata/discovery; stale metadata never authorizes fresh readings. Retire obsolete discovery configs explicitly after checking consumers.

See [HA setup](HA-SETUP.md), [protocol](MQTT-V2-PROTOCOL.md) and [benchmark interpretation](BENCHMARKING.md). The five-card integration example is reviewable configuration only; deploy to Office Hub only after approval.
