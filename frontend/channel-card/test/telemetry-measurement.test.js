import test from "node:test";
import assert from "node:assert/strict";
import { MillisecondHistogram, TelemetryMeasurement } from "../src/telemetry-measurement.js";

test("histogram percentiles are bounded bucket upper limits, with explicit overflow", () => {
  const h = new MillisecondHistogram();
  for (const n of [0, 1.2, 2, 4.1, 10001, -1, NaN]) h.add(n);
  assert.deepEqual(h.snapshot(), { count: 5, invalid: 2, overflow: 1, p50_ms: 2, p95_ms: null, p99_ms: null });
  for (let i = 0; i < 100000; i++) h.add(1);
  assert.equal(h.buckets.length, 10001);
});
const timing = (sequence, time, age = 10, session = "one") => ({ session, sequence,
  receivedMonoMs: time, receivedUtcMs: 100000 + time, publishedUtcMs: 100000 + time - age });

test("receipt and DOM measurements count coalescing without repeated paints", () => {
  const m = new TelemetryMeasurement(0, 1000);
  m.receive(timing(0, 50)); m.receive(timing(1, 100));
  assert.equal(m.dom("one", 0, 110), false);
  assert.equal(m.dom("one", 1, 112), true);
  assert.equal(m.dom("one", 1, 120), false);
  m.stop(200);
  const r = m.snapshot(500);
  assert.equal(r.elapsed_ms, 200); assert.equal(r.received_frames, 2);
  assert.equal(r.dom_updates, 1); assert.equal(r.superseded_before_dom, 1);
  assert.equal(r.receipt_spacing.p50_ms, 50);
  assert.equal(r.receipt_to_dom.p50_ms, 12);
  assert.equal(r.publish_to_dom_estimate.p50_ms, 22);
  assert.equal(r.received_hz, 10);
  assert.equal(m.receive(timing(2, 201)), false);
});

test("deadline, invalid clocks, new sessions and clock skew stay explicit", () => {
  const m = new TelemetryMeasurement(100, 1000);
  assert.equal(m.receive(timing(0, 99)), false);
  m.receive(timing(1, 150, -3)); m.dom("one", 1, 160);
  assert.equal(m.receive(timing(1, 170)), false);
  assert.equal(m.receive(timing(2, 140)), false);
  m.receive(timing(0, 200, 10, "two"));
  assert.equal(m.receive(timing(1, 1100, 10, "two")), false);
  const r = m.snapshot(2000);
  assert.equal(r.elapsed_ms, 1000); assert.equal(r.completed, true);
  assert.equal(r.sessions, 2); assert.equal(r.received_frames, 2);
  assert.equal(r.publish_to_receipt.invalid, 1);
  assert.equal(r.publish_to_dom_estimate.count, 0);
  assert.equal(r.unpainted_frames, 1);
  assert.throws(() => new TelemetryMeasurement(0, 0));
});
