# Telemetry measurement contract

Measurement is opt-in and bounded to at most 30 minutes. It must not change MQTT
schemas, add fast HA entities, send audio controls or retain audio samples.

The frontend reports four distributions:

- Publish-to-receipt: browser UTC minus the payload's published_at_utc. This depends
  on clock synchronisation and includes the bridge/network/broker/HA path.
- Receipt spacing: monotonic interval between accepted frames in this card.
- Receipt-to-DOM: monotonic time between native callback entry and the card's DOM
  update. It does not prove browser compositing or physical screen presentation.
- Publish-to-DOM estimate: publish-to-receipt plus receipt-to-DOM, with the same clock
  caveat. A negative publication age never becomes a valid sample by adding delay.

Each distribution has fixed 1 ms buckets through 10 seconds, with separate invalid
and overflow counters. p50/p95/p99 are bucket upper bounds. A null percentile means
no samples or that the requested rank fell in overflow; consult the counters. Memory
does not grow with duration. Only aggregate counts and one pending frame identity
are kept, not raw audio values or a frame history.

Report accepted frames, DOM updates, frames superseded before DOM update and pending
unpainted frames separately. Repeated paints of one frame do not inflate the count.
The accepted-frame rate uses the full measurement duration, including gaps. It is
not a publisher rate or a packet-loss estimate: fast/slow frames share a sequence
counter and the native validator excludes stale, malformed or mismatched frames.
Latency quantiles cover accepted frames only, so report gaps/rejections separately
before drawing a performance conclusion.

Native delivery attaches callback-entry UTC and monotonic timestamps before parsing
and shared fan-out. The same timestamps reach every card. It also attaches bounded
cumulative fast-event/accepted/decoder-rejected/session-rejected counters, counted
once per underlying subscription pair. These are **since subscription setup**, not
since a card measurement started, and are snapshots at the last accepted frame.
Rejections after that snapshot will not appear until another accepted frame arrives.
Do not interpret those counters as total broker packet loss or as a completed audit
when the feed stops delivering accepted frames.

A functional smoke run is not the blueprint's benchmark. Run 15–30 minutes per mode
with recorded environment, configured rates, source/card counts, visibility, CPU,
memory and network observations. Cover 10/20 Hz, multiple tabs, target tablet and
recovery. Do not claim the provisional <250 ms publish-to-visible target from DOM
timestamps alone.

Opt in on a native-stream card with top-level `diagnostics: true`. Select 10 seconds
or one minute for smoke checks, or 15/30 minutes for sustained runs, then press
**Start measurement**. The read-only report appears after Stop or the deadline.
Hiding/removing the card ends the run with `hidden`; changing configuration clears
it. Keep all benchmark cards visible. Diagnostics are off by default and allocate
no timing histograms until Start. Copy the report before reconfiguring or reloading.

The development runner also emits `observer_timing`: monotonic elapsed time since
observer construction (including connection/startup/shutdown and idle time), and
per-stream accepted frame count, application payload bytes, whole-window frame
rate, mean spacing and maximum spacing. Empty/zero-duration statistics are null.
Spacing includes session transitions. Byte totals exclude rejected frames,
metadata, MQTT headers, retransmissions and HA WebSocket traffic; they are not a
network-interface bandwidth measurement. This observer runs on the bridge PC,
so its timings do not measure HA or browser delay. Aggregates use constant memory.
