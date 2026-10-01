# Phase 0 and Phase 1 foundation verification

Run date: **2026-09-29**. Local branch: `codex/phase0-mapping-tests`.
Base: `0602793c73313b0e6c3705eb8ecb00d03ebeab75`.
The owner authorized the local Phase 0 commit after reviewing these results. Find its SHA in Git history by the subject below; no remote PR or push is authorized.

Scope document: `Voicemeeter_MQTT_Bridge_v2_Codex_Blueprint.md`, read in full; SHA-256 `d16346074df29414beff888f4793344472d9c19a5307193545122d391091df37`. The direct request selects Phase 0 and deterministic Phase 1 channel tests, not the complete v2 product.

## Results

| Check | Observed result |
|---|---|
| Fresh upstream build before edits | PASS with scratch SDK 8.0.425; one existing CS1998 warning in `SettingsForm.SaveSettingsAsync`. System SDK 7.0.302 initially failed with NETSDK1045. |
| Legacy characterization harness | **9 passed**: 98 raw meter values, all 155 discovery identities/topics, mapped and generic command readback, availability, existing JSON values/defaults. |
| New mapping/conversion tests | **395 passed**. Includes all 34 input channels at modes 0/1/2 and all 64 output channels at mode 3. |
| Entire solution, final code | **404 passed, 0 failed, 0 skipped**, VSTest 17.11.1 / .NET 8.0.31. Reported test duration 548 ms; this is test execution time, not a telemetry benchmark. |
| Wrong-map experiment | Temporarily changed virtual start offset 10 to 9. All **166** channel-isolation cases failed, proving the independent expectations detect the fault. Correct map restored before final tests/build. |
| Existing `build.ps1` | PASS: restore, Release build and self-contained win-x64 publish; script unchanged. |
| Publish content inspection | EXE, PDB and demo JSON only. No live appsettings, logs, vendor Remote DLL, xUnit, Moq, testhost or test assemblies. |
| Read-only live HA follow-up | Home Assistant Connection MCP works. All 155 live registry entity IDs/unique IDs match the SMB inventory; all enabled, MQTT, one device. Two snapshots contain all 155 states with none unknown/unavailable; four input meter values changed. This is passive observation, not a command roundtrip or streaming benchmark. |
| Diff whitespace check | PASS (`git diff --check`). No separate repository lint configuration exists; the C# build supplies compiler checking. |
| Local review | `ce-simplify-code` reuse/quality/efficiency passes, inline per supplied AGENTS.md: no changes warranted (0 applied, 0 rejected findings). `ce-code-review` lite correctness/requirements/test review completed with no actionable findings; no independent reviewer or deployment review claimed. |

The pre-existing warning is intentionally not repaired as part of this slice. Before implementation, mapping tests also failed to compile because the new mapping types did not exist; the later wrong-map experiment supplies a behavioral failure check rather than relying only on that compile failure.

## What the tests prove

| Blueprint ID | Evidence |
|---|---|
| MAP-01 / MAP-02 / MAP-03 | Independent literal channel tables, exhaustive one-hot peaks, exact reads, stable unique IDs and complete ownership of 0–33 input / 0–63 output channels. Every test also checks all other source/tap combinations stay isolated. |
| MAP-04 | A failed read at every channel position affects only its source/tap; no previous/source-neighbor values are reused. Invalid negative and non-finite values are unavailable. |
| DB-01 / DB-02 | Known linear values, custom floor, finite silence, over-unity peaks, maximum of included channels, invalid-value rejection. |
| MUTE-01 | Independent pre-fader, post-fader and post-mute samples; buses permit output only. |
| MQTT-06 (in memory only) | Existing bridge methods generate unchanged raw meters, discovery IDs, command/state topics and retained availability through a mocked MQTTnet client. This is not a live broker/HA test. |
| CFG baseline only | Synthetic JSON preserves all 19 existing properties and topic expansion; missing properties use existing defaults. **CFG-01 v2 migration is not implemented or claimed passed.** |

Test safety: fake Remote methods reject startup/login/poll-loop operations. Tests never call `BridgeService.StartAsync`, live settings `Load`/`Save`, the native DLL or a broker connection. Command/publication logs use an injected sink. Synthetic credentials are plainly test fixtures. Test results and owner inventories are outside Git.

## Read-only HA verification

