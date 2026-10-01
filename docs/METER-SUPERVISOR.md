# Telemetry session shutdown and restart

Optional `TelemetryStatusOptions(ShutdownTimeout)` enables retained lifecycle status.
The supervisor sends starting before metadata, running after the sampling loop starts,
then stopped or faulted after child work drains. The independent terminal-send timeout
requests cancellation without abandoning the real send or releasing the restart gate.
Terminal failure remains visible, alongside any original failure. A disconnected
client skips terminal status; availability/LWT is still needed. See
[the status contract](TELEMETRY-STATUS.md) for ordering and remaining diagnostics work.
After initial running status, a supervised task refreshes it with a 30-second delay
between completed sends. Failure cancels the session; shutdown drains this task before
terminal status, including any send that ignores cancellation. No catch-up queue exists.

Optional `SlowDiscoveryOptions(Computer, Prefix, ExpireAfterSeconds)` enables v2 slow discovery during each explicit run. All configs are constructed before network writes, then published sequentially after metadata and before sampling. Null options skip discovery; a disabled slow stream also skips it. Partial failure may leave retained configs at the broker; restart republishes the complete current set under stable IDs. Retiring old configs and reacting to HA birth events remain separate pending work.

`MeterTelemetrySupervisor` runs one sampling/publishing pair on an injected, already-connected MQTT client and level adapter. It remains outside application startup. The connection owner explicitly calls `RunAsync` with a registry, settings, base topic, bridge version, age budgets and cancellation token for that connection. There is no automatic reconnect loop or live connection-event subscription in this component.

After validating the publication configuration, it publishes retained QoS 1 source metadata and awaits success before creating the sampling timer. The metadata carries the same session ID as subsequent frames and does not consume their sequence numbers. Rejection, transport failure, cancellation or disconnection during this step prevents sampling. The metadata send is awaited even if it ignores cancellation; restart stays blocked until it ends. Settings must remain unchanged throughout startup, including this acknowledgement wait. Explicit restart with a rebuilt registry republishes updated labels and mappings; no live label watcher is installed.

Each accepted run creates fresh accumulators, queues, frame builder/session ID and publisher. A concurrent run is rejected while the previous run is active **or still shutting down**. Once both child tasks and optional terminal status have ended, another explicit run is allowed, including after a failed run. The host supplies a freshly read registry when labels/configuration change. Settings must not be mutated during startup; the child components capture their configuration before awaiting work.

If either child ends, the supervisor cancels and awaits both. Publishing faults additionally request session cancellation immediately, before waiting for any sibling send to finish, so the sampler stops even if another send ignores cancellation. A transport cancellation without a session cancellation request is treated as a failure; it must not be mistaken for a successful stop. Ordinary caller cancellation ends normally. Other failures propagate after cleanup so the host can report them and decide when to retry.

Shutdown does not flush pending snapshots into a new session. A restarted run begins with empty queues, independent peak/hold state, a new session ID and sequence zero. The original MQTT client and native owner are not disconnected or disposed by the supervisor. The host must cancel on connection loss or engine/configuration changes, await this run, then reconnect/rebuild and start explicitly. It must not start another supervisor instance against the same resources to bypass the restart gate.

The supervisor cannot abort a synchronous native call or a client send that ignores cancellation. In that case shutdown and the restart gate stay pending until the actual operation ends. This avoids overlapping sessions; it does not prove bounded shutdown time. Real-client transport deadlines, connection-event wiring, native call timing and error/status reporting still require verification before deployment.

Global v2 disable, both streams disabled, or an empty enabled source profile do no sampling or publishing. Disconnected clients and invalid publication budgets are rejected before the sampling timer starts. None of these paths enables v2 in the running legacy bridge.

Tests compose the real sampler, accumulators, queues, serializer, publisher and supervisor with fake levels, timer and MQTT boundary. They cover metadata ordering/retention/session identity, label refresh, restart isolation, failure cleanup, ignored send cancellation, cancellation classification and rejection of overlap. They are not physical audio, real broker or HA performance evidence. Live status/discovery verification and the required modular HACS card, visual editor and shared subscription remain outstanding.
