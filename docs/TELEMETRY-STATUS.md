# Retained session status foundation

`TelemetryStatusPublisher` explicitly publishes one caller-observed session state to
`BASE/v2/status`, retained at QoS 1. The supervisor can opt in with
`TelemetryStatusOptions(ShutdownTimeout)`; application startup remains unwired.
The caller supplies `Starting`, `Running`, `Stopped` or `Faulted`; this class does not
enforce lifecycle transitions or infer native engine state from a sampling task.

Payload fields: `schema: 2`, the frame builder's `session_id`, `bridge_version`,
`published_at_utc`, lowercase `session_state`, and `broker_connected_at_send: true`.
`engine_state` stays null until native health is measured. With supervisor wiring,
`diagnostics` contains `elapsed_seconds`, `fast_publish_count` and `slow_publish_count`.
`actual_fast_hz` and `actual_slow_hz` are those counts divided by elapsed seconds;
they are null at zero elapsed time. A standalone status publisher without a diagnostics
provider still emits null rates/diagnostics. Schema is the data format version. Status serialization
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

Publish counts include only MQTT client calls returning success, separately for fast
and slow meter topics. Metadata, discovery and status do not count. Stale frames,
failed/rejected sends and canceled calls do not count. A send returning success after
shutdown was requested does count: it actually completed. QoS 0 completion is not a
broker acknowledgement or proof of HA receipt/rendering.

Rates are cumulative session averages, not configured rates or rolling 30-second rates.
Elapsed time uses the monotonic clock from publisher construction through completion
of its child sends, including startup waits and draining. Counts and elapsed time are
snapshotted under one lock and freeze when publishing finishes, so terminal-status
delays cannot dilute the result. New sessions create fresh counters. Disabled streams
have zero successful sends and therefore zero rate once elapsed time is positive.

Still required by the blueprint: application lifecycle integration;
remaining requested diagnostics (sample/read timing, coalescing, queue depth, invalid
reads, freshness, reconnects and discovery counts); native engine health; crash and
reconnect behavior; real broker/HA verification. The modular HACS card, visual editor,
shared subscription and HA fast-stream benchmarks remain in scope.

Queue diagnostics are available as `diagnostics.fast_queue` and `slow_queue`:
`depth` counts pending frames (0 or 1, excluding an in-flight send),
`coalesced_count` counts actual DropOldest replacements, and `stale_drop_count`
counts expired/untracked frames discarded by freshness checks, including checks
after dequeue/serialization. Failed writes to completed queues do not count as drops.
Fields are individually thread-safe observations, not an atomic cross-queue snapshot.
Counters reset with each new session queue; they never describe command/discovery traffic.

Sampling diagnostics add `diagnostics.sampling.api_read_count` (attempted GetLevel
calls) and `invalid_read_count` (nonfinite/negative values or thrown read errors).
Zero and values above 1 are valid. Expected Remote errors still mark the source
unavailable; unexpected errors still propagate. Counts are independently thread-safe,
reset with the sampler, and remain available after shutdown. These count adapter calls,
not proof of installed SDK compatibility; no extra native calls are introduced.