The working **Home Assistant Connection** MCP confirms HA Core 2026.9.4, HACS 2.0.5, HA OS 18.2, Supervisor 2026.09.3 and Mosquitto addon 7.1.1. MQTT integration state is loaded. The two separately exposed **Web HA MCP** account links still require reauthentication; this no longer blocks read-only inventory through the working connection.

Calls used: `ha_get_system_health({})`, `ha_get_integration({query: "mqtt", include_options: false})`, `ha_get_entity({entity_id: <155 IDs from private inventory>})`, and `ha_eval_template` selecting those same entity IDs from `states` and returning state, unit, last-updated and last-reported values. `ha_get_state` also verified one legacy input meter individually. The legacy connector's advertised `ha_get_states` batch method returned unknown-tool; template evaluation supplied the complete snapshots instead. Transient HTTP 503 responses were followed by successful reads.

Snapshots at **2026-09-29 17:38:53 UTC** and **17:41:53 UTC** each contain 32 numbers, 107 switches and 16 sensors, with zero missing, unknown or unavailable entities. Four input meter values changed between them. All 16 meter units are absent, consistent with the existing raw payload. State presence or a zero value does not establish virtual Comp/Gate support. These snapshots do not measure cadence or latency and do not confirm Guest/External/System/Chat/Music source ordering. No control command, HA service action, config write or bridge restart was performed.

Selected live registry records and both state snapshots are saved privately in `../.local-phase0/ha-live-verification.private.json`, outside Git. This follow-up changed documentation only; the recorded 404-test result applies to the unchanged code and was not rerun unnecessarily.

## Exact commands

The following PowerShell commands reproduce this session from the clone. `$sdk` is the scratch SDK directory beside the clone; an independently installed .NET 8 SDK can be substituted. The process-local PATH change is necessary only for the unchanged upstream build script's `dotnet` calls.

```powershell
Set-Location 'C:\Users\Declan\Projects\VA MQTT HA\Voicemeeter-MQTT-Bridge'
$sdk = (Resolve-Path '..\.local-phase0\dotnet').Path
$dotnet = Join-Path $sdk 'dotnet.exe'

# Baseline/final application compile (does not run the bridge).
& $dotnet build .\VoicemeeterMqttBridge.csproj -c Release

# Final whole-solution verification; omitting --no-restore permits a fresh test checkout.
& $dotnet test .\Voicemeeter-MQTT-Bridge.sln -c Release --logger 'trx;LogFileName=phase0-mapping.trx' --results-directory '..\.local-phase0\test-results'

# Optional focused reruns.
& $dotnet test .\tests\VoicemeeterMqttBridge.Tests\VoicemeeterMqttBridge.Tests.csproj -c Release --filter 'FullyQualifiedName~ChannelMappingTests'
& $dotnet test .\tests\VoicemeeterMqttBridge.Tests\VoicemeeterMqttBridge.Tests.csproj -c Release --filter 'FullyQualifiedName~LegacyCompatibilityTests'

# Existing build/publish path; does not install or launch anything.
$env:PATH = $sdk + ';' + $env:PATH
$env:DOTNET_ROOT = $sdk
& .\build.ps1 -ProjectFile 'VoicemeeterMqttBridge.csproj'
git diff --check
```

The recorded final solution run included `--no-restore` after successful restore. The wrong-map run used `--filter 'FullyQualifiedName~Every_channel_is_measured_only_by_its_owner_and_selected_tap'`; do not leave the intentional mutation applied.

Local EXE: `bin/Release/net8.0-windows/win-x64/publish/VoicemeeterMqttBridge.exe`.
SHA-256 of this build: `0b73504e364d98f3ac25b5446a88e29fa83ada101e42ce346621ee1a2f64f9a9`.
It has not been installed/launched or signed by this work. It still carries upstream version 1.0.1 and is **not a v2 release**.

## Pending evidence and remaining work

The additional local foundation below supersedes the earlier statement that v2 JSON defaults, source descriptors and aggregate serialization had not been implemented. It does not complete Phase 1 or enable a feed.

