# MQTT v2 contract foundation

Status: development library and deterministic tests, **not an enabled bridge feed**. `AggregateFrameBuilder` constructs JSON from completed `MeterWindowSnapshot` values. The isolated supervisor can publish through an injected client, but application startup does not create it. The existing executable version remains 1.0.1.

## Frame contract

The isolated publisher sends one aggregate to the configured `BASE/v2/meters/fast`, non-retained, QoS 0. Scheduling and transport have deterministic fake-boundary tests; live delivery remains unverified. Legacy `/meters`, command topics, availability and discovery IDs keep their existing behavior.

| Field | Meaning |
|---|---|
| `schema` | Integer 2. |
| `session_id` | Fresh opaque GUID for each builder lifetime; also present in metadata. Consumers reset sequence tracking when the session changes. |
| `seq` | Starts at zero and increases on each successfully serialized frame, independently of wall-clock changes. Never silently wraps. |
| `published_at_utc` | Caller-supplied timestamp normalized to UTC. This alone does not measure latency. |
| `sample_window_ms` | Positive configured aggregation window supplied by the caller, as required by the blueprint. The accumulator's actual elapsed `Duration` stays internal. |
| `sources` | Object containing exactly one entry for every registry-enabled canonical source. Disabled sources are absent. |

Strip entries contain only their configured `pre_dbfs`, `post_fader_dbfs` and/or `post_mute_dbfs`; buses contain only `output_dbfs`. Missing or failed readings make the entire source unavailable for that frame: `available=false`, all its configured level fields and its `active`/`clipping` fields are **null**. Unconfigured tap fields are absent. A valid zero-amplitude source stays available at the configured floor, normally -90 dBFS.

The accumulator takes repeated observations and supplies one maximum linear peak per enabled source/tap. The serializer converts it using `20*log10`, independently of any supplied cached dB value. Any failed observation invalidates that source for the window. Missing/invalid readings or missing flags make the whole source unavailable; no previous level is reused. Duplicate entries, nonpositive duration and readings outside the registry's enabled sources/taps are rejected before sequence advances. Non-finite or negative amplitudes never become JSON numbers.

