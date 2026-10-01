import test from "node:test";
import assert from "node:assert/strict";
import { createNativeTelemetry, decodeNativeEvent, validateNativeTopic } from "../src/native-ha-transport.js";
import { MeterModel } from "../src/meter-model.js";

const now = Date.parse("2026-10-01T12:00:00Z"), topic = "fixture/levels/fast";
const frame = (changes = {}) => ({ schema: 2, session_id: "fixture", seq: 1,
  published_at_utc: new Date(now).toISOString(), sources: { "strip:0": { available: true, pre_dbfs: -18 } }, ...changes });
const event = (value = frame()) => ({ variables: { trigger: { platform: "mqtt", topic, payload: JSON.stringify(value) } } });

test("native request uses the existing connection, shares subscribers and unsubscribes once", async () => {
  let callback, opens = 0, closes = 0;
  const connection = { async subscribeMessage(cb, request) {
    opens++; callback = cb;
    assert.deepEqual(request, { type: "subscribe_trigger", trigger: { platform: "mqtt", topic, qos: 0 } });
    return async () => { closes++; };
  } };
  const hub = createNativeTelemetry(() => now), seen = [];
  const a = hub.acquire(connection, topic, value => seen.push(value));
  const b = hub.acquire(connection, topic, value => seen.push(value));
  await Promise.all([a.ready, b.ready]); callback(event());
  assert.equal(opens, 1); assert.equal(seen.length, 2); assert.equal(seen[0], seen[1]);
  await a.release(); assert.equal(closes, 0);
  await b.release(); assert.equal(closes, 1);
  callback(event()); assert.equal(seen.length, 2);
});

test("only the unwrapped MQTT event on the exact topic is accepted", () => {
  assert.equal(decodeNativeEvent(event(), topic, now).seq, 1);
  for (const value of [null, {}, { event: event() }, { variables: { trigger: { ...event().variables.trigger, topic: "other" } } },
    { variables: { trigger: { ...event().variables.trigger, platform: "state" } } }])
    assert.equal(decodeNativeEvent(value, topic, now), null);
});

test("malformed, oversized and non-string payloads are dropped", () => {
  for (const payload of ["{", " ".repeat(65537), {}, null, "null", "[]"])
    assert.equal(decodeNativeEvent({ variables: { trigger: { platform: "mqtt", topic, payload } } }, topic, now), null);
});

test("old, future and ambiguous publication timestamps are dropped", () => {
  for (const published_at_utc of [new Date(now - 750).toISOString(), new Date(now + 1).toISOString(), "invalid", "2026-10-01T12:00:00"])
    assert.equal(decodeNativeEvent(event(frame({ published_at_utc })), topic, now), null);
  assert.ok(decodeNativeEvent(event(frame({ published_at_utc: new Date(now - 749).toISOString() })), topic, now));
  assert.equal(decodeNativeEvent(event(), topic, NaN), null);
});

test("invalid envelope, source ids and impossible levels cannot refresh cards", () => {
  for (const change of [{ schema: 1 }, { session_id: "" }, { seq: -1 }, { seq: 1.5 }, { sources: [] },
    { sources: { "strip:8": { available: true } } }, { sources: { "strip:0": { available: "yes" } } },
    { sources: { "strip:0": { available: true, pre_dbfs: 61 } } }])
    assert.equal(decodeNativeEvent(event(frame(change)), topic, now), null);
});

test("template and wildcard topics fail before any HA request", async () => {
  for (const value of ["", " a", "a/#", "a/+", "{{ topic }}", "{% raw %}a", "{# comment #}", "a\0b"]) {
    assert.throws(() => validateNativeTopic(value));
  }
  let opens = 0, errors = 0;
  const lease = createNativeTelemetry().acquire({ subscribeMessage() { opens++; } }, "{{ topic }}", () => {}, () => errors++);
  await lease.ready; await lease.release(); assert.equal(opens, 0); assert.equal(errors, 1);
});

test("HA permission failures reach consumers without an unhandled rejection", async () => {
  const error = { code: "unauthorized" }, errors = [];
  const lease = createNativeTelemetry().acquire({ async subscribeMessage() { throw error; } }, topic, () => {}, e => errors.push(e));
  await lease.ready; await lease.release(); assert.deepEqual(errors, [error]);
});

test("decoded frames still obey card ordering and session boundaries", () => {
  const model = new MeterModel({ source: { id: "strip:0" } });
  assert.equal(model.accept(decodeNativeEvent(event(), topic, now), 0), true);
  assert.equal(model.accept(decodeNativeEvent(event(), topic, now), 1), false);
  assert.equal(model.accept(decodeNativeEvent(event(frame({ session_id: "replacement", seq: 2 })), topic, now), 2), false);
  assert.equal(model.view(750).state, "stale");
});
