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

A functional smoke run is not the blueprint's benchmark. Run 15–30 minutes per mode
with recorded environment, configured rates, source/card counts, visibility, CPU,
memory and network observations. Cover 10/20 Hz, multiple tabs, target tablet and
recovery. Do not claim the provisional <250 ms publish-to-visible target from DOM
timestamps alone.
