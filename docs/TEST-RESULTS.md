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
