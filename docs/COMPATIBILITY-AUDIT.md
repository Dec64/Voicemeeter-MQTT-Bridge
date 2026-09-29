# Phase 0 compatibility audit

Inspected 2026-09-29. Scope: blueprint Phase 0 and deterministic Phase 1 mapping tests only. The original blueprint remains the product scope authority.

## Pinned baseline and environment

- Upstream: https://github.com/thegeekoftheworld/Voicemeeter-MQTT-Bridge
- GitHub API and fresh Git fetch agree on `main` SHA `0602793c73313b0e6c3705eb8ecb00d03ebeab75` (2026-05-18). This matches the planning baseline.
- Local branch: `codex/phase0-mapping-tests`; no remote repository, fork, push or deployment created.
- Upstream: .NET 8 Windows/WinForms, MQTTnet `4.3.7.1207`, no existing automated test project. Initial checkout clean.
- Windows build 26200, x64. System SDK 7.0.302 cannot target .NET 8 (`NETSDK1045`). A separate scratch-directory SDK 8.0.425 was installed for verification, without changing system PATH.
- Installed executable file versions: Potato **3.1.3.0**, Remote64 DLL **1.1.3.0**, bridge **1.0.1.0** (product version identifies upstream parent `3ef44533e06f7d6556ff8587e54724ab45a0c82b`). These are file metadata, not a live Remote API handshake.
- Read-only SMB and live Home Assistant Connection MCP agree on HA Core **2026.9.4**, HACS **2.0.5**. The working MCP also reports HA OS **18.2**, Supervisor **2026.09.3** and Mosquitto addon **7.1.1**; the MQTT integration is loaded. The two separate Web HA MCP links still return reauthentication errors. Current entity states are verified below; authenticated frontend streaming remains unverified.

## Compatibility findings

| Area | Observed baseline | Consequence / required handling |
|---|---|---|
| Meters | `BridgeService.PublishMetersAsync` scans raw mode-0/mode-3 channels; discovery uses `in_0..7` and `out_0..7` as logical sources | Input 0/1 are the L/R channels of strip 0; all eight advertised output sensors are A1 channels. Preserve this legacy raw payload and IDs; new combined meters must use additive v2 topics/IDs. |
| Read failures | First exception breaks the whole legacy channel loop; discovery templates use `default(0)` | Later channels disappear and errors can look like silence. New mapping tests must prove per-source failure isolation; do not silently redefine legacy semantics. |
| Controls | Comp/Gate generated for every strip, including virtual 5–7; no capability probing | Six advertised controls lack established virtual support. Advanced compressor/gate/denoiser, virtual EQ and physical EQ need a version-qualified registry and probes before discovery. |
| Dirty/error | `dirty != 0` republishes every mapped control, including negative error returns | Distinguish errors from changes; cache changed values in later Phase 1 work. `publishAllMappedControls` is stored but never read by the publisher. |
| Login/lifecycle | Any nonzero login result may launch Potato and call Login again; loaded DLL is used as health signal | Vendor documents login 1 as successful registration with engine absent and one login/logout per client lifetime. Restart/recovery and poll-loop shutdown ownership need later correction. |
| Commands | Generic `/set` accepts arbitrary float parameter paths; set return code only logged | Preserve legacy behavior initially, document broker trust boundary; validate new commands and report failures before adding controls. |
| Scheduling | MQTT publication is awaited within the parameter/meter polling loop | No evidence for 20 Hz, bounded queues or latency. Native HA `subscribe_trigger` prototype and benchmark remain mandatory before accepting transport. |
| SDK threading | Official header marks dirty polling and GetLevel as single-thread callers | Future sampler/executor must respect this; a faster independent loop cannot be bolted onto existing concurrent calls without an ownership design. |
| Installer | Explicit file list; only demo settings installed; AppData separate | Preserve. Build scripts do not check every native command exit code; fallback demo has obsolete property names. Packaging/upgrade execution remains unverified. |

## Live compatibility inventory (read-only)