- Matching **installed** header/manual and actual DLL feature probes; official reference evidence is recorded separately in [SDK-REFERENCE.md](SDK-REFERENCE.md).
- Actual Guest/External/System/Chat/Music strip assignments, source labels and isolated physical-audio tests. No assignment was guessed.
- Live command feedback, control capability and frontend permissions/event shape. Passive MCP reads establish current availability of all 155 entities and some changing telemetry; SMB dashboard reads establish 139 direct Office Hub references. No command roundtrip was attempted.
- File-level migration/rollback and advanced settings UI, native string getter, probed capabilities, API owner/lifecycle, timed peak windows, activity/clip hysteresis and bounded queues remain later implementation. JSON migration, source descriptors, aggregate serialization and the later control-publication slice are covered below.
- No active broker/HA integration tests, native `subscribe_trigger` experiment or latency/throughput benchmark ran. Passive live reads above do not prove 20 Hz or p95 <250 ms.
- The reusable **one-strip-or-bus** HACS card, visual editor, per-instance taps/history, shared subscription and slow fallback remain mandatory. No fixed mixer substitutes for it; conditional backend decision waits for native-path evidence.
- Installer build, install/upgrade/rollback and backup restore remain unverified. Backups of live settings/installer/HA config are required before an approved migration/deployment; none was attempted here.
- Licensing metadata inconsistency remains unresolved before release; see [COMPATIBILITY-AUDIT.md](COMPATIBILITY-AUDIT.md).

Phase 0's local source/harness/build work is complete. Runtime-only verification remains pending, as the blueprint permits when inaccessible. Only the deterministic portion of Phase 1 is complete. No live HA files or installed bridge were modified; no GitHub repository/fork/push was created.

## Phase 0 commit

`test: establish Phase 0 baseline and verify Potato channel mapping`

Includes this audit, SDK/test evidence, dependency-injection seams, canonical map/sampler, test harness and solution/build exclusions. Credentials, private inventories, vendor reference downloads, SDK, test logs and binaries remain outside Git. Publishing remains a separate approval step.

After that commit, the next implementation slice should add the version-gated source registry and confirmed label reads, explicit v2 defaults/migration tests and aggregate-payload tests before enabling any runtime feed.

## Follow-up: local settings, source registry and aggregate contract

Phase 0 was committed as **6692753** (`test: establish Phase 0 baseline and verify Potato channel mapping`). The next local slice adds the settings/registry/serialization foundation described in [MQTT-V2-PROTOCOL.md](MQTT-V2-PROTOCOL.md). Native metadata remains a test boundary: no new DLL binding or live label read was implemented.

| Check | Observed result |
|---|---|
| JSON migration | **14 tests passed**: representative v1 credentials/flags preserved, disabled defaults with no guessed profiles, inherited legacy cadence, Unicode and unknown fields, null-object/order independence, idempotence and invalid configuration rejection. Tests use synthetic JSON, not live AppData files. |
| Source registry | **14 tests passed**: all 16 descriptors, supported engine identity gate, label fallback/overrides, stable IDs, canonical channels/taps, alias uniqueness and invalid source/tap rejection. Capability groups stay empty pending probes. |
| Aggregate contract | **15 tests passed**: isolated enabled sources/taps, finite levels, missing/failed sources, maximum observed values, session/sequence, metadata, rejected stray samples and registry-to-sampler-to-frame composition. No broker or scheduler participates. |
| Full solution after restoring mutation | **447 passed, 0 failed, 0 skipped**, reported duration 551 ms; this is not a telemetry benchmark. Existing 404 tests remain passing, with the legacy settings assertion allowing only the additional v2 object. |
| Proof before implementation | Both new migration tests failed on missing JSON fields. Registry/frame tests initially failed compilation because their types did not exist. |
| Behavioral fault experiment | Replacing window `Math.Max` with `Math.Min` caused the peak-preservation test to fail (expected 0 dBFS, got -90). Correct code restored; final 447-test run passed. |
| Build and publish | Existing `build.ps1` succeeds. Publish directory contains only EXE, PDB and demo settings; no live settings, logs, vendor DLL or test dependencies. |
| Review | Reuse/quality/efficiency simplification passes required no edits. Correctness, testing, maintainability, security, API-contract, reliability and adversarial lenses completed sequentially in the main agent per supplied AGENTS.md; no actionable findings. No independent reviewer or cross-model validation is claimed. |

Exact follow-up commands from the clone, using the same scratch SDK:

