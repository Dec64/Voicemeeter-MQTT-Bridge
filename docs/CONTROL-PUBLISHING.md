# Legacy control publication contract

Implemented locally on 2026-09-30, after foundation commit `2494324`. This changes the existing control path; it does not enable the v2 telemetry publisher. `BridgeService` now lives in `src/BridgeService.cs`, retaining its namespace and public entry points.

## Changed state and full snapshots

- A positive `IsParametersDirty()` result scans the configured legacy controls. Numbers publish when their difference from the last successfully sent value is at least half their registry step (0.05 for the existing 0.1-step controls). Smaller changes accumulate against that sent value, rather than moving the comparison baseline on every read.
- Switches compare their existing wire projection: `abs(value) > 0.5` is `ON`, otherwise `OFF`. For example, bus mono values 1 and 2 still share the legacy boolean representation; this does not introduce stereo-reverse discovery.
- A negative dirty result never starts a control scan. `-2` reports `Engine unavailable`; other negatives report the code as a Remote API error. Exceptions report a poll error. An unloaded adapter now throws instead of returning a false healthy zero. Repeated identical statuses are logged once. The next successful poll forces a snapshot, even if dirty is zero.
- Each new MQTT connection calls the existing post-connect setup, which forces a complete state snapshot. `PublishAllStateAsync()` is also the explicit full-resync entry point. Command readback and `PublishParameterStateAsync()` force the requested control. No new MQTT resync route is introduced.
- Manual reconnect no longer performs a second discovery/state sweep after the connection callback schedules one. Discovery follows the existing `homeAssistantDiscovery` and `publishDiscoveryOnConnect` flags.

All 139 legacy control topics, retained values, QoS 0 delivery, and all 155 discovery identities remain. The cache advances only after MQTTnet reports success. That means **local send completion**, not broker acknowledgement or HA receipt. An offline client, publish exception or unsuccessful result leaves the control eligible to retry.

## Reconciliation and failures

The additive root setting `controlReconcileIntervalMs` defaults to **30000**. Positive values are clamped at use to 1000–3600000 ms; nonpositive values disable the periodic scan. Timing uses a monotonic clock. A due reconciliation scan reads controls but sends only changed or previously failed values. It retries reads and sends without requiring another dirty event, using current readings rather than replaying queued stale values. With reconciliation disabled, retries depend on a positive dirty event or a forced snapshot. The settings form preserves this field; no new UI field is added.

A failed/non-finite read publishes no replacement state and invalidates only that control's cache. Other controls continue. Recovery republishes the value even when unchanged from before the failure. `ControlReadErrorCount` and transition logs report read failures separately from the MQTT status and engine dirty status. Existing retained HA state is not deleted or replaced with zero during failure; per-control HA availability remains future work.

One semaphore serializes control scans, writes and readback through their publication. This prevents an older scan from overwriting command readback on MQTT. It does **not** provide a dedicated native calling thread or decouple metering from network waits. Those runtime requirements remain pending.

## Commands and trust boundary

All command paths reject NaN and infinity before a native write, including numeric switch payloads. A nonzero set result or thrown set error records `LastCommandError`/a log and skips readback publication. After a successful mapped write, the bridge reads the engine and publishes that returned value; it does not substitute the requested value. Failed readback is reported as a control-read failure. There is no new success/error response topic in this legacy slice.

The generic `BASE/set` endpoint still accepts arbitrary finite float parameter names. Existing finite values, including `Bus[0].Mono = 2`, are preserved. MQTT publishers with access to command topics remain trusted to control the engine. No new advanced controls, string parameters or unvalidated endpoints were added. Bounds and capability validation for **new** numeric controls and their structured result channel remain required before those controls are exposed; the full CTRL-03 requirement is not marked complete.

## Verification limits

The tests use the real bridge logic and MQTT connection callback with fake native and transport boundaries. They never load the installed DLL, start the application, connect to a broker or modify Home Assistant. The matching installed SDK/manual remains unavailable; existing negative result meanings were checked against the pinned official header in [SDK-REFERENCE.md](SDK-REFERENCE.md). No new native binding was added. Exact commands and results are in [TEST-RESULTS.md](TEST-RESULTS.md).

Remaining requirements include native API ownership/login lifecycle, capability and Unicode label probes, independent metering/transport schedules, bounded discovery, timed windows/hysteresis, file migration/rollback, and the reusable one-strip-or-bus HACS card with visual editor and shared subscription. Native HA streaming still needs its prescribed 10/20 Hz benchmark; this control-cache test run is not that benchmark.