Entity registry contains **155** matching MQTT entities: **32 number**, **107 switch**, **16 sensor**; none registry-disabled. Live MCP registry reads confirm all 155 entity IDs and unique IDs match the SMB inventory, all use MQTT and belong to one device. Office Hub directly references **139** of these entity IDs. Exact IDs are in private scratch inventory outside Git; no private dashboard contents are copied into this repository.

Read-only live snapshots at **2026-09-29 17:38:53 UTC** and **17:41:53 UTC** contain all 155 entities, with no `unknown` or `unavailable` states. Four input meter values changed between snapshots; all 16 legacy sensors have no unit of measurement. This verifies HA availability and some changing telemetry, not correct physical-source assignment, command feedback, control capability or stream cadence. Unchanged state timestamps must not be treated as proof of a stopped publisher. No commands, service actions or configuration writes were sent.

Legacy identity contract: `voicemeeter_<computer>_<control_id>`, meters `meter_in_0..7` / `meter_out_0..7`, device identifier `voicemeeter_mqtt_bridge_<computer>`. Retain `/set`, `/parameter/{id}/set`, `/parameter/{id}/state`, `/availability`, and raw `/meters`. Never infer renamed HA entity IDs from discovery IDs.

Live settings use `voicemeeter/{computer}`, 250 ms polling and 1000 ms meters. Credentials exist but were not printed or copied. Guest/External hardware indices and System/Chat/Music virtual ordering remain **unconfirmed**; no owner profile is created.

## Settings migration and rollback

Current load order: existing AppData settings, then legacy executable-adjacent settings, then demo, then defaults. Import saves to AppData; later saves serialize only known properties. Unknown future fields will be lost if v1 saves a v2 config. Settings-form reconstruction must also preserve any future v2 sub-object.

No migration is performed in this slice. Before an approved upgrade, back up AppData JSON, old installer/executable and relevant HA config **outside Git**, with access restricted to the owner. The old 1.0.1 installer was located; no backup is claimed yet because no migration/deployment is happening.

| Scenario | Required future v2 result |
|---|---|
| v1.0.1 AppData | Preserve credentials, topic expansion and every existing flag; additive `meteringV2.enabled=false`, `fastEnabled=false`, empty source profile. |
| First run | Safe defaults; no guessed Guest/System assignments or automatic entity flood. |
| v1.0.0 adjacent config | Preserve existing one-time import precedence; apply v2 defaults only after import. |
| Rollback | Restore old executable **and** pre-upgrade JSON backup; avoid saving v2 configuration with v1. |

Current tests will characterize serialization/defaults without calling live `Load()`/`Save()`. File migration, UI migration and installer rollback remain later tests.

## License and vendor-reference uncertainty

`LICENSE`'s project grant, `NOTICE.txt`, `COPYRIGHT.txt`, README and Program.cs say GPL version 3 **or later**; `COPYING` supplies GPLv3 text. The csproj alone says `GPL-3.0-only`. Preserve every notice and record this inconsistency; align package metadata with the governing grant only after owner/licensor clarification before release. This audit is not legal clearance. No vendor DLL, SDK source or artwork is added.

No installed header/manual was found in the Voicemeeter install, Downloads, Documents or Projects search. Official SDK checkout `02a1abf15358ddb33e588cd31c43576a065f81ac` and the public December 2023 API 3.1.0.1 manual were read instead. This is explicitly **not** verification of a matching installed SDK. No new native API binding or hardware call is implemented in this slice. See [SDK-REFERENCE.md](SDK-REFERENCE.md).

## Scope still required after this slice

One reusable HACS card for exactly one canonical strip **or** bus, duplicated freely, with a visual editor, capability-aware controls, per-card tap/history and one shared subscription per connection/topic/tab remains mandatory. It is not replaced by a fixed five-strip dashboard. Native authenticated HA streaming must be prototyped and benchmarked at 10/20 Hz; custom backend remains conditional. No throughput or latency target has been demonstrated.
