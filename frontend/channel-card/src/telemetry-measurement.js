// Fixed 1 ms buckets; percentiles are upper bounds, not exact raw-sample quantiles.
export class MillisecondHistogram {
  constructor() { this.buckets = new Float64Array(10001); this.count = 0; this.invalid = 0; this.overflow = 0; }
  add(value) {
    if (!Number.isFinite(value) || value < 0) { this.invalid++; return; }
    this.count++;
    if (value > 10000) this.overflow++;
    else this.buckets[Math.ceil(value)]++;
  }
  percentile(fraction) {
    if (!this.count) return null;
    const rank = Math.ceil(this.count * fraction);
    let seen = 0;
    for (let i = 0; i < this.buckets.length; i++) {
      seen += this.buckets[i]; if (seen >= rank) return i;
    }
    return null; // The requested quantile is in the >10 s overflow bucket.
  }
  snapshot() {
    return { count: this.count, invalid: this.invalid, overflow: this.overflow,
      p50_ms: this.percentile(.5), p95_ms: this.percentile(.95), p99_ms: this.percentile(.99) };
  }
}

// Stores counters, four fixed histograms and one pending frame identity, never audio
// values or a growing frame history. Callers opt in for a bounded measurement window.
export class TelemetryMeasurement {
  constructor(start, duration) {
    if (!Number.isFinite(start) || start < 0 || !Number.isFinite(duration) || duration < 1 || duration > 1800000)
      throw new Error("Choose a measurement duration up to 30 minutes.");
    this.start = start; this.deadline = start + duration; this.end = null;
    this.received = 0; this.painted = 0; this.superseded = 0; this.sessions = 0;
    this.session = null; this.sequence = -1; this.lastReceipt = null; this.pending = null;
    this.publishReceipt = new MillisecondHistogram(); this.spacing = new MillisecondHistogram();
    this.receiptDom = new MillisecondHistogram(); this.publishDom = new MillisecondHistogram();
  }
  running(now) {
    if (now >= this.deadline && this.end === null) this.end = this.deadline;
    return this.end === null && Number.isFinite(now) && now >= this.start;
  }
  receive(timing) {
    const { session, sequence, receivedMonoMs: now, receivedUtcMs, publishedUtcMs } = timing;
    if (!this.running(now) || (this.lastReceipt !== null && now < this.lastReceipt) ||
        (session === this.session && sequence <= this.sequence)) return false;
    if (session !== this.session) { this.sessions++; this.session = session; }
    this.sequence = sequence;
    if (this.pending) this.superseded++;
    if (this.lastReceipt !== null) this.spacing.add(now - this.lastReceipt);
    this.lastReceipt = now; this.received++;
    const age = receivedUtcMs - publishedUtcMs;
    this.publishReceipt.add(age);
    this.pending = { session, sequence, now, age };
    return true;
  }
  dom(session, sequence, now) {
    const pending = this.pending;
    if (!this.running(now) || !pending || pending.session !== session || pending.sequence !== sequence || now < pending.now) return false;
    const delay = now - pending.now;
    this.receiptDom.add(delay);
    // A negative publication age is clock skew, even if adding render delay would
    // turn it positive. Do not turn skew into a seemingly valid latency sample.
    if (Number.isFinite(pending.age) && pending.age >= 0) this.publishDom.add(pending.age + delay);
    this.pending = null; this.painted++;
    return true;
  }
  stop(now) {
    if (!Number.isFinite(now) || now < this.start || (this.lastReceipt !== null && now < this.lastReceipt))
      throw new Error("Invalid measurement clock.");
    if (this.end === null) this.end = Math.min(now, this.deadline);
  }
  snapshot(now) {
    this.running(now);
    const elapsed = Math.max(0, (this.end ?? now) - this.start);
    return { completed: this.end !== null, scheduled_duration_ms: this.deadline - this.start,
      elapsed_ms: elapsed, received_frames: this.received, dom_updates: this.painted,
      superseded_before_dom: this.superseded, unpainted_frames: this.received - this.painted - this.superseded,
      sessions: this.sessions, received_hz: elapsed > 0 ? this.received * 1000 / elapsed : null,
      publish_to_receipt: this.publishReceipt.snapshot(), receipt_spacing: this.spacing.snapshot(),
      receipt_to_dom: this.receiptDom.snapshot(), publish_to_dom_estimate: this.publishDom.snapshot() };
  }
}
