# Retained session status foundation

`TelemetryStatusPublisher` explicitly publishes one caller-observed session state to
`BASE/v2/status`, retained at QoS 1. The supervisor can opt in with
`TelemetryStatusOptions(ShutdownTimeout)`; application startup remains unwired.
The caller supplies `Starting`, `Running`, `Stopped` or `Faulted`; this class does not
enforce lifecycle transitions or infer native engine state from a sampling task.

Payload fields: `schema: 2`, the frame builder's `session_id`, `bridge_version`,
`published_at_utc`, lowercase `session_state`, and `broker_connected_at_send: true`.
`engine_state`, `actual_fast_hz`, `actual_slow_hz` and `diagnostics` are explicitly null
until measured inputs exist. Schema is the data format version. Status serialization
does not advance the meter sequence. Broker connectivity is a pre-send observation,
not a durable health guarantee or proof that HA received the message.

Only one actual send may run per publisher. Overlap is rejected, including after
cancellation while a client ignores its token. Rejection, transport failure,
cancellation and a disconnected completion remain observable to the caller; no retry
or background send is created. A failed or canceled call may already have reached the
broker. The owner must therefore treat delivery as uncertain, not roll it back locally.
Exception text and credentials are never included in status payloads.

The owner must share one instance per session, await it before replacement, supply
correct transitions, and arrange graceful terminal publication with its own shutdown
budget. This component cannot announce a disconnected/crashed process. Retained status
must not replace availability/LWT or consumer freshness checks.

The supervisor validates a positive shutdown timeout of at most one minute before any
send. With status enabled, ordering is starting, metadata, optional discovery,
sampling-loop start, running, then stopped/faulted after all child work drains.
Running means the loop started, not that the native engine is healthy or any frame
reached HA. A sampler/publisher failure cancels a pending running-status send.

Terminal status uses an independent cancellation budget so ordinary caller cancellation
can still announce stopped. It is skipped when disconnected. A client ignoring this
budget keeps the restart gate occupied until its actual send ends. A terminal send
failure is surfaced even after normal cancellation; when another failure already
exists, an AggregateException preserves the original first and the terminal failure
second. Disabled, empty and pre-canceled sessions do not publish status. Null options
preserve the previous supervisor publication sequence.

After the initial running status succeeds, the supervisor waits 30 seconds before
refreshing retained running status. Each subsequent delay starts after the preceding
send completes: slow sends do not overlap or build a catch-up backlog. This task shares
session cancellation and is drained before terminal status and restart. A refresh
failure stops the session; unrequested transport cancellation is treated as failure.
Null status options create no refresh timer. Refresh is a session liveness observation,
not proof of native health or a replacement for availability/LWT.

Still required by the blueprint: application lifecycle integration;
measured rates and all requested diagnostics; native engine health; crash and
reconnect behavior; real broker/HA verification. The modular HACS card, visual editor,
shared subscription and HA fast-stream benchmarks remain in scope.
