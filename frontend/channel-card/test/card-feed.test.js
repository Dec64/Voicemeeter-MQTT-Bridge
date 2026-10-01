import test from "node:test";
import assert from "node:assert/strict";
import { CardFeed } from "../src/card-feed.js";
import { MeterModel, normalizeConfig } from "../src/meter-model.js";

test("feed replacement ignores old callbacks and repeated hass updates do not resubscribe", () => {
  const leases = [], seen = [], hub = { acquire(connection, topic, callback, error) {
    const lease = { connection, topic, callback, error, released: 0, release() { this.released++; } }; leases.push(lease); return lease;
  } };
  const feed = new CardFeed(value => seen.push(value), hub), first = {}, second = {};
  feed.update(first, "a", true); feed.update(first, "a", true); assert.equal(leases.length, 1);
  feed.update(second, "a", true); assert.equal(leases[0].released, 1);
  const count = seen.length; leases[0].callback({ state: "frame" }); leases[0].error(new Error()); assert.equal(seen.length, count);
  leases[1].callback({ state: "frame" }); assert.equal(seen.at(-1).state, "frame");
  feed.stop(); feed.stop(); assert.equal(leases[1].released, 1);
});
test("unconfigured or disabled feeds never subscribe; errors become a safe status", () => {
  const seen = [], hub = { acquire() { throw new Error("private connection details"); } }, feed = new CardFeed(value => seen.push(value), hub);
  feed.update({}, "", true); feed.update({}, "a", false); assert.equal(seen.length, 0);
  feed.update({}, "a", true); assert.equal(seen.at(-1).state, "error"); assert.equal(JSON.stringify(seen).includes("private"), false);
});
test("publication age reduces remaining meter lifetime instead of refreshing old data", () => {
  const model = new MeterModel({ source: { id: "strip:0" } });
  const frame = { schema: 2, session_id: "a", seq: 1, sources: { "strip:0": { available: true, pre_dbfs: -18 } } };
  assert.equal(model.accept(frame, 100, 700), true);
  assert.equal(model.view(149).state, "signal"); assert.equal(model.view(150).state, "stale");
  for (const age of [750, -1, Infinity]) assert.equal(model.accept({ ...frame, seq: 2 }, 151, age), false);
});
test("transport config admits only implemented modes and literal base topics", () => {
  assert.equal(normalizeConfig({}).transport, "auto");
  for (const transport of ["auto", "native_ws", "entities_only"]) assert.equal(normalizeConfig({ bridge: { transport } }).transport, transport);
  for (const bridge of [{ transport: "custom_ws" }, { base_topic: "{{ topic }}" }, { base_topic: "example/" }])
    assert.throws(() => normalizeConfig({ bridge }));
});
