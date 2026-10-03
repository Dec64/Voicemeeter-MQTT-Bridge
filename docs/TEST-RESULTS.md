# Verification history and release candidate results

## Current release candidate — 2026-10-03

The historical entries below retain the scope and limitations of their original runs. The following results supersede earlier implementation and test counts, but do not certify owner audio/device acceptance or public release.

| Check | Latest result |
|---|---|
| Backend suite | 704 passed, zero failures/skips. Includes guarded advanced commands, actual readback, immutable drafts, redacted export, atomic backup files and rejection before replacement. |
| Frontend suite | 88 passed. Complete 16-source EQ metadata survives bounded shared fan-out; normal shared feeds retain their smaller default bound. |
| Meter browser checks | Seven scenarios pass, including stream restart clearing measured history and peak hold. |
| Control/editor browser checks | Eight scenarios pass, including lazy advanced controls, invalid-range rejection and actual HA readback. |
| Settings layout | Real WinForms renders at 1000×800 and minimum 850×650 inspected; source grid and all action buttons remain accessible. |
| Release publish/installers | Self-contained win-x64 publish and both Inno Setup builds pass. Separate-AppId install/uninstall passes, exact executable hash matches and live settings hash stays unchanged. |
| Card distribution | Standalone dist/hacs.json/README/license tree and ZIP/SHA-256 prepared. Actual HACS install/update requires publication. |
| Native labels/capabilities | Potato 3.1.3.0 label reads and allowlisted advanced readbacks observed. Physical inputs expose 261, virtual inputs four and buses 243 controls with all groups enabled. No live audio control write was issued. |
| Broker, requested 20 Hz | 30-minute observation: 18.69 accepted frames/sec, zero rejected frames; startup/stalls included. |
| Broker, requested 10 Hz | 30-minute observations: 9.95 frames/sec without advanced groups and 9.95/9.93 with all groups, zero rejected/unavailable readings. Point working-set samples about 75–80 MB; these are not peak measurements. |
| HA 20 Hz | 15-minute report: 18.79 accepted frames/sec, p95 publication-to-DOM estimate 98 ms, but only 896 DOM updates and 16,010 superseded frames. This does not prove fluid visible 20 Hz rendering. |
| HA 10 Hz, sixteen-card view | A run stopped on hidden-tab cleanup at 609.7 seconds: 9.99 frames/sec, 6,018 DOM updates, 72 superseded frames and p95 publication-to-DOM estimate 67 ms. This is shorter than required sustained acceptance. |
| HA 10 Hz, completed repeat | Full 900-second run on the final card bundle: 8,993 accepted frames (9.99 Hz), 8,946 DOM updates, 47 superseded frames, zero unpainted frames, p95 publication-to-DOM estimate 63 ms. Selected source strip:0; sixteen configured cards, not a claim that all remained visible. |

Evidence is in [native 20 Hz HA report](evidence/native-20hz-ha.json), [native 10 Hz broker report](evidence/native-10hz-broker.json), [all-groups broker report](evidence/native-10hz-advanced-broker.json) and [partial 10 Hz HA report](evidence/native-10hz-ha-hidden.json). UTC estimates include clock skew; DOM update time is not physical screen presentation latency. A dashboard with sixteen configured cards does not establish that all sixteen remained visible. The viewport override did not visibly change the controlled browser's layout, so the full visible matrix remains unclaimed.

The final SettingsPreview regression invokes the real Apply button and verifies that an existing 50 ms legacy meter interval survives unchanged. The source grid also displays supported meter taps. The final installer was rebuilt after this check and passed isolated installation/uninstallation with the exact current executable and unchanged live settings.

Remaining release gates are enumerated in [release readiness](RELEASE-READINESS.md): owner source/audio confirmation, full 1/5/8/16-card 10/20 Hz device matrix, restart-order/recovery and live controls, actual HACS distribution and authorized installed-bridge/Office Hub deployment. A reachable tablet remote page is not a tablet card test. See the [final review](RELEASE-REVIEW.md).

The [completed 10 Hz HA report](evidence/native-10hz-ha-complete.json) supports retaining the native transport for desktop 10 Hz use. It does not settle target-tablet suitability, fluid 20 Hz rendering or physical display latency. The native trigger request used the authenticated HA connection; no custom Python integration was added.

## Phase 0 and Phase 1 foundation verification

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

## Follow-up: telemetry session supervision

The publisher slice was committed as `110739b`. [MeterTelemetrySupervisor](METER-SUPERVISOR.md) now coordinates one sampler/publisher pair, cancels both on failure, drains actual operations before permitting restart, and creates fresh queues, windows and session IDs on each explicit run. Connection-event integration remains pending; application startup still does not enable this component.

- **10 new cases; 578 total passed, zero failed/skipped**, reported duration 522 ms. Tests cover restart isolation, pending old peaks, publisher and sampler failures, ignored cancellation, overlap rejection, disabled/empty profiles, invalid startup, and unsolicited transport cancellation.
- Initial test-first compilation failed on the missing supervisor type. A later regression test demonstrated unsolicited MQTT cancellation being swallowed as normal shutdown; it failed before the exception-classification fix and passes in the final suite.
- Release build/publish passed. The existing CS1998 settings-save warning appeared during compilation; incremental publishing reported zero warnings. EXE SHA-256: `ecf6522e2d2d393e376e0dd9461ce2a7646455270c900207c8e49a9b0dc8486a`.
- Simplification and focused correctness/adversarial review completed sequentially in the main agent. Private receipt `20261001-phase2-supervisor`: no remaining actionable findings. No independent or cross-model review claimed.

Exact commands from the clone:

```powershell
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --filter FullyQualifiedName~MeterTelemetrySupervisorTests
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --filter 'FullyQualifiedName~MeterTelemetrySupervisorTests|FullyQualifiedName~MeterTelemetryLoopTests|FullyQualifiedName~MeterTelemetryPublisherTests'
# Regression before the cancellation-classification fix (one failure).
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --filter FullyQualifiedName~Unrequested_transport_cancellation --logger 'trx;LogFileName=phase2-supervisor-red.trx' --results-directory '..\.local-phase0\test-results'
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --logger 'trx;LogFileName=phase2-supervisor.trx' --results-directory '..\.local-phase0\test-results'
$env:PATH=(Resolve-Path '..\.local-phase0\dotnet').Path+';'+$env:PATH
$env:DOTNET_ROOT=(Resolve-Path '..\.local-phase0\dotnet').Path
& .\build.ps1 -ProjectFile 'VoicemeeterMqttBridge.csproj'
git diff --check
```

Real-client cancellation/deadlines, connection-event wiring, matching installed SDK, physical channel validation, retained metadata/status/discovery, advanced capabilities, migration/rollback and licensing remain open. A send or native call ignoring cancellation still delays shutdown. HA 10/20 Hz performance is unverified. The modular HACS card, visual editor and shared subscription remain required. No live HA/SMB, installed bridge or application settings were changed; nothing was installed, launched or pushed.

Proposed next commit: `feat: publish retained v2 source metadata before streaming`, using the same session identity and fake-client ordering/failure tests before live integration.

## Follow-up: retained source metadata before streaming

Baseline `ce003d7`. The supervisor now publishes `BASE/v2/metadata` retained at QoS 1, awaits success, and checks cancellation/connectivity before sampling. It uses the same builder/session as meter frames, leaves their initial sequence at zero, and republishes updated registry labels on explicit restart. The caller now supplies `bridgeVersion`; all local callers were updated. No application startup wiring was added.