```powershell
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --logger 'trx;LogFileName=phase1-foundation.trx' --results-directory '..\.local-phase0\test-results'
& '..\.local-phase0\dotnet\dotnet.exe' test '.\tests\VoicemeeterMqttBridge.Tests\VoicemeeterMqttBridge.Tests.csproj' -c Release --no-restore --filter 'FullyQualifiedName~SettingsMigrationTests|FullyQualifiedName~SourceRegistryTests|FullyQualifiedName~AggregateFrameTests'
```

Use the earlier build-script commands to publish locally. The earlier EXE hash belongs to the Phase 0 build, not the follow-up. No installer, installed bridge, live settings or HA configuration was changed. Advanced settings are preserved but no UI for enabling them is added yet. `CFG-01` has JSON-level proof only; filesystem migration/rollback remains unverified. `CAP-02` has metadata-boundary proof only; native Unicode read/write remains unverified.

Follow-up EXE SHA-256: `85340c06d8a28631e0c06f2fd6dcbbb84604b17b3dc5a6affce5e3912e43a951`. It was not installed or launched. Review receipt: private temporary run `20260929-phase1-foundation`, status complete; whitespace check passes. No standalone lint is configured.

Local follow-up commit subject: `feat: add v2 settings, source registry and aggregate frames`.
Proposed next commit: `fix: publish only changed controls and handle Remote API errors`. The native label binding still requires the matching installed header/manual or an explicit decision to use the documented official SDK reference instead. The remaining Phase 1/runtime and modular-card requirements are not marked complete.

## Follow-up: changed controls and Remote API failures (2026-09-30)

Foundation commit `2494324` precedes this slice. [CONTROL-PUBLISHING.md](CONTROL-PUBLISHING.md) records the implemented contract and trust boundary.

| Check | Observed result |
|---|---|
| Proof before implementation | 11 of 12 new regression cases failed as expected: failed sets still read/published, non-finite commands reached the adapter, and non-finite readings became retained state. Actual-readback characterization passed. |
| Control tests | **29 cases passed**: tolerance/accumulation, switch projection, changed-only scans, zero/negative/throwing dirty poll, unloaded-adapter health, recovery, full resync, real connected-callback wiring, optional monotonic reconciliation, isolated read failure, failed/offline/rejected send retry, forced-send failure, serialized command/scan and generic endpoint compatibility. |
| Final solution | **476 passed, 0 failed, 0 skipped**, reported duration 503 ms. This includes all prior mapping, settings, registry, aggregate and legacy compatibility tests. It is not a load or latency benchmark. |
| Build | Existing Release build/publish script succeeds after the review fixes. The known CS1998 warning remains on the unchanged settings-form save method; no new compiler/analyzer warning remains. No standalone lint is configured. |
| Review | Seven lenses ran sequentially in the main agent per supplied AGENTS.md. Two findings were applied: move BridgeService out of Program.cs and test a thrown dirty poll plus recovery. Full suite reran after both fixes. No independent or cross-model review is claimed. |

Final follow-up inspection also found that the unloaded native adapter's default dirty value could report a false `Connected` status. A test first reproduced that exact failure; the adapter now throws on an unbound dirty delegate. This test constructs the adapter but never calls Load/Login or enters native code. The final suite and publish build ran after this correction as well.

Exact commands from the clone:

```powershell
# Focused regression run (the first run, before fixes, recorded the failures above).
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --filter FullyQualifiedName~ControlPublishingTests --logger 'trx;LogFileName=phase1-controls-red.trx' --results-directory '..\.local-phase0\test-results'

# Additional unloaded-adapter regression, first run before its correction.
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --filter FullyQualifiedName~Unloaded_native_adapter_cannot_report_a_healthy_poll --logger 'trx;LogFileName=phase1-controls-unloaded-red.trx' --results-directory '..\.local-phase0\test-results'

# Final complete suite after review fixes.
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --logger 'trx;LogFileName=phase1-controls.trx' --results-directory '..\.local-phase0\test-results'

$env:PATH=(Resolve-Path '..\.local-phase0\dotnet').Path+';'+$env:PATH
$env:DOTNET_ROOT=(Resolve-Path '..\.local-phase0\dotnet').Path
& .\build.ps1 -ProjectFile 'VoicemeeterMqttBridge.csproj'
git diff --check
```

