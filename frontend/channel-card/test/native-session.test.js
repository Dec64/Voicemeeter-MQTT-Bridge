import test from "node:test";
import assert from "node:assert/strict";
import { createNativeSessionTelemetry } from "../src/native-session.js";

const base = "example/pc", now = Date.parse("2026-10-01T12:00:00Z");
const metadata = session => ({ schema: 2, engine: "potato", session_id: session,
  sources: [{ id: "strip:0", kind: "strip", index: 0, label: "Input", enabled: true, taps: ["pre"] }] });
const frame = (session, seq = 0) => ({ schema: 2, session_id: session, seq, published_at_utc: new Date(now).toISOString(),
  sources: { "strip:0": { available: true, pre_dbfs: -18 } } });
function fakeConnection() {
  const callbacks = new Map(), listeners = new Map(), closed = [];
  return { callbacks, listeners, closed, connected: true,
    addEventListener(name, cb) { listeners.set(name, cb); }, removeEventListener(name) { listeners.delete(name); },
    async subscribeMessage(cb, request) { callbacks.set(request.trigger.topic, cb); return async () => { closed.push(request.trigger.topic); }; },
    emit(suffix, payload) { const topic = `${base}/v2/${suffix}`; callbacks.get(topic)?.({ variables: { trigger: { platform: "mqtt", topic, payload: JSON.stringify(payload) } } }); }
  };
}
test("cards share one metadata/fast pair and a late join gets metadata with the next frame", async () => {
  const c = fakeConnection(), hub = createNativeSessionTelemetry(() => now), a = [], b = [];
  const first = hub.acquire(c, base, value => a.push(value)); await first.ready;
  c.emit("meters/fast", frame("one")); assert.equal(a.length, 0);
  c.emit("metadata", metadata("one")); c.emit("meters/fast", frame("one"));
  const second = hub.acquire(c, base, value => b.push(value)); await second.ready;
  assert.equal(b.length, 0); c.emit("meters/fast", frame("one", 1));
  assert.equal(b[0].metadata.session_id, "one"); assert.equal(b[0].frame.seq, 1); assert.equal(c.callbacks.size, 2);
  await first.release(); assert.equal(c.closed.length, 0); await second.release();
  assert.equal(c.closed.length, 2); assert.equal(c.listeners.size, 0);
});
test("disconnect revokes trust; reconnect requires metadata and maintains replay protection", async () => {
  const c = fakeConnection(), seen = [], lease = createNativeSessionTelemetry(() => now).acquire(c, base, value => seen.push(value));
  await lease.ready; c.emit("metadata", metadata("one")); c.emit("meters/fast", frame("one"));
  c.connected = false; c.listeners.get("disconnected")(); assert.equal(seen.at(-1).state, "disconnected");
  c.emit("metadata", metadata("two")); c.connected = true; c.emit("meters/fast", frame("two"));
  assert.equal(seen.at(-1).state, "disconnected");
  c.emit("metadata", metadata("two")); c.emit("meters/fast", frame("two")); assert.equal(seen.at(-1).frame.session_id, "two");
  c.emit("metadata", metadata("one")); c.emit("meters/fast", frame("one", 1)); assert.equal(seen.at(-1).frame.session_id, "two");
  await lease.release();
});
test("metadata changes clear readings and invalid metadata blocks subsequent frames", async () => {
  const c = fakeConnection(), seen = [], lease = createNativeSessionTelemetry(() => now).acquire(c, base, value => seen.push(value));
  await lease.ready; c.emit("metadata", metadata("one")); c.emit("meters/fast", frame("one"));
  c.emit("metadata", {}); assert.equal(seen.at(-1).state, "invalid_metadata"); assert.equal(seen.at(-1).frame, null);
  const count = seen.length; c.emit("meters/fast", frame("one", 1)); assert.equal(seen.length, count);
  await lease.release();
});
test("partial setup failure is visible and final release cleans the successful subscription", async () => {
  const c = fakeConnection(), open = c.subscribeMessage, seen = [];
  c.subscribeMessage = async (cb, request) => { if (request.trigger.topic.endsWith("fast")) throw new Error("denied"); return open(cb, request); };
  const lease = createNativeSessionTelemetry().acquire(c, base, value => seen.push(value)); await lease.ready;
  assert.equal(seen.at(-1).state, "error"); await lease.release(); assert.equal(c.closed.length, 1); assert.equal(c.listeners.size, 0);
});
test("both cleanups run even when one rejects, and replacement is blocked", async () => {
  const c = fakeConnection(), seen = [], hub = createNativeSessionTelemetry(); let attempts = 0;
  c.subscribeMessage = async () => async () => { attempts++; if (attempts === 1) throw new Error("failed close"); };
  const first = hub.acquire(c, base, () => {}, error => seen.push(error)); await first.ready; await first.release();
  const next = hub.acquire(c, base, () => {}, error => seen.push(error)); await next.ready;
  assert.equal(attempts, 2); assert.equal(seen.length, 1); await next.release();
});
