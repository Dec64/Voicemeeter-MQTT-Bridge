import test from "node:test";
import assert from "node:assert/strict";
import { SessionGate, validateMetadata } from "../src/session-gate.js";
import { decodeNativeEvent } from "../src/native-ha-transport.js";

export const metadata = (session = "first") => ({ schema: 2, engine: "potato", session_id: session,
  sources: [{ id: "strip:0", kind: "strip", index: 0, label: "Input", enabled: true, taps: ["pre"] },
    { id: "bus:5", kind: "bus", index: 5, label: "Output", enabled: true, taps: ["output"] }] });
export const frame = (session = "first", seq = 0) => ({ schema: 2, session_id: session, seq,
  sources: { "strip:0": { available: true, pre_dbfs: -18 }, "bus:5": { available: true, output_dbfs: -30 } } });

test("frames require matching metadata and strictly increasing sequence", () => {
  const gate = new SessionGate();
  assert.equal(gate.accept(frame()), null);
  assert.ok(gate.setMetadata(metadata()));
  assert.ok(gate.accept(frame())); assert.equal(gate.accept(frame()), null);
  assert.equal(gate.accept(frame("other", 1)), null);
  assert.ok(gate.accept(frame("first", 1)));
});
test("metadata permits restart but cannot roll back to a retired session", () => {
  const gate = new SessionGate(); gate.setMetadata(metadata()); gate.accept(frame("first", 99));
  assert.ok(gate.setMetadata(metadata("second"))); assert.ok(gate.accept(frame("second")));
  assert.equal(gate.setMetadata(metadata()), null); assert.equal(gate.accept(frame("first", 100)), null);
  assert.ok(gate.accept(frame("second", 1)));
});
test("same-session metadata never resets sequence; invalid metadata revokes readings", () => {
  const gate = new SessionGate(); gate.setMetadata(metadata()); gate.accept(frame());
  gate.setMetadata(metadata()); assert.equal(gate.accept(frame()), null);
  assert.equal(gate.setMetadata({}), null); assert.equal(gate.accept(frame("first", 1)), null);
  gate.setMetadata(metadata()); assert.ok(gate.accept(frame("first", 1)));
});
test("metadata rejects duplicate ids, inconsistent kind/index and invented taps", () => {
  const source = metadata().sources[0];
  for (const sources of [[source, source], [{ ...source, index: 1 }], [{ ...source, kind: "bus" }],
    [{ ...source, taps: ["output"] }], [{ ...source, enabled: "true" }], [{ ...source, label: 42 }]])
    assert.equal(validateMetadata({ ...metadata(), sources }), null);
});
test("disabled sources and unadvertised meter taps are never forwarded", () => {
  const gate = new SessionGate(), meta = metadata(); meta.sources[0].enabled = false;
  gate.setMetadata(meta); assert.equal(gate.accept(frame()), null);
  gate.setMetadata(metadata());
  const bad = frame(); bad.sources["strip:0"].post_mute_dbfs = -20;
  assert.equal(gate.accept(bad), null); assert.ok(gate.accept(frame()));
});
test("metadata is copied and frozen; bounded retired session memory fails closed", () => {
  const gate = new SessionGate(), meta = metadata(); gate.setMetadata(meta); meta.sources[0].enabled = false;
  assert.ok(gate.accept(frame())); assert.ok(Object.isFrozen(gate.metadata.sources[0].taps));
  for (let i = 0; i < 128; i++) gate.setMetadata(metadata(`next-${i}`));
  assert.equal(gate.setMetadata(metadata("overflow")), null); assert.equal(gate.accept(frame("overflow")), null);
});
test("native decoder accepts the UTC offset emitted by System.Text.Json", () => {
  const now = Date.parse("2026-10-01T12:00:00Z");
  const value = { ...frame(), published_at_utc: "2026-10-01T12:00:00.0000000+00:00" };
  const event = { variables: { trigger: { platform: "mqtt", topic: "test", payload: JSON.stringify(value) } } };
  assert.ok(decodeNativeEvent(event, "test", now));
});