The root JSON gains `controlReconcileIntervalMs`; legacy fields and unknown fields remain preserved. No settings were loaded from or saved to live AppData. The original matching installed SDK, native thread/lifecycle, live command feedback, physical audio mapping, capabilities, labels, filesystem migration and rollback remain unverified. Legacy meter mapping is still deliberately unchanged in the runtime compatibility feed; the deterministic corrected map is not yet wired into a v2 publisher. Full Phase 1, the modular HACS card and HA streaming acceptance criteria remain open.

Review receipt: private temporary run `20260930-phase1-controls`, status complete; both findings applied and verified. No live HA/SMB files or installed bridge were modified, no external messages were sent, and no GitHub repository, push or PR was created.

Final local EXE SHA-256: `1d70e4f790e932e29103b600e34b1120fb60f6ef8d131a74a74e5f0d4f011997`. Publish output contains only the EXE, PDB and demo JSON. It was not installed or launched and still carries upstream version 1.0.1; it is not a v2 release.

Local commit subject: `fix: publish only changed controls and handle Remote API errors`.
Proposed next commit: `fix: give Remote API calls one owner and correct login lifecycle`, with deterministic fake-adapter lifecycle tests before any live probe or new native binding.

## Follow-up: Remote API owner and login lifecycle

The control-publication slice was committed as `d63a707`. The next slice implements [the lifecycle contract](REMOTE-LIFECYCLE.md) using existing native signatures only.

| Check | Observed result |
|---|---|
| New owner/lifecycle tests | **19 cases** cover one physical thread, registration results, idempotence, failed-registration retry, engine recovery, launch fallback, exception isolation, stop before start, shutdown during login/commands and no late native calls. |
| Complete suite | **495 passed, 0 failed, 0 skipped**, reported duration 538 ms. These are fake-adapter/unit tests, not native or HA performance evidence. |
| Test-first evidence | Owner tests initially failed compilation because `RemoteApiOwner` did not exist. This is a missing-type red result, not a behavioral failure of the old bridge. |
| Build | Release build and local publish checked using the existing script. The clean compilation still reports the pre-existing CS1998 settings-save warning; the incremental build may report zero warnings. No standalone lint is configured. |
| Review | Reuse/quality/efficiency and correctness/testing/maintainability/reliability/adversarial checks ran sequentially in the main agent per user instructions. The implementation fixes initialization/shutdown ordering and adds retry/stop-before-start coverage. No independent or cross-model review is claimed. |

Exact commands from `Voicemeeter-MQTT-Bridge`:

```powershell
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --filter 'FullyQualifiedName~RemoteApiOwnerTests|FullyQualifiedName~BridgeLifecycleTests'
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --logger 'trx;LogFileName=phase1-lifecycle.trx' --results-directory '..\.local-phase0\test-results'
$env:PATH=(Resolve-Path '..\.local-phase0\dotnet').Path+';'+$env:PATH
$env:DOTNET_ROOT=(Resolve-Path '..\.local-phase0\dotnet').Path
& .\build.ps1 -ProjectFile 'VoicemeeterMqttBridge.csproj'
git diff --check
```

No installed native API, physical audio, live HA stream or release deployment was tested. New bindings still await matching installed SDK evidence. Native-call hangs, load/queue saturation, the separate MQTT transport shutdown lifecycle, capabilities, settings-file migration/rollback, license reconciliation and the modular HACS card remain open. V2 remains disabled by default and its corrected map is not yet wired into the legacy runtime feed.

Review receipt: private run `20260930-phase1-lifecycle`, sequential inline checks; no independent coverage. No live HA/SMB, installed bridge or AppData settings changes; no repository creation, push or PR.

Local publish contains only EXE, PDB and demo settings. EXE SHA-256: `9098c8f09fbfb360db7bbe47dc1d2c054dce3aaca8b0ff061c1e3d2b278bea73`. It was not launched or installed.

Local commit subject: `fix: give Remote API calls one owner and correct login lifecycle`.
Proposed next commit: `feat: add deterministic timed peak windows and meter activity state`, keeping the v2 runtime feed disabled until its integration and benchmark gates are met.

## Follow-up: timed peak windows and activity state

The lifecycle slice was committed as `4cc3754`. [MeterWindowAccumulator](METER-WINDOWS.md) now preserves observed interval peaks with independent per-tap activity/clipping hysteresis and holds. It is an offline building block, not connected to the existing frame serializer or live publisher.