- **8 new cases; 586 total passed, zero failed/skipped**, duration 491 ms. Tests cover ordering, retention/QoS, session identity, rejection/transport failure, cooperative and ignored cancellation, disconnect, label refresh and invalid budgets. Existing shutdown/restart tests still exercise meter failures after metadata succeeds.
- Three test-first cases failed before implementation because startup sent no metadata. Final full suite passed. Release build/publish succeeded; the pre-existing CS1998 warning appeared on compilation and incremental build reported zero warnings. No standalone lint configured; `git diff --check` passed.
- EXE SHA-256: `f6fe245845c64693b9b031a59ac6308d2df5d41885b6950ee76331cea82aceaf`.
- Correctness, testing, API-contract, reliability and adversarial passes ran sequentially in the main agent, per user instructions. Private review receipt `20261001-phase2-metadata` is complete with no remaining actionable findings; no independent or cross-model review claimed. The 16 changed executable non-test lines stay below the dedicated simplification threshold.

Exact commands from the clone:

```powershell
# Before implementation: three expected failures.
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --filter FullyQualifiedName~MeterMetadataPublicationTests --logger 'trx;LogFileName=phase2-metadata-red.trx' --results-directory '..\.local-phase0\test-results'
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --filter 'FullyQualifiedName~MeterMetadataPublicationTests|FullyQualifiedName~MeterTelemetrySupervisorTests'
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --logger 'trx;LogFileName=phase2-metadata.trx' --results-directory '..\.local-phase0\test-results'
$env:PATH=(Resolve-Path '..\.local-phase0\dotnet').Path+';'+$env:PATH
$env:DOTNET_ROOT=(Resolve-Path '..\.local-phase0\dotnet').Path
& .\build.ps1 -ProjectFile 'VoicemeeterMqttBridge.csproj'
git diff --check
```

Fake-client verification does not establish broker retention/acknowledgement, HA performance, or physical audio mapping. Connection events, retained status/availability, slow discovery, installed SDK confirmation, advanced controls, migration/rollback and licensing remain open. Disabled/empty profiles leave previously retained metadata in place; consumers must not interpret metadata as current availability. The modular HACS card, visual editor and shared subscription remain required. Live HA, SMB and installed bridge were untouched; nothing was pushed or installed.

Proposed next commit: `feat: build v2 slow sensor discovery with stable source identities`, starting with deterministic discovery payloads and unavailable-state templates before live publication.

## Follow-up: slow sensor discovery payloads

Baseline `4caa814`. `SlowSensorDiscovery` now constructs retained QoS 1 discovery messages for peak/activity/clipping on each enabled source, with canonical v2 IDs, legacy device grouping and slow-only state subscriptions. It does not publish them. [Contract and limitations](SLOW-DISCOVERY.md).

- **12 new cases; 598 total passed, zero failed/skipped**, duration 537 ms. Tests cover component types, topic/QoS/retention, IDs/device grouping, labels/aliases, enable gates, expiration budget, tap selection and all 48 possible entity IDs. Initial compilation failed on the missing builder before implementation.
- **192 Jinja2 rendering assertions passed**, using six actual generated payloads. Valid levels/booleans, silence, null/missing/wrong-type values, missing/malformed sources, wrong schema/tap and undefined JSON are checked for both value and availability. The verification script rejects missing/incorrect fixture sets rather than silently passing an empty input.
- HA's read-only template evaluator could not run: all three available Web HA MCP links returned `UNAUTHORIZED` requiring reauthentication. Local Jinja rendering is not HA schema validation, entity behavior or throughput evidence.
- Release build/publish passed; existing CS1998 appeared on clean compilation, incremental publish had zero warnings. No standalone lint configured; whitespace check passed. EXE SHA-256: `39e42ce720d486081416ab0000704a199511dfbf90f357107c3dfa9cba0d28bb`.
- Reuse/quality/efficiency and correctness/testing/API-contract/security/reliability/adversarial reviews ran sequentially in the main agent. Private receipt `20261001-phase2-discovery` is complete; no unresolved actionable findings or independent-review claim.