`active` and `clipping` now preserve the [timed accumulator's](METER-WINDOWS.md) hysteresis and holds. Their tap is identified by metadata `activity_tap`: pre-fader when selected, otherwise the first selected input tap, or output for buses. Flags are not recomputed from the interval maximum: a quiet current window can retain a recent clip warning, and a window with a loud peak can end inactive after observed silence. These are sampled indicators, not complete clip detectors. The frame contains no generic peak-hold field. Cards must maintain tap-specific history and hold.

## Latest-snapshot queue

`LatestMeterSnapshotQueue` keeps at most one pending snapshot. A successful nonblocking `TryWrite` replaces a superseded pending snapshot; it does not mean MQTT delivery. Multiple producers are supported, with one reader per queue. A consumer may separately hold one snapshot already taken for delivery. Use separate queues for fast/slow streams. Commands, discovery and retained state must not use this lossy queue.

The queue stores snapshots, not pre-serialized JSON. The consumer reads the latest snapshot and then calls `BuildFastFrame` with its publication timestamp. Dropped snapshots do not consume sequence numbers. The builder itself remains single-caller; concurrent stream consumers must not share it without an owning serialization loop.

`Complete` rejects later writes, lets the final pending snapshot drain, then ends readers with `ChannelClosedException`. Cancelling one read leaves the queue usable. No broker, native API or background publisher starts here. The composition test validates supplied timestamps, not real delivery time or latency.

`ReadFreshAsync(maximumAge, cancellationToken)` discards expired snapshots and waits for fresh data. It measures age conservatively from the **start of the measurement window**, using the accumulator's original monotonic clock. Enqueue, dequeue and publication timestamps cannot reset that age. At exactly the positive age budget the snapshot expires. A snapshot constructed without capture provenance is ineligible. The original low-level `ReadAsync` does no age filtering and is not the publishing entry point.

Choose a separate age budget per cadence, greater than the intended measurement window: a 1000 ms slow window cannot pass a 750 ms budget. No production default or performance claim is established here. The consumer must recheck `snapshot.IsFresh(maximumAge)` after any wait before handing data to the transport; data can expire after dequeue. Freshness describes measurement age, not source availability: a fresh unavailable frame still carries null readings and can report an outage. The timestamp stays internal and does not change v2 JSON.

Session/connection transitions still require runtime policy. A reconnect must cancel the old reader and replace its queue and accumulators; an age check alone does not reject a recent snapshot from a previous connection. Already-started network sends, missing polls within a window, source-specific deadlines and delivery latency remain outside this building block.

## Slow frame and publishing

`BuildSlowFrame` uses the same source levels, availability and timed flags as the fast frame, with `window_ms` in place of `sample_window_ms`. It adds `sensor_tap` to each source: pre-fader when configured, otherwise the first configured input tap, or output for a bus. This explicitly defines which level a conventional sensor should consume. Unavailability does not remove that tap declaration. Discovery and sensor templates remain pending.

[MeterTelemetryPublisher](METER-PUBLISHER.md) sends fresh fast/slow snapshots as non-retained QoS 0 messages through an injected connected client, with bounded pending/in-flight work and shared-session serialization. [MeterTelemetrySupervisor](METER-SUPERVISOR.md) coordinates its shutdown with sampling and permits a fresh session only after old work ends. Both remain outside application startup; connection-event wiring, metadata/discovery publication and live verification remain pending.

## Metadata contract

`BuildMetadata(bridgeVersion)` includes `schema`, `session_id`, caller-supplied `bridge_version`, `engine`, `engine_version` and all 16 canonical source descriptors. The supervisor publishes it to `BASE/v2/metadata`, retained, QoS 1, before starting sampling. It awaits a successful publish result and rechecks cancellation/connectivity before allowing meter frames. Metadata and both meter streams share one builder/session identity; metadata does not consume a meter sequence number. Rebuilding the registry after a configuration/label change requires an explicit new supervisor run, which refreshes retained metadata with the new session.

Metadata failure stops startup and propagates after child cleanup; no automatic retry or QoS downgrade is performed. A pending metadata send occupies the same restart gate as telemetry shutdown. A disabled or empty profile does not publish or clear retained metadata. Metadata describes sources, not current availability: consumers must also use session identity and the eventual availability/status contract. Connection-event wiring and real broker acknowledgement/recovery remain unverified.

Each descriptor contains `id`, `kind`, `index`, `channels`, `engine_label`, effective `label`, optional `alias`, `enabled`, `taps`, `activity_tap` and `capability_groups`. Effective label precedence is manual display override, then a nonblank engine label, then a generic hardware/virtual/bus name. Aliases are optional and case-insensitively unique; labels never determine canonical IDs. No HA `entity_id` is guessed.

The registry accepts a reported Potato type 3, version 3.x at least 3.1.0.1, matching the public reference baseline. This is a compatibility gate on the **supplied identity**, not proof of the installed engine. `IVoicemeeterMetadata` has only a test implementation until the matching installed SDK prerequisite is resolved. Label-read failures fall back to generic names with a null engine label. All capability groups remain empty until version-qualified runtime probes exist; the legacy virtual Comp/Gate entities are not evidence of support.

## Slow discovery payloads

[SlowSensorDiscovery](SLOW-DISCOVERY.md) builds three new discovery payloads per enabled source: peak dBFS, activity and clipping. They use only the slow stream, stable canonical IDs, the existing device identity, source-specific availability and caller-selected expiration. Templates are rendered locally and through HA's read-only evaluator against synthetic examples. Explicit supervisor discovery options publish the configs before sampling; live integration, retained-config retirement and actual HA entity validation remain pending.

## Additive settings

Deserializing v1 settings adds `meteringV2` in memory; it does not write the file. The existing save path serializes it on the next explicit save. All existing settings and unknown root/v2/source properties survive round trips. Settings UI save continues to update the existing settings instance, preserving the v2 object and extension fields.

Defaults: v2 disabled, fast disabled, slow enabled, 50 ms sampling/fast windows, 1000 ms slow windows, -90 dBFS floor, -48 dBFS activity, -0.1 dBFS clip and 5 seconds history. The source list is empty. On v1 migration, legacy-meter enable/cadence defaults inherit `publishMeters` and `publishMetersEveryMs`; no owner source assignment is invented. A null v2 object is treated as absent. Repeated serialization is idempotent.

Validation before registry/frame construction enforces canonical IDs, unique aliases, compatible distinct taps, finite ordered thresholds and interval bounds. Sampling: 10–1000 ms; fast: 50–5000 ms and at least the sampling interval; slow: 250–60000 ms and at least the sampling interval. History: 1–60 seconds. These are configuration limits, **not measured performance guarantees**. Aliases allow up to 64 ASCII letters, digits, underscores or hyphens. Manual labels allow Unicode, up to 511 UTF-16 code units, without control characters.

The isolated [MeterTelemetryLoop](METER-SCHEDULER.md) honors v2 enable/cadence fields when explicitly run, but is not instantiated by the application. Existing `publishMeters` and `publishMetersEveryMs` remain authoritative for the running legacy publisher. Application integration must reconcile compatibility settings and own session transitions before enabling the new schedules. Advanced settings UI, file-migration/rollback tests and deployment backups remain pending.

## Remaining requirements

Native metadata API binding and feature probes; confirmed owner assignments; application integration and connection supervision of sampling/publishing; slow sensor discovery; native HA WebSocket prototype and 10/20 Hz benchmarks; the reusable **one-strip-or-bus HACS card**, its visual editor, shared subscription and slow fallback. Changed-only legacy control publication and the native-call owner are implemented in separate slices. No fixed dashboard replaces the modular card.