- **18 new cases; 513 total passed, zero failed/skipped**, reported duration 575 ms. Fake monotonic time drives every new timing test; no sleeps, DLL, broker or HA access.
- Coverage includes independent fast/slow windows, peak reset and immutable snapshots, activity hysteresis/hold boundaries, immediate inactive silence, clip hold, tap/source isolation, invalid/missing readings, recovery, rejected samples, positive duration, settings round trips and invalid settings.
- Initial test-first run failed on the missing accumulator types. Two subsequent behavioral regressions were reproduced before correction: exact clip threshold with zero hold/hysteresis, and accepting a clip release threshold that silence could never cross. Both corrected cases pass in the final suite.
- Release build/publish succeeds. The existing CS1998 warning remains on the unchanged settings save method; no new warning or standalone lint configuration. EXE SHA-256: `1434fcfc34ac69b7d44c3acd9f98dcda47c64d09de8bd2edb9fcb86925f86227`.
- Simplification and focused correctness/adversarial checks completed sequentially in the main agent. Private review run: `20260930-phase1-windows`, status complete, no remaining actionable finding. No independent reviewer or cross-model coverage is claimed.

Exact commands from the clone:

```powershell
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --filter FullyQualifiedName~MeterWindowTests
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --logger 'trx;LogFileName=phase1-windows.trx' --results-directory '..\.local-phase0\test-results'
$env:PATH=(Resolve-Path '..\.local-phase0\dotnet').Path+';'+$env:PATH
$env:DOTNET_ROOT=(Resolve-Path '..\.local-phase0\dotnet').Path
& .\build.ps1 -ProjectFile 'VoicemeeterMqttBridge.csproj'
git diff --check
```

The hold/hysteresis settings are additive and v2 remains disabled. Runtime scheduling, missing-poll/stale detection, snapshot serialization, bounded MQTT transport, native/physical validation and HA 10/20 Hz measurements remain pending. The modular one-strip-or-bus HACS card, visual editor and shared subscription remain required. No live HA/SMB, installed bridge or AppData files were modified; nothing was pushed or deployed.

Proposed next commit: `feat: serialize timed meter snapshots and coalesce pending telemetry`, still without enabling a live v2 feed.

## Follow-up: timed snapshot serialization and pending telemetry

The timed-window slice was committed as `b045508`. The serializer now preserves the accumulator's activity and clipping flags. A capacity-one queue replaces pending snapshots with newer ones; serialization happens after dequeue so the caller can supply the publication timestamp then. This remains an offline building block with no live publisher.

- **13 new cases; 526 total passed, zero failed/skipped**, reported duration 543 ms. Coverage includes held clipping, current activity versus interval peak, selected-tap isolation, malformed/duplicate snapshots, configured window duration, queue replacement, concurrent producers, cancellation, completion and accumulator-to-message composition.
- Initial test-first compilation failed because the queue type and snapshot serializer signature did not yet exist. No behavioral red result is claimed for this slice.
- Release build/publish succeeds. The pre-existing CS1998 settings-save warning remains; incremental publish reported no warnings. EXE SHA-256: `616cfe7295bc94a6a13d791b1d66c7d5ea5636dcd3d31fb001b95faa7403a845`. The EXE was neither installed nor launched.
- Reuse/quality/efficiency, correctness, testing, maintainability, API-contract, reliability and adversarial checks ran sequentially in the main agent. Private review run `20260930-phase1-telemetry-queue`: complete, no remaining actionable findings. No independent or cross-model review is claimed.

Exact final verification commands from the clone:

```powershell
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --logger 'trx;LogFileName=phase1-telemetry-queue.trx' --results-directory '..\.local-phase0\test-results'
$env:PATH=(Resolve-Path '..\.local-phase0\dotnet').Path+';'+$env:PATH
$env:DOTNET_ROOT=(Resolve-Path '..\.local-phase0\dotnet').Path
& .\build.ps1 -ProjectFile 'VoicemeeterMqttBridge.csproj'
git diff --check
```

Sampling schedules, stale-snapshot expiry, reconnect/session handling and live MQTT integration remain pending. The installed SDK, physical audio, HA 10/20 Hz performance, license reconciliation and migration/rollback remain unverified. The required modular one-strip-or-bus HACS card, visual editor and shared subscription remain outstanding. V2 stays disabled; no live HA/SMB, installed bridge or AppData settings changes, repository creation, push or deployment occurred.