Exact commands from the clone (Python path is this workspace's supplied runtime):

```powershell
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --filter FullyQualifiedName~SlowSensorDiscoveryTests
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --logger 'trx;LogFileName=phase2-discovery.trx' --results-directory '..\.local-phase0\test-results'
& 'C:\Users\Declan\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' -m pip install --target '..\.local-phase0\jinja-probe-deps' 'Jinja2==3.1.6'
# Local scratch console references the bridge library and exports six builder payloads.
& '..\.local-phase0\dotnet\dotnet.exe' run --project '..\.local-phase0\discovery-probe\Probe.csproj' -c Release -- 'C:\Users\Declan\Projects\VA MQTT HA\.local-phase0\discovery-probe\payloads.json'
$env:PYTHONPATH=(Resolve-Path '..\.local-phase0\jinja-probe-deps').Path
& 'C:\Users\Declan\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' '.\tests\verify_discovery_templates.py' '..\.local-phase0\discovery-probe\payloads.json'
$env:PATH=(Resolve-Path '..\.local-phase0\dotnet').Path+';'+$env:PATH
$env:DOTNET_ROOT=(Resolve-Path '..\.local-phase0\dotnet').Path
& .\build.ps1 -ProjectFile 'VoicemeeterMqttBridge.csproj'
git diff --check
```

Discovery publication/retirement, HA schema/availability/expiry behavior, reconnect/status integration, installed SDK/physical mapping, advanced controls, migration/rollback, licensing and HA benchmarks remain open. The modular HACS card, visual editor and shared subscription remain required. No live HA configuration, SMB files or installed bridge were changed; nothing was pushed.

Proposed next commit: `feat: publish v2 slow discovery during supervised startup`, with fake-client ordering/failure tests and explicit separation from legacy discovery.

## Follow-up: supervised slow discovery publication

Baseline `0be6708`. Optional `SlowDiscoveryOptions` now enables discovery on an explicit supervisor run. The entire set is validated before network writes, then metadata and each discovery config are awaited sequentially before sampling begins. Fast-only/null options skip discovery. Partial failure stops startup; explicit restart republishes the complete set. Existing application startup and legacy discovery remain unchanged.

- **9 new cases; 607 total passed, zero failed/skipped**, duration 524 ms. Coverage includes publication order, no sampling during setup, rejection/exception, cooperative and ignored cancellation, overlap prevention, invalid options before writes, fast-only gating, disconnect and restart after partial failure. Test-first compilation failed on the missing options type before implementation.
- **HA MCP authentication confirmed restored.** The initial six-scenario template check passed; an expanded batch then passed **180 exact value/availability assertions** across six generated payloads and 15 synthetic cases each. This used only HA's read-only template evaluator, with no state listeners or config writes. Results and input are saved locally under `.local-phase0/discovery-probe/ha-template-result.json` and `ha-template-request.json`. These checks verify rendering, not MQTT discovery acceptance, entity expiration, throughput or latency.
- Release build/publish succeeded. Existing CS1998 appeared during compilation; incremental build reported zero warnings. No standalone lint configured; whitespace check passed. EXE SHA-256: `b8e48e783f3ea788cf29d614beac4c6a265b1c8e8f3057169c4a0f3e3ff22fe6`.
- Reuse/quality/efficiency checks retained a shared startup-send method and bounded sequential publishing. Correctness, testing, API-contract, reliability and adversarial review completed sequentially in the main agent; private receipt `20261001-phase2-discovery-startup`, no remaining actionable findings, no independent review claimed.

Exact commands from the clone:

```powershell
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --filter FullyQualifiedName~DiscoveryStartupTests
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --filter 'FullyQualifiedName~DiscoveryStartupTests|FullyQualifiedName~MeterMetadataPublicationTests|FullyQualifiedName~MeterTelemetrySupervisorTests'
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --logger 'trx;LogFileName=phase2-discovery-startup.trx' --results-directory '..\.local-phase0\test-results'
$env:PATH=(Resolve-Path '..\.local-phase0\dotnet').Path+';'+$env:PATH
$env:DOTNET_ROOT=(Resolve-Path '..\.local-phase0\dotnet').Path
& .\build.ps1 -ProjectFile 'VoicemeeterMqttBridge.csproj'
git diff --check
```

Read-only MCP check: `web_ha_mcp.ha_eval_template`, `report_errors=true`, using the saved request's `template` and the authenticated Web HA MCP connection. Output was compared exactly with the expected 90 value/availability pairs. No discovery messages were sent to the live broker.

Retained configs may remain after partial failure; automatic deletion is not implemented. HA birth/reconnect handling, retained session status, actual discovery/expiry behavior, installed SDK/physical mapping, advanced controls, migration/rollback, licensing and HA 10/20 Hz benchmarks remain open. The modular HACS card, visual editor and shared subscription remain required. Live HA/SMB configuration and installed bridge were untouched; nothing pushed.

Proposed next commit: `feat: publish retained v2 telemetry session status`, with explicit state transitions and fake-client failure/cancellation tests before live wiring.

## Follow-up: retained session status publisher

Implemented an isolated retained QoS 1 status publisher with shared metadata/meter
session identity, explicit caller-observed lifecycle states and unknown diagnostic
fields represented as null. This slice does not yet wire status into the supervisor.

Verified 14 new cases and the full suite: **621 passed, zero failed/skipped** (547 ms).
Release build and self-contained publish succeeded. The pre-existing CS1998 warning
at Program.cs:540 appeared on compilation; incremental release build had no warnings.
EXE SHA256: `00897a704e6eac8e2018dd4cf63146a17f89f6b32b2004980204b2e5718c8761`.

Exact commands, from the repository directory:

```powershell
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --filter FullyQualifiedName~TelemetryStatusPublisherTests
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --logger 'trx;LogFileName=phase2-status-publisher.trx' --results-directory '..\.local-phase0\test-results'
$env:PATH=(Resolve-Path '..\.local-phase0\dotnet').Path+';'+$env:PATH
$env:DOTNET_ROOT=(Resolve-Path '..\.local-phase0\dotnet').Path
& .\build.ps1 -ProjectFile 'VoicemeeterMqttBridge.csproj'
git diff --check
```

Sequential inline review covered correctness, API contract, failure handling, tests,
security and simplicity. No independent reviewer was used. No blocking findings;
review receipt is outside Git in `.local-phase0/status-publisher-review.json`.

Not verified: real broker delivery, supervisor lifecycle integration, 30-second
refresh, diagnostics, native engine state, crash/LWT behavior or HA performance.
No live HA, installed bridge or remote Git changes were made. Modular card/editor
and shared subscription remain required. Proposed next commit: integrate retained
status with supervised lifecycle and test shutdown failure precedence.

## Follow-up: supervised retained status lifecycle

Optional `TelemetryStatusOptions(ShutdownTimeout)` connects status to the supervisor:
starting before metadata, running after loop startup, stopped/faulted after children
drain. Null options preserve the previous sequence. Application startup remains unwired.

**635 tests passed, zero failed/skipped** (532 ms), including 14 new cases covering
ordering, identity, failure preservation, timeouts, ignored cancellation, disconnection,
invalid budgets and the race between sampling failure and pending running status.
Release build and self-contained publish passed. Existing CS1998 at Program.cs:540
appeared during compilation; incremental release build had zero warnings.
EXE SHA256: `3a2799d92a3307ba06e8ca7b16984fe8fe92b49dbed414134c475f64e43a03f9`.

Exact commands from the repository directory:

```powershell
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --filter FullyQualifiedName~SupervisedStatusTests
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --logger 'trx;LogFileName=phase2-supervised-status.trx' --results-directory '..\.local-phase0\test-results'
$env:PATH=(Resolve-Path '..\.local-phase0\dotnet').Path+';'+$env:PATH
$env:DOTNET_ROOT=(Resolve-Path '..\.local-phase0\dotnet').Path
& .\build.ps1 -ProjectFile 'VoicemeeterMqttBridge.csproj'
git diff --check
```

The focused run passed the initial 12 cases; the full run includes two added race and
cancellation cases. Sequential inline review fixed the pending-running-send race and
found no remaining blockers. No independent reviewer. Review receipt outside Git:
`.local-phase0/supervised-status-review.json`.

Unverified: installed SDK/native integration, real broker/HA delivery and fast-stream
performance. Periodic status and measured diagnostics remain pending, as do the modular
HACS card, visual editor and shared subscription. No live HA or installed bridge changes;
nothing pushed. Next commit: periodic retained status refresh under session cancellation
and restart guarantees.

## Follow-up: periodic retained status refresh

The opted-in supervisor refreshes running status 30 seconds after the previous status
send completes. One awaited send prevents overlap/backlog. Refresh faults stop the
session; cancellation drains refresh before terminal status and restart.

Full suite: **639 passed, zero failed/skipped** (555 ms), including four new cases:
30-second boundary/session identity, transport failure, unsolicited cancellation and
an uncooperative refresh blocking terminal status/restart. Release build and publish
passed. Existing CS1998 at Program.cs:540 appeared on compilation; incremental build
reported zero warnings.

Exact commands from the repository directory:

```powershell
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --logger 'trx;LogFileName=phase2-status-refresh.trx' --results-directory '..\.local-phase0\test-results'
$env:PATH=(Resolve-Path '..\.local-phase0\dotnet').Path+';'+$env:PATH
$env:DOTNET_ROOT=(Resolve-Path '..\.local-phase0\dotnet').Path
& .\build.ps1 -ProjectFile 'VoicemeeterMqttBridge.csproj'
git diff --check
```

Sequential inline review checked task supervision, failure classification, timer disposal,
retained payload compatibility and no overlapping status sends. No independent reviewer
and no remaining blocking findings. Receipt: `.local-phase0/status-refresh-review.json`
(outside Git). No additional abstraction or queue was needed during simplicity review.

Unverified: real MQTT/HA delivery and fast-stream performance, installed native SDK and
application integration. Diagnostics remain null until measured. The modular HACS card,
visual editor and shared subscription remain in scope. Live HA and installed bridge were
untouched; nothing pushed. Proposed next commit: collect measured session publish counts
and rates for retained status diagnostics.

## Follow-up: measured publish counts and rates

Status now reports successful fast/slow MQTT completion counts, monotonic elapsed
seconds and cumulative session rates. Counters reset per publisher and freeze after
its sends finish. Failed, rejected, canceled and stale work does not count; a send
returning success during shutdown does. This is not broker/HA receipt evidence.

**646 tests passed, zero failed/skipped** (518 ms), including seven new diagnostic
cases covering stream separation, freeze/reset, failed/rejected/stale sends,
cancellation versus successful draining, and actual status JSON showing a measured
10 Hz rate instead of configured 20 Hz. Release build and publish passed. Existing
CS1998 at Program.cs:540 appeared during compilation; incremental build had no warnings.

Exact commands from the repository directory:

```powershell
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --logger 'trx;LogFileName=phase2-publish-diagnostics.trx' --results-directory '..\.local-phase0\test-results'
$env:PATH=(Resolve-Path '..\.local-phase0\dotnet').Path+';'+$env:PATH
$env:DOTNET_ROOT=(Resolve-Path '..\.local-phase0\dotnet').Path
& .\build.ps1 -ProjectFile 'VoicemeeterMqttBridge.csproj'
git diff --check
```

Sequential inline review covered thread-safe snapshots, count timing, serialization,
failure behavior, compatibility and simplicity. No blocking findings; no independent
reviewer. Receipt outside Git: `.local-phase0/publish-diagnostics-review.json`.

Unverified: native SDK/application integration, real MQTT/HA delivery and HA fast-stream
performance. Sample timing, API reads, queue/coalescing, freshness and other diagnostics
remain pending. Modular HACS card/editor/shared subscription remain required. No live
HA or installed bridge changes; nothing pushed. Proposed next commit: expose bounded
queue coalescing and stale-frame drop counters in retained diagnostics.

## Five-commit diagnostics batch: 1 — queue pressure

Added pending depth, actual DropOldest replacement count and stale-frame drop count
per stream, wired into retained status diagnostics. Deterministic replacement, dequeue,
stale discard, completed-write and fresh-instance checks pass; publisher test verifies
status diagnostics receive the stale count. Full Release suite: 647 passed, none failed
or skipped. Known existing CS1998 warning remains.

Exact command (repository directory):
```powershell
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --logger 'trx;LogFileName=batch5-1-queues.trx' --results-directory '..\.local-phase0\test-results'
git diff --check
```

Sequential inline review: counted actual channel drops rather than inferring from racy
queue depth; stale increments occur only on discard; no extra queue or per-frame logs.
No independent reviewer. Live native/broker/HA behavior remains unverified; no live
changes or push. Next in this batch: native level-read and invalid-read counts.

## Five-commit diagnostics batch: 2 — level reads

Added actual channel-read attempts and invalid/error-read counts through the sampler,
loop and retained status. Eight new cases cover zero/positive/negative/nonfinite values,
expected and unexpected exceptions, unsupported taps and fresh counters; status JSON
also verifies read counts. Full Release suite: 655 passed, none failed/skipped.

Exact command:
```powershell
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --logger 'trx;LogFileName=batch5-2-reads.trx' --results-directory '..\.local-phase0\test-results'
git diff --check
```

Inline review preserved original sampler creation/validation timing, native error
semantics and one read per channel. No independent review; installed native API and
live HA remain unverified. Next: sample-loop duration and pass counts.

## Five-commit diagnostics batch: 3 — sampling duration

Added attempted-pass count, last duration and maximum duration. Tests use a fake clock
to prove timer waiting is excluded, failed native work is included, shorter later
passes do not erase the maximum, and results freeze after shutdown. Status JSON verifies
wire fields. Full Release suite: 658 passed, none failed/skipped. Focused timing tests
also passed after adding explicit synchronization before advancing the second pass.

Exact commands:
```powershell
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --logger 'trx;LogFileName=batch5-3-timing.trx' --results-directory '..\.local-phase0\test-results'
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --filter FullyQualifiedName~SamplingTimingTests
git diff --check
```

Inline review: timing in finally preserves faults/cancellation; no native call under
timing lock; no cadence changes or extra timer. No independent reviewer. No live native
or HA benchmark. Next: measured age of successfully sent meter frames.

## Five-commit diagnostics batch: 4 — frame age at completion

Added separate fast/slow frame age at successful send completion. Snapshot age reuses
the same monotonic provenance as freshness checks. A stalled-send regression proves
50 ms of measurement plus 1000 ms in-flight yields 1050 ms, with one success and no
stale discard. Existing freshness, failed-send and status JSON tests also verify age.
Full Release suite: 659 passed, none failed/skipped.

Exact command:
```powershell
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --logger 'trx;LogFileName=batch5-4-age.trx' --results-directory '..\.local-phase0\test-results'
git diff --check
```

Inline review: shared age calculation preserves the exact freshness boundary; no UTC
comparison; no retained snapshot/history; success-only assignment; missing data stays
null. No independent review and no HA latency claim. Next: confirmed discovery publish
counts and final batch verification.

## Five-commit diagnostics batch: 5 — discovery completion and final verification

Added per-session retained discovery completion count to status: zero initially, three
for one enabled source, one after failure on its second config, and zero with slow
discovery disabled. Restart test proves the count resets with a new session identity.
No retained-topic inventory or actual HA entity count is inferred.

Full Release suite: **663 passed, zero failed/skipped** (514 ms). This five-commit batch
adds 17 cases plus stronger existing assertions. Final self-contained Release build
and publish passed. Known CS1998 at Program.cs:540 appeared during test compilation;
incremental release build reported zero warnings. No independent reviewer was used.

Exact final commands from the repository directory:
```powershell
& '..\.local-phase0\dotnet\dotnet.exe' test '.\Voicemeeter-MQTT-Bridge.sln' -c Release --no-restore --logger 'trx;LogFileName=batch5-5-discovery.trx' --results-directory '..\.local-phase0\test-results'
$env:PATH=(Resolve-Path '..\.local-phase0\dotnet').Path+';'+$env:PATH
$env:DOTNET_ROOT=(Resolve-Path '..\.local-phase0\dotnet').Path
& .\build.ps1 -ProjectFile 'VoicemeeterMqttBridge.csproj'
Get-FileHash .\bin\Release\net8.0-windows\win-x64\publish\VoicemeeterMqttBridge.exe -Algorithm SHA256
git diff --check
```

Inline batch review checked additive wire fields, independent queue observations,
read/timing counter semantics, monotonic frame age, partial discovery and per-session
reset. No blocking findings; review receipts are outside Git in
`.local-phase0/batch5-1-review.txt` through `batch5-5-review.txt`.

No live HA/SMB changes, installed bridge overwrite/launch or GitHub push occurred.
Remaining: installed SDK/native compatibility, application and reconnect wiring,
tray diagnostics, real broker/HA validation, modular HACS card with visual editor and
shared subscription, and fast-stream benchmarks. Proposed next commit: begin the
reusable one-strip/bus card with a local fixture preview, without live HA deployment.

## Card batch: 1 — reusable component and local preview

Added a dependency-free custom element, pure model and synthetic local preview under
frontend/channel-card. One instance selects exactly one strip/bus; default selects none.
Two instances of the same input use independent incoming/post-mute taps. No live HA
connection, guessed owner labels or HACS deployment. Official custom-card configuration
and sizing reference: https://developers.home-assistant.io/docs/frontend/custom-ui/custom-card/.

18 Node model tests passed. Chromium browser checks observed readings -18/-18/-32,
then -18/-90/-32 for the muted fixture; all three unavailable and stale states cleared
numeric values. At 390px, no horizontal overflow; desktop/mobile light-theme screenshots
were inspected. No page errors (initial favicon 404 only). Local screenshot evidence is
outside Git in `.local-phase0/card-preview-mobile.png` and `card-preview-desktop.png`.

Exact unit/preview commands:
```powershell
node --test frontend/channel-card/test/*.test.js
python -m http.server 8765 --bind 127.0.0.1 --directory frontend/channel-card
```
Browser: open /preview/, select muted/unavailable/stale scenarios, check the three card
values, wait for stale expiry, toggle theme and resize to 390x844 and 1280x800.

Inline review covered inert label rendering, source isolation, sequence rejection,
source/tap validation and teardown. No independent reviewer. Readings update existing
DOM; no fake motion. Real HA transport, editor, controls, HACS and performance are still
pending. Next in this batch: the visual configuration editor.

## Card batch: 2 — visual configuration editor

Added getConfigElement and manual source/topic/name/tap/floor editor. Config-changed
preserves unrelated fields; bus selection removes input-only tap. Initial/edit-time
validation reports errors. 21 Node tests pass. Browser verified literal HTML-like labels
(no injected element), bus output -32, disabled input-tap selector, wildcard-topic error
and config-element registration. Browser cache was disabled after detecting stale JS;
checks passed against the new module. Metadata suggestions/control editor remain pending.

Exact unit command: `node --test frontend/channel-card/test/*.test.js`.
Browser: expand Edit the first card; change display name and source to bus:5; enter
bad/# as topic and check error; use getConfigElement to check custom element registration.
Inline correctness/security/config-preservation review completed; no independent review.
Next: horizontal/vertical and density choices. No live HA writes or push.

## Card batch: 3 — per-instance layout choices

Added horizontal/vertical meters and compact/standard/expanded spacing, available in
the editor. 22 Node tests pass. Browser confirmed a 26x130px compact vertical track,
vertical accessibility orientation, unchanged horizontal sibling and full cover at
silence; no overflow at 390px. Inspected the vertical screenshot outside Git at
`.local-phase0/card-vertical.png`. No controls/history/transport performance claim.

Exact command: `node --test frontend/channel-card/test/*.test.js`.
Browser: first-card editor -> vertical + compact -> silence -> viewport 390x844.
Inline review verified axis/scale direction, clamping, independent settings and no DOM
replacement on readings. No independent reviewer. Next: shared subscription lifecycle.

## Card batch: 4 — shared subscription ownership

Added a weak connection-keyed, topic-keyed registry and switched fixture delivery to it.
31 Node tests pass, including setup/removal races, teardown/reacquire serialization,
failed setup and cleanup, late-generation callbacks, consumer exceptions and mutation
isolation. Browser still observes -18/-18/-32 and muted -18/-90/-32 with no page errors.
Review caught and fixed a late-event race by invalidating subscription generations.

Exact command: `node --test frontend/channel-card/test/*.test.js`.
Inline review covered resource ownership, promise errors, no cached stale replay and
one immutable frame copy per delivery. No independent review. HA reconnect semantics
and throughput remain unverified. Next: native authenticated HA subscription prototype.

## Frontend batch 5 — native HA transport prototype (2026-10-01)

Implemented a reusable authenticated-connection adapter without enabling live card
subscriptions. Added eight deterministic contract tests; the complete frontend suite
passes 39 tests, zero failures:

```powershell
node --test frontend/channel-card/test/*.test.js
```

Tests verify shared request/unsubscribe behavior, exact unwrapped MQTT event shape,
malformed/oversized payload rejection, publication-age boundaries, source/level
validation, template-topic rejection before API access, permission errors, and model
sequence/session rejection. Sequential inline review checked the subscription boundary,
consumer isolation and safe cleanup; no delegated review was performed.

Source references and limitations are in frontend/channel-card/README.md. No live
HA request, real event capture, throughput benchmark, session-metadata integration or
HACS installation was performed. C# was unchanged in this frontend batch; its earlier
663-test result is historical, not a new backend test run. Next proposed commit:
metadata-authorized session lifecycle before connecting native transport to the card.

## Frontend continuation 1/5 — session gate

Metadata must identify Potato v2, a session and unique canonical source descriptors.
Only enabled, advertised taps from the matching session pass the gate. Duplicate and
old sequence numbers are rejected; valid new metadata permits restart at sequence zero.
Retired sessions cannot roll back within a connection lifetime; after 128 retirements,
the gate fails closed until a fresh connection lifecycle. Invalid metadata revokes trust.
This is consistency validation inside a trusted MQTT namespace, not publisher authentication.
UTC `+00:00` timestamps emitted by the C# serializer are now accepted alongside `Z`.

Verification: `node --test frontend/channel-card/test/*.test.js` — 46 passed.
Seven new tests were added first (initial run failed because the gate module was absent).
Inline simplification/review checked copy ownership, bounded session history, sequence
preservation and tap isolation. No live HA or C# runtime verification was performed.

## Frontend continuation 2/5 — shared native session lifecycle

One shared metadata/fast subscription pair per existing HA connection and base topic
now authorizes frames through the session gate. Late subscribers receive metadata with
the next accepted frame, never a cached meter value. Disconnect revokes metadata trust;
HA-client reconnect must redeliver metadata before frames resume. Invalid metadata clears
readings, while retired-session replays are ignored. Partial setup retains cleanup
ownership, both unsubscribe attempts run, and failed cleanup prevents duplicate setup.

Verification: `node --test frontend/channel-card/test/*.test.js` — 51 passed.
Five tests were added before implementation; initial run failed on the absent module.
Inline review covered setup/cleanup races and failure containment. Reconnect is simulated
through the HA-client event API; no live HA delivery or performance was measured.

## Frontend continuation 3/5 — card native transport binding

Cards now consume the shared metadata-authorized session via their hass setter.
Auto/native_ws use the existing connection; entities_only never subscribes. Configuration
rejects unimplemented transports, template base topics and ambiguous trailing slashes.
Connection changes and removal release leases; late callbacks cannot update replacements.
Metadata supplies safe text labels and explicit unsupported-source/tap states. Native
publication age reduces the remaining 750-ms lifetime instead of restarting it at receipt.

Verification: `node --test frontend/channel-card/test/*.test.js` — 55 passed. Four tests
were added first (missing-module failure observed). Local Chromium mock integration:
two cards opened exactly two subscriptions total (metadata + fast); data before metadata
and old-session frames stayed blank; restart accepted sequence zero; levels remained
isolated (-18/-32 then -24/-32); HTML-like label stayed literal; removal closed both
subscriptions and removed the disconnect listener. No real HA connection was used.

## Frontend continuation 4/5 — explicit slow-sensor fallback

Auto falls back when fast data is waiting/stale; native_ws remains fast-only and
entities_only opens no stream. entities.meters.pre/post_mute/output explicitly maps each
tap to a sensor. No label-based inference or cross-tap substitution. Only finite numeric
dBFS states are shown, with a reduced-freshness label. Missing, disconnected, wrong-unit,
unknown and unavailable values stay blank. A conservative 15-second last_updated budget
expires unchanged states; this may mark a still-reporting constant value stale and does
not establish device sample age. HA state contract: https://developers.home-assistant.io/docs/dev_101_states/
and https://github.com/home-assistant/home-assistant-js-websocket/blob/master/lib/types.ts
(inspected 2026-10-01; installed client remains unverified).

The editor maps the currently selected tap. Changing source clears all entity overrides
to prevent accidental readings/control targets from the previous source. Other per-card
settings remain. Verification: `node --test frontend/channel-card/test/*.test.js` — 59
passed. Four tests added first (missing-module failure); an added test initially used
internal tap name pre instead of editor value incoming, corrected to the actual contract.
Local browser mock: -18 shown with reduced-freshness badge, expired/unavailable readings
blank, entities_only opened zero subscriptions. Inline review fixed source-map carryover.

## Frontend continuation 5/5 — visible rendering and repeatable browser checks (2026-10-02)

Cards now pause offscreen/display-hidden or when the document is hidden. They release
leases, discard fast readings and clear timers; showing them waits for metadata/fresh
frames. requestAnimationFrame coalesces updates to the latest model at a 30-fps ceiling
without an idle loop. Observer and callback generations prevent detached work from
changing replacement lifecycles. README now describes the actual integrated feature set.

Verification:

```powershell
node --test frontend/channel-card/test/*.test.js
python -m http.server 8765 --bind 127.0.0.1 --directory frontend/channel-card
```

Node: 62 passed, zero failures. Three scheduling tests were written first; absent-module
failure observed. Open http://127.0.0.1:8765/browser/checks.html at 1280x900: all six saved
mock browser scenarios pass (shared pair/source isolation, session restart, offscreen
cleanup/resume, simulated document-hidden cleanup, slow expiry, fast-to-slow fallback).
Real Chromium initially exposed an illegal-invocation error from an unbound browser RAF
function; wrapping the default calls fixed it. The successful rerun had no console errors.
Preview at 390x844: incoming -18.0, no horizontal overflow; screenshot visually inspected.

Inline simplification/review checked bounded scheduling, timer/listener cleanup, observer
replacement and browser-only behavior. No independent reviewer was dispatched, per the
user's AGENTS mapping. No frontend lint/typecheck script is configured. C# is unchanged;
its earlier 663-test result was not rerun in this frontend-only batch. Live HA/Fire-tablet
performance, real event capture, installed client reconnect and HACS install remain
unverified. Next proposed commit: capability-aware basic controls with explicit entity
bindings and real HA state readback, retaining the advanced-control requirement.

## Controls batch 1/5 — validated bindings and command/readback state (2026-10-02)

Explicit gain/mute/routing visibility and entity overrides are validated before use.
No label-based resolution. Bus strip-routing is absent. Gain requires a number entity
with dB units, finite min/max/step within the bridge's existing -60..12 dB contract;
switches require on/off state and corresponding HA services. Missing/unknown/offline
controls cannot issue commands. Distinct controls cannot share an entity accidentally.

Commands use hass.callService(number, set_value, {entity_id,value}) or explicit
switch.turn_on/turn_off. Service completion alone is not readback confirmation. Pending
commands expire after three seconds; retry stays blocked while the original service
promise is in flight. Reconfiguration/disposal ignores old completions. No retries or
live service calls occur automatically. Manual entity overrides assert source ownership;
automatic capability/entity discovery remains pending.

Contract sources: https://developers.home-assistant.io/docs/frontend/data/,
https://www.home-assistant.io/integrations/number/ and
https://www.home-assistant.io/integrations/switch/ (inspected 2026-10-02).
Verification: node --test frontend/channel-card/test/*.test.js — 68 passed. Six tests
written first; missing-module failure observed. Sequential inline review covered command
allowlisting, source isolation, bounded pending timers and late completion. Live HA
write/readback and physical audio remain unverified.

## Controls batch 2/5 — gain slider and numeric entry

The card now renders an explicitly enabled/bound gain control with HA-derived range,
step and readback. Drag/input events only show a proposed value; change/release sends
one validated command. Pending controls disable duplicate actions and keep the reported
value unchanged until HA readback. Missing/unavailable states disable input. No control
commands depend on fast-meter availability. Hidden/remounted UI discards local pending
state; an already sent HA action cannot be cancelled by removing the card.

Verification: node --test frontend/channel-card/test/*.test.js — 68 passed. New saved
browser page http://127.0.0.1:8765/browser/control-checks.html passed two gain scenarios:
command-on-release/readback and failed/unavailable handling. Initial Chromium run exposed
unbound setTimeout/clearTimeout receiver errors in the command controller; wrapped calls
fixed them and the browser rerun passed. Inline review checked range/step validation,
keyboard/numeric handling and no optimistic readback. No live HA calls were made.

## Controls batch 3/5 — mute with independent HA readback

Explicitly bound mute buttons now issue switch.turn_on/turn_off from the latest HA
state. They never infer mute from silence or a post-mute meter reading. Pending actions
retain the old pressed state; actual HA updates reconcile it, including changes from
another dashboard. Unknown/unavailable states disable the button and remove its pressed
claim. Error text is generic and no server exception payload reaches the UI.

Verification: node --test frontend/channel-card/test/*.test.js — 70 passed, including
readback-before-service-completion, explicit off target after external on, rejected action
and unknown-state rejection. The saved control browser page passes three scenarios,
including pending mute, real readback, external changes and unknown state. Sequential
inline review checked button semantics and source/entity targeting. No live action sent.

## Controls batch 4/5 — per-strip routing buttons

A collapsible, wrapping route group now displays only explicitly mapped A1–A5/B1–B3
switches. Each route has independent pending/error/readback state. Bus cards hide strip
routing and explain the incompatible setting. Route buttons retain stable DOM nodes and
accessible names; no strip destination is inferred from a bus label.

Verification: node --test frontend/channel-card/test/*.test.js — 71 passed. Added coverage
for all eight exact entity targets, independent pending locks and invalid A6 rejection.
The saved mock control browser page passes four scenarios, including A1 on/B2 off,
sibling availability while another route is pending, omitted unmapped routes and bus
routing suppression. Inline review checked explicit on/off requests and per-route state.
No live routing or audio was changed.

## Controls batch 5/5 — editor, mock preview and final control verification

The visual editor now configures gain, mute and individual routing bindings. Invalid
entity domains/duplicates are rejected; changing source OR bridge topic clears old
entity overrides. Existing solo/advanced settings are preserved with a clear unfinished
warning. The local preview has delayed in-memory gain/mute/routing readback and cannot
call real HA services. Gain/routes do not simulate audio processing.

Final review fixed Escape handling so numeric drafts restore readback before blur,
added explicit invalid-gain feedback, and allowed unitless legacy gain entities only
with explicit mapping and valid min/max/step within the bridge's -60..12 contract.
A declared non-dB unit still disables gain. Automatic entity ownership/capability
resolution is not claimed; users must select verified source-specific overrides.

Verification commands (from repository root):

```powershell
node --test frontend/channel-card/test/*.test.js
python -m http.server 8765 --bind 127.0.0.1 --directory frontend/channel-card
```

74 Node tests pass. Open /browser/control-checks.html on that local server: six scenarios
pass, including gain Escape/invalid value/release, mute readback/error, routing isolation,
real three-second timeout and editor validation/source reset. /browser/checks.html:
all six previous meter/session/fallback/visibility scenarios pass. Browser console clean
on the control run. At 390x844 the preview's mock mute call reflected on, gain remained
-6.0 dB, routing wrapped and no horizontal overflow occurred; screenshot inspected.
No lint/typecheck is configured. C# unchanged; prior backend results were not rerun.

Sequential inline simplification/review covered service boundaries, source identity,
no optimistic toggles, stable DOM, accessibility, timer disposal and safe error text.
No independent reviewer was dispatched under the user's AGENTS mapping. No live HA,
SMB, installed bridge, remote GitHub or physical audio changes were made. Live service
latency/real readback, tablet throughput and HACS installation remain unverified.
Next proposed commit: source-appropriate solo support using explicit bindings and the
same readback contract, followed by capability-driven advanced processing groups.

## Input solo control — 2026-10-02

Added opt-in strip-only solo with explicit switch binding, independent pending state
and observed HA readback. Bus cards hide solo and explain the unsupported control.
The editor saves solo mappings and clears them on source/topic changes; the preview
simulates switch readback without claiming audio isolation.

Verification: node --test frontend/channel-card/test/*.test.js — 76 passed.
Local server: python -m http.server 8765 --bind 127.0.0.1 --directory frontend/channel-card
Open /browser/control-checks.html — seven mock Chromium scenarios passed, including
solo target/readback, bus suppression and editor source reset. C# unchanged/not rerun.

Inline review checked explicit targeting, distinct entities, default-off visibility,
bus suppression, shared command lifecycle and no native API changes. No independent
reviewer dispatched, following the user AGENTS mapping.

Real-data attempt: legacy HA tool returned Unknown tool; the separate Web HA MCP
returned UNAUTHORIZED/requires reauthentication. No current live values or v2 publisher
were verified. Program.cs still has no v2 pipeline startup integration; installed SDK
verification and runtime wiring remain prerequisites to a real stream. No HA/SMB writes,
bridge launch/replacement, remote push or physical audio changes were made.
Next proposed commit: address the native SDK/runtime integration prerequisites for
a development real-data stream, keeping deployment separate and approval-gated.
Actual 10/20 Hz HA benchmarks and HACS installation remain unverified.

## Read-only live data probe — 2026-10-02

Added a separate development console tool that captures 20 synchronous passes using
existing Remote identity/level exports and the v2 mapper. No new native signatures,
settings access, audio setters, engine launch or MQTT traffic. Native identity getters
now reject failed calls instead of silently returning zero. Default application logging
is preserved; the probe supplies a no-op logger to avoid touching installed logs.

Red evidence: new probe tests initially failed compilation because Capture did not exist.
Final command (repository root):

```powershell
& '../.local-phase0/dotnet/dotnet.exe' test tests/VoicemeeterMqttBridge.Tests/VoicemeeterMqttBridge.Tests.csproj --no-restore --verbosity quiet
& '../.local-phase0/dotnet/dotnet.exe' build tools/MeterProbe/MeterProbe.csproj --verbosity quiet
& './tools/MeterProbe/bin/Debug/net8.0-windows/win-x64/MeterProbe.exe' --read-live
```

670 backend tests pass, including seven probe cases: all canonical taps, registration
with engine absent, login failure, wrong engine/failed refresh, invalid capture count,
unloaded identity and per-source read failure. Probe build passes. The first probe build
failed NETSDK1151; setting its SelfContained property to match the existing application
resolved it. No-argument usage returns 1 before native access. Known Program.cs CS1998
remains unchanged. Frontend unchanged, so its tests were not rerun.

Real native execution returned Potato 3.1.3.0, 20 frames and 480 available readings with
no unavailable values; private capture retained outside Git. Live MCP overview/template
reads verified HA 2026.9.4 and 16 available legacy meter sensors; six values changed
between two snapshots. These are raw, unitless legacy sensors, not v2 mapped dBFS.
See LIVE-DATA-PROBE.md for scope, reproducible commands and evidence limits.

Sequential review covered lifecycle cleanup, single native calling thread, no setter/
MQTT/settings path, bounded capture memory, identity errors, project compile exclusions
and source failure isolation. Simplification found no useful behavior-preserving change.
No independent reviewer used under the user's sequential AGENTS mapping. No live HA or
SMB writes, installed bridge replacement or GitHub push. Matching installed SDK documents,
controlled source mapping, actual v2 application startup and HA 10/20 Hz streaming remain
unverified. Next commit: integrate identity-gated v2 sessions with the application's
native owner and MQTT lifecycle, disabled by default, before an approved publisher run.

## Runtime batch 1/5 — owned engine metadata

Existing type/version exports now supply IVoicemeeterMetadata through RemoteApiOwner,
so identity and future registry reads share the native calling thread. Unicode labels
are explicitly unavailable; configured/generic labels remain the only fallback. No new
native export was introduced. The read-only probe reuses this identity conversion.
Verification: dotnet test tests/VoicemeeterMqttBridge.Tests/VoicemeeterMqttBridge.Tests.csproj --no-restore --filter FullyQualifiedName~RemoteApiOwnerTests --verbosity quiet
(using ../.local-phase0/dotnet/dotnet.exe): nine passed. Sequential review checked
registration guards, thread ownership, missing metadata adapters and no native label claims.

## Runtime batch 2/5 — reject changed engine maps

The sampling loop verifies owner-reported identity before and after each pass. A changed
identity faults the session before completing its window. This does not consume the
parameter dirty flag and therefore does not hide control updates from the existing poller.
Two regression cases first failed, then passed for an engine change before/during reads.
Verification: dotnet test tests/VoicemeeterMqttBridge.Tests/VoicemeeterMqttBridge.Tests.csproj --no-restore --filter 'FullyQualifiedName~EngineSessionTests|FullyQualifiedName~MeterTelemetryLoopTests' --verbosity quiet.
Sequential review: sampled windows never leave a changed-engine pass; cancellation and
queue completion remain owned by the existing loop. A same-version engine restart wholly
between checks cannot be identified by these type/version exports; no stronger claim made.

## Runtime batch 3/5 — own successive telemetry sessions

MeterTelemetryRuntime snapshots opt-in settings and starts sessions only after connection
readiness and Potato identity checks. Connection generation changes cancel/drain the old
session before a fresh metadata/session ID can start. Failures use a five-second retry;
shutdown drains real sends even when cancellation is ignored. Default flags cause no
native/MQTT work. Status logs exclude exception payloads. Initial test compile caught a
read-only ComputerName assignment; corrected fixture before passing tests.
Verification: dotnet test tests/VoicemeeterMqttBridge.Tests/VoicemeeterMqttBridge.Tests.csproj --no-restore --filter FullyQualifiedName~MeterTelemetryRuntimeTests --verbosity quiet: four passed.
Sequential review checked no session overlap, cancellation ownership, disabled defaults,
identity refusal and bounded timers/queues. Application wiring follows in the next commit.

## Runtime batch 4/5 — wire application startup and drain shutdown

BridgeService now starts the v2 runtime when explicitly enabled and routes all native
calls through RemoteApiOwner. MQTT readiness is granted only after the current connection's
setup; old setup completions cannot authorize a new generation. Manual/automatic disconnect
revokes readiness and drains telemetry before reconnect. Shutdown cancels connection retry
waits, drains session sends while MQTT is available, then disconnects and logs out. Existing
legacy paths remain active; default v2-disabled behavior is unchanged. MQTT instance logs
now honor the injected logger used by tests instead of writing to installed AppData logs.
Verification: dotnet test tests/VoicemeeterMqttBridge.Tests/VoicemeeterMqttBridge.Tests.csproj --no-restore --filter 'FullyQualifiedName~BridgeTelemetryTests|FullyQualifiedName~BridgeLifecycleTests|FullyQualifiedName~MeterTelemetryRuntimeTests' --verbosity quiet: 18 passed.
Tests exercise the actual application/publisher pipeline with fake native/broker boundaries:
metadata before frames, reconnect session replacement, disabled defaults, and an in-flight
send that refuses cancellation. Sequential review checked setup/stop races and logout order.
No installed application launch, live MQTT publish or HA write was performed.

## Runtime batch 5/5 — HA birth recovery and complete verification

HA online announcements now schedule one coalesced refresh outside the receive callback.
The old v2 session drains before metadata/optional slow discovery are republished under a
fresh session. Regression tests hold a real pipeline send open to prove the receive
callback returns, and hold old/new subscription setup to prove stale work cannot authorize
native sampling. Updated runtime documentation states configuration/restart behavior and
remaining deployment gaps; no native Unicode/advanced capability support is claimed.

Exact final commands, from repository root:

```powershell
& '../.local-phase0/dotnet/dotnet.exe' test tests/VoicemeeterMqttBridge.Tests/VoicemeeterMqttBridge.Tests.csproj --no-restore --verbosity quiet
& '../.local-phase0/dotnet/dotnet.exe' build tools/MeterProbe/MeterProbe.csproj --verbosity quiet
& './tools/MeterProbe/bin/Debug/net8.0-windows/win-x64/MeterProbe.exe' --read-live
```

682 backend tests passed. Probe build succeeded. A fresh real native capture again
reported Potato 3.1.3.0 and 480/480 available readings; private JSON remains outside Git.
Read-only HA MCP confirmed 16 legacy meter entities, none unknown/unavailable. No real
v2 MQTT publication, HA restart, broker interruption or throughput benchmark was performed.
Frontend unchanged; prior 76 Node tests were not rerun. Existing CS1998 remains unchanged.

Sequential simplification and code review covered all five runtime commits: registration,
thread ownership, identity checks, generation readiness, cancellation/drain order, birth
callback acknowledgement risk, stable legacy IDs and disabled defaults. Review caught and
corrected an inaccurate comment claiming that settings UI restarts the bridge; v2 changes
actually require an application restart. No independent agent review under the user's AGENTS
mapping. No live HA/SMB writes, installed bridge replacement, repository creation or push.

Remaining: isolated development launch configuration, real broker/native HA event capture,
controlled owner source mapping, matching installed SDK/Unicode labels, advanced controls,
HACS packaging and 10/20 Hz benchmarks. Legacy meter authority still uses top-level settings;
v2 legacyMeters fields and retained discovery retirement remain pending. Next proposed
commit: isolated development launch/config path with a dry-run topic/client summary.

## Development runner and first live MQTT delivery — 2026-10-02

Added an explicit private-config console runner, credential-free dry run, bounded
run mode and independent broker subscriber. No installed settings fallback or write.
Owner authorization now permits replacing the HA Voicemeeter test setup when needed.
This test used separate client/topic identities, discovery off and legacy meters off;
it did not need live HA file changes or installed bridge replacement. Private backups
of settings, executable, installer and HA dashboard/resources were made outside Git.
The first backup ACL command failed before copying files; corrected SID syntax succeeded.

Exact verification commands from repository root:

```powershell
& '../.local-phase0/dotnet/dotnet.exe' build tools/DevelopmentBridge/DevelopmentBridge.csproj --verbosity quiet
& '../.local-phase0/dotnet/dotnet.exe' test tests/VoicemeeterMqttBridge.Tests/VoicemeeterMqttBridge.Tests.csproj --no-restore --filter FullyQualifiedName~DevelopmentLaunchTests --verbosity quiet
$backupDir = (Get-Content -Raw '../.local-phase0/current-live-test-path.txt').Trim()
& './tools/DevelopmentBridge/bin/Debug/net8.0-windows/win-x64/DevelopmentBridge.exe' --config (Join-Path $backupDir 'development-settings.private.json') --dry-run
& './tools/DevelopmentBridge/bin/Debug/net8.0-windows/win-x64/DevelopmentBridge.exe' --config (Join-Path $backupDir 'development-settings.private.json') --run 10
& '../.local-phase0/dotnet/dotnet.exe' test tests/VoicemeeterMqttBridge.Tests/VoicemeeterMqttBridge.Tests.csproj --no-restore --verbosity quiet
```

Build succeeded; 3 targeted tests and 685 full backend tests passed. Both runner
commands exited 0. Live subscriber received 1 metadata, 121 fast and 6 slow messages,
2032 available source readings, zero unavailable and zero rejected. Requested duration
included startup. No throughput/latency claim follows from these counts. Frontend was
unchanged and its tests were not rerun. Sequential review checked explicit configuration,
secret-safe output, cancellation, separate observer subscription and sequence checks.
No independent agent review under the user's AGENTS mapping. No repository push.

Next proposed commit: feed real metadata/frames through HA into the modular card.
HA WebSocket performance, physical source mapping, installed SDK matching, advanced
controls, HACS packaging and license uncertainties remain unresolved.

## Live native HA card deployment — 2026-10-02

Added a development staging tool and HA setup/rollback instructions. Staging copies
14 browser modules plus LICENSE into a new version directory and writes SHA-256
hashes. It refuses an existing destination, protecting a running version and avoiding
mixed cached relative imports. Runtime card code did not need modification.

Live actions, under the owner's HA test-replacement authorization:

- Refreshed the private resource-registry backup before registration.
- Staged the module directory on the config/www SMB share; all 15 hashes matched.
- Registered one module resource through HA MCP.
- Created a dedicated admin-only Voicemeeter Live Test dashboard with five reusable
  card instances: strip:0 incoming and post_mute, strip:5, strip:6 and bus:0.
- The dashboard-write MCP endpoint rejected the request because its best-practice
  acknowledgment schema/guide was unavailable through that connection. The separate
  Web HA MCP connection required reauthentication. Used the normal signed-in HA UI
  for dashboard creation/configuration; HA MCP readback verified six cards including
  the explanatory markdown card. No direct .storage edits or HA restart.

Functional live result (HA Core 2026.9.4, desktop Chrome, admin account):

- Actual native MQTT-trigger delivery produced visible changing readings and metadata
  labels. Example observation: strip:0 -27.9 dBFS, strip:5 and bus:0 -10.1 dBFS,
  strip:6 -90 dBFS/silence. These observations do not establish physical source identity.
- Two copies of strip:0 displayed their configured incoming/after-mute taps. No live
  mute command was issued; physical mute semantics remain pending.
- Browser reload restored readings while the producer continued.
- Desktop and phone-width browser layouts were visually checked; private screenshots
  remain outside Git. This is not a real mobile/tablet performance test.
- After timed shutdown all five cards showed Stale data with numeric readings cleared.
- The separate MQTT observer counted 2 metadata messages (including prior retained
  metadata), 2316 fast frames, 116 slow frames, 38912 available source readings,
  zero unavailable and zero rejected. Requested runtime was 120 seconds including
  startup. These are broker-observer counts, not HA frame counts or a sustained-rate
  benchmark. Raw HA event envelopes and publish-to-render latency were not captured.

Exact commands from repository root:

```powershell
node tools/stage-channel-card.mjs '\\homeassistant\config\www\voicemeeter-v2-dev-5234de1'
node --test frontend/channel-card/test/*.test.js
$backupDir = (Get-Content -Raw '../.local-phase0/current-live-test-path.txt').Trim()
& './tools/DevelopmentBridge/bin/Debug/net8.0-windows/win-x64/DevelopmentBridge.exe' --config (Join-Path $backupDir 'development-settings.private.json') --run 120
```

76 Node tests passed. Live runner exited 0. Repeating the staging command returned
EEXIST/exit 1 as intended; deployed files were unchanged. Existing tests were kept
unchanged because this commit adds reversible file staging and documentation, not
card/runtime behavior; live rendering, hash checks and existing-destination rejection
provide the additional evidence. Backend code unchanged; prior 685 passing tests were
not rerun. No Windows bridge replacement, GitHub creation/push or control writes.

HA-02 functional delivery is now observed. UI-02 duplicate-source rendering and the
browser-reload portion of UI-12 were observed; no full acceptance claim for either.
Still pending: long 10/20 Hz benchmarks, captured event-envelope fixture, shared-feed
counts under live removal/reconnect, slow-sensor fallback, physical source naming,
advanced controls, installed SDK match, license resolution and HACS packaging.
Next proposed commit: add bounded publish/receipt/render measurement for the HA
transport so the required performance benchmark has reproducible evidence.

## Measurement batch 1/5 — bounded frontend statistics

Added fixed 1 ms histograms and a timed measurement model with separate receipt,
DOM-update, coalescing and clock-skew accounting. No live wiring in this commit.
Proof-first test failed on the absent module, then all 3 targeted tests passed:
`node --test frontend/channel-card/test/telemetry-measurement.test.js`.
Sequential correctness/adversarial/test review checked overflow rank handling, no
clock-skew laundering, monotonic timing, duplicate paints and fixed memory. Simplify
review found no worthwhile reuse/quality/efficiency changes. No independent agents
under the user AGENTS mapping. DOM timestamps explicitly do not prove visible latency.

## Measurement batch 2/5 — timestamp native HA receipt

Native callback entry now captures UTC/monotonic time before validation and fan-out.
Accepted events carry those shared timestamps and cumulative bounded rejection counters.
MQTT/HA wire contracts and subscription count are unchanged. New test first failed
because delivery had no timing, then this command passed 19 tests:
`node --test frontend/channel-card/test/native-session.test.js frontend/channel-card/test/card-feed.test.js frontend/channel-card/test/shared-telemetry.test.js`.
Sequential review covered rejected-frame handling, one timestamp/counter update per
underlying event, immutable fan-out and existing cleanup. Counters are explicitly
last-accepted snapshots since subscription setup, not measurement-window packet loss.

### Measurement controls (2026-10-02)

`node --test frontend/channel-card/test/*.test.js`: 81 passed.
Browser `/browser/checks.html`: PASS (7 scenarios), including manual stop,
10-second automatic completion and hidden-card cleanup. The new browser case
failed first with missing measurement controls. An existing fixture visibility
race was corrected by waiting for both cards before delivering a one-shot frame.
A fresh preview origin was needed to avoid cached pre-change JavaScript modules.

### Broker observation metrics (2026-10-02)

Red first: `dotnet test ... --filter FullyQualifiedName~DevelopmentTimingTests`
failed because TimingSnapshot and the injected clock did not exist.
`../.local-phase0/dotnet/dotnet.exe test tests/VoicemeeterMqttBridge.Tests --verbosity quiet`:
687 passed (existing Program.cs CS1998 warning).
`../.local-phase0/dotnet/dotnet.exe build tools/DevelopmentBridge/DevelopmentBridge.csproj --verbosity quiet`:
succeeded with zero warnings/errors. Deterministic clock checks cover idle periods,
rejected duplicates, payload byte totals, empty streams and zero-duration rates.
