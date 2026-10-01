import test from "node:test";
import assert from "node:assert/strict";
import { SharedTelemetry } from "../src/shared-telemetry.js";
const deferred = () => { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; };

test("same connection/topic shares one feed until the final release", async () => {
  let opens = 0, closes = 0, deliver;
  const hub = new SharedTelemetry(async (_, __, callback) => { opens++; deliver = callback; return () => { closes++; }; });
  const connection = {}, left = [], right = [];
  const a = hub.acquire(connection, "topic", value => left.push(value));
  const b = hub.acquire(connection, "topic", value => right.push(value));
  await Promise.all([a.ready, b.ready]); assert.equal(opens, 1);
  deliver(1); await a.release(); deliver(2);
  assert.deepEqual(left, [1]); assert.deepEqual(right, [1, 2]); assert.equal(closes, 0);
  await b.release(); await b.release(); assert.equal(closes, 1);
});
test("different connections and topics never share", async () => {
  let opens = 0; const hub = new SharedTelemetry(async () => { opens++; return () => {}; });
  const connection = {};
  const leases = [hub.acquire(connection, "a", () => {}), hub.acquire(connection, "b", () => {}), hub.acquire({}, "a", () => {})];
  await Promise.all(leases.map(x => x.ready)); assert.equal(opens, 3);
  await Promise.all(leases.map(x => x.release()));
});
test("removal during setup awaits and then closes the actual subscription", async () => {
  const gate = deferred(); let closes = 0;
  const hub = new SharedTelemetry(() => gate.promise); const connection = {};
  const lease = hub.acquire(connection, "a", () => {}); await Promise.resolve();
  const stopping = lease.release(); gate.resolve(() => { closes++; }); await stopping;
  assert.equal(closes, 1);
});
test("joining while old subscription closes waits before opening a replacement", async () => {
  const gate = deferred(); let opens = 0;
  const hub = new SharedTelemetry(async () => { opens++; return opens === 1 ? () => gate.promise : () => {}; });
  const connection = {}; const a = hub.acquire(connection, "a", () => {}); await a.ready;
  const stopped = a.release(); await Promise.resolve();
  const b = hub.acquire(connection, "a", () => {}); assert.equal(opens, 1);
  gate.resolve(); await Promise.all([stopped, b.ready]); assert.equal(opens, 2); await b.release();
});
test("setup failure is reported and a released entry can retry", async () => {
  let attempts = 0; const errors = [];
  const hub = new SharedTelemetry(async () => { if (++attempts === 1) throw new Error("denied"); return () => {}; });
  const connection = {}; const a = hub.acquire(connection, "a", () => {}, error => errors.push(error.message));
  await a.ready; assert.deepEqual(errors, ["denied"]); await a.release();
  const b = hub.acquire(connection, "a", () => {}); await b.ready; assert.equal(attempts, 2); await b.release();
});
test("failed cleanup never silently opens a duplicate", async () => {
  let opens = 0; const hub = new SharedTelemetry(async () => { opens++; return () => { throw new Error("close failed"); }; });
  const connection = {}; const a = hub.acquire(connection, "a", () => {}); await a.ready; await a.release();
  const errors = []; const b = hub.acquire(connection, "a", () => {}, error => errors.push(error.message));
  await b.ready; assert.equal(opens, 1); assert.deepEqual(errors, ["close failed"]); await b.release();
});
test("one bad consumer cannot block siblings or cleanup", async () => {
  let deliver; const hub = new SharedTelemetry(async (_, __, callback) => { deliver = callback; return () => {}; });
  const connection = {}, seen = [];
  const a = hub.acquire(connection, "a", () => { throw new Error("render failed"); }, () => { throw new Error("handler failed"); });
  const b = hub.acquire(connection, "a", value => seen.push(value)); await b.ready;
  deliver(7); assert.deepEqual(seen, [7]); await a.release(); await b.release();
});
test("late events from a closed generation cannot reach replacement subscribers", async () => {
  const gate = deferred(); const callbacks = []; let opens = 0;
  const hub = new SharedTelemetry(async (_, __, callback) => { callbacks.push(callback); return ++opens === 1 ? () => gate.promise : () => {}; });
  const connection = {}; const a = hub.acquire(connection, "a", () => {}); await a.ready;
  const closing = a.release(); await Promise.resolve();
  const seen = []; const b = hub.acquire(connection, "a", value => seen.push(value));
  callbacks[0]("closing"); gate.resolve(); await closing;
  callbacks[0]("late"); callbacks[1]("new");
  assert.deepEqual(seen, ["new"]); await b.release();
});
test("consumer mutation cannot corrupt sibling readings or the input fixture", async () => {
  let deliver; const hub = new SharedTelemetry(async (_, __, callback) => { deliver = callback; return () => {}; });
  const connection = {}, seen = [];
  const a = hub.acquire(connection, "a", frame => { frame.sources.input.level = 0; });
  const b = hub.acquire(connection, "a", frame => seen.push(frame.sources.input.level)); await b.ready;
  const frame = { sources: { input: { level: -18 } } }; deliver(frame);
  assert.deepEqual(seen, [-18]); assert.equal(Object.isFrozen(frame), false);
  await a.release(); await b.release();
});