Proposed next commit: `feat: track meter freshness and reject stale pending snapshots`, with fake-time tests before connecting a live publisher.

## Follow-up: reject stale pending snapshots

The serialization/queue slice was committed as `710339f`. Snapshots now retain a monotonic measurement-window origin. `IsFresh` and the queue's `ReadFreshAsync` reject expired or untracked readings without resetting age when queued. This is a local telemetry building block; the running legacy bridge remains unchanged.

- **Nine new cases; 535 total passed, zero failed/skipped**, reported duration 568 ms. Tests cover exact expiry, measurement time, late enqueue, expiry while pending, unknown age, recovery, cancellation, invalid budgets, new window origins, fresh unavailable readings and negative elapsed time.
- Test-first compilation failed on the missing freshness methods before implementation. This was an absent-interface failure, not a reproduced behavioral bug in the legacy bridge.
- Release build/publish passed. The pre-existing CS1998 settings-save warning remains on clean compilation; incremental publish reported zero warnings. No standalone lint is configured. EXE SHA-256: `29cb1bec4b1f0e0a805d69365b4ca5947d79efb6c15c15011e34a0c12cee43c6`.
- Simplification and focused correctness/adversarial checks completed sequentially in the main agent, per user instructions. Private review run `20260930-phase1-freshness`: complete, no actionable findings; no independent or cross-model review claimed.

Exact commands from the clone:

```powershell
# First run before implementation failed compilation; the subsequent focused run passed seven initial cases.
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --filter FullyQualifiedName~MeterFreshnessTests
# Final suite includes two additional review cases.
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --logger 'trx;LogFileName=phase1-freshness.trx' --results-directory '..\.local-phase0\test-results'
$env:PATH=(Resolve-Path '..\.local-phase0\dotnet').Path+';'+$env:PATH
$env:DOTNET_ROOT=(Resolve-Path '..\.local-phase0\dotnet').Path
& .\build.ps1 -ProjectFile 'VoicemeeterMqttBridge.csproj'
git diff --check
```

The caller must choose a budget greater than each measurement cadence and recheck after waits before transport submission. Connection/session invalidation, already-started sends and missed polls within a window are not solved by an age check. Runtime scheduling, MQTT integration, installed SDK/physical verification, advanced capabilities, migration/rollback, licensing and HA benchmarks remain open. The modular HACS card, visual editor and shared subscription remain required. Nothing was installed, launched, pushed or changed in live HA/SMB/AppData.

Proposed next commit: `feat: schedule independent meter windows with bounded telemetry`, using fake sampling/transport and keeping live v2 disabled.

## Follow-up: independent fast/slow scheduling

The freshness slice was committed as `909e2f5`. [MeterTelemetryLoop](METER-SCHEDULER.md) now feeds independently timed peak windows and bounded queues from one sampling pass. It uses a real `PeriodicTimer` with an injectable `TimeProvider`; no application startup path instantiates it.

- **13 new cases; 548 total passed, zero failed/skipped**, reported duration 573 ms. Fake-clock tests cover independent peaks, exact source/tap reads, enable flags, empty profiles, slow-only and fast-only operation, delayed/coalesced ticks, unread queues, error isolation/recovery, cancellation, timer cleanup, unexpected faults, concurrent/repeated starts, settings capture and invalid configuration.
- Test-first compilation initially failed because the loop type did not exist. Two later behavioral tests reproduced skipped deadlines: variable sample duration and a publishing cadence not divisible by the sample interval. Both failed by timing out waiting for their second due frame. Anchoring deadlines to the session clock fixed them; the final suite includes both regressions.
- Release build/publish passed; the known CS1998 settings-save warning remains on clean compilation, while incremental publish reported zero warnings. No standalone lint is configured. EXE SHA-256: `1ab0d058694c83ddb88353f3501e475ba08e0be945be01e3c755b1d41293af48`.
- Reuse, quality and efficiency checks plus focused correctness/adversarial review ran sequentially in the main agent, per user instructions. Private review run `20260930-phase2-scheduler`: complete, no remaining actionable findings; no independent or cross-model review claimed.

Exact commands from the clone:

```powershell
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --filter FullyQualifiedName~MeterTelemetryLoopTests
# Behavioral regression run before the deadline fix (two failures).
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --filter 'FullyQualifiedName~Variable_sample_duration|FullyQualifiedName~Non_multiple_cadence' --logger 'trx;LogFileName=phase2-scheduler-red.trx' --results-directory '..\.local-phase0\test-results'
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --logger 'trx;LogFileName=phase2-scheduler.trx' --results-directory '..\.local-phase0\test-results'
$env:PATH=(Resolve-Path '..\.local-phase0\dotnet').Path+';'+$env:PATH
$env:DOTNET_ROOT=(Resolve-Path '..\.local-phase0\dotnet').Path
& .\build.ps1 -ProjectFile 'VoicemeeterMqttBridge.csproj'
git diff --check
```

This proves deterministic scheduling and bounded queue behavior, not actual DLL throughput, command responsiveness, MQTT delivery or HA 10/20 Hz performance. Native owner/metadata integration, reconnect supervision, publishing/discovery, instrumentation, advanced capabilities, migration/rollback and licensing remain open. The required modular HACS card, visual editor and shared subscription remain outstanding. No live HA/SMB/AppData, installed bridge, repository creation or push was involved; the executable was neither installed nor launched.

Proposed next commit: `feat: publish fresh v2 meter frames through bounded transport`, with a fake MQTT client, cancellation/stall tests and non-retained QoS 0 assertions before any live integration.

## Follow-up: bounded v2 MQTT publication

The scheduler slice was committed as `77f937b`. [MeterTelemetryPublisher](METER-PUBLISHER.md) now consumes fresh snapshots on an injected connected MQTT client. Fast and slow are non-retained QoS 0; slow payloads explicitly declare `sensor_tap` and `window_ms`. No application startup path creates this publisher.

- **20 new cases; 568 total passed, zero failed/skipped**, reported duration 526 ms. Coverage includes both wire contracts, shared session/sequence, bounded pending work during stalls, independent consumers, cancellation reaching and draining sends, failure/rejection propagation, stale discard before and after formatting, timestamp assignment, disabled streams, disconnected clients, repeat start, invalid topics, source availability/tap selection, age budgets and shared-queue rejection.
- Test-first compilation failed on the missing publisher type. A subsequent behavioral regression reproduced acceptance of the same single-reader queue for both streams; the guard was added and that test passes. No live broker behavior is inferred from the fake client.
- Release build/publish passed. Clean compilation still reports the known CS1998 settings-save warning; incremental publish reported zero warnings. No standalone lint is configured. EXE SHA-256: `07b9bb777abda6f527bc5a18256ad4f9d614134feffe503d2f098c28a2f55d49`.
- Simplification plus correctness, testing, maintainability, API-contract, reliability, security and adversarial review completed sequentially in the main agent, per user instructions. Private run `20261001-phase2-publisher`: complete, no remaining actionable findings; no independent or cross-model review claimed.

Exact commands from the clone:

```powershell
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --filter FullyQualifiedName~MeterTelemetryPublisherTests
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --filter 'FullyQualifiedName~MeterTelemetryPublisherTests|FullyQualifiedName~AggregateFrameTests'
# Behavioral regression before the shared-queue guard (one failure).
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --filter FullyQualifiedName~Fast_and_slow_cannot_share --logger 'trx;LogFileName=phase2-publisher-red.trx' --results-directory '..\.local-phase0\test-results'
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --logger 'trx;LogFileName=phase2-publisher.trx' --results-directory '..\.local-phase0\test-results'
$env:PATH=(Resolve-Path '..\.local-phase0\dotnet').Path+';'+$env:PATH
$env:DOTNET_ROOT=(Resolve-Path '..\.local-phase0\dotnet').Path
& .\build.ps1 -ProjectFile 'VoicemeeterMqttBridge.csproj'
git diff --check
```

Session supervision and reconnect, real-client cancellation/deadlines, retained metadata/status and slow discovery remain pending. A send already handed to MQTT cannot be recalled, and a client ignoring cancellation can hold shutdown; the publisher awaits actual sends rather than spawning replacements. Installed SDK, physical source validation, advanced capabilities, migration/rollback, license reconciliation and HA performance remain unverified. The modular HACS card, visual editor and shared subscription remain required. Nothing was installed, launched, pushed or changed in live HA/SMB/AppData.

Proposed next commit: `feat: supervise v2 telemetry session shutdown and restart`, with fake connection lifecycle tests before live wiring.
