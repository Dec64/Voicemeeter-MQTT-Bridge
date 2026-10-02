import test from "node:test";
import assert from "node:assert/strict";
import { VisibleRenderer } from "../src/visible-renderer.js";
function harness() {
  const queue = new Map(), paints = []; let id = 0;
  const renderer = new VisibleRenderer(() => paints.push(true), cb => { queue.set(++id, cb); return id; }, key => queue.delete(key));
  const tick = time => { const pending = [...queue.values()]; queue.clear(); pending.forEach(cb => cb(time)); };
  return { renderer, queue, paints, tick };
}
test("hidden cards schedule no work and many updates coalesce to one paint", () => {
  const h = harness(); h.renderer.request(); assert.equal(h.queue.size, 0);
  h.renderer.setActive(true); for (let i = 0; i < 100; i++) h.renderer.request();
  assert.equal(h.queue.size, 1); h.tick(0); assert.equal(h.paints.length, 1); assert.equal(h.queue.size, 0);
});
test("visible painting is capped at 30fps without an idle animation loop", () => {
  const h = harness(); h.renderer.setActive(true); h.tick(0); h.renderer.request();
  h.tick(16); assert.equal(h.paints.length, 1); h.tick(32); assert.equal(h.paints.length, 1);
  h.tick(34); assert.equal(h.paints.length, 2); assert.equal(h.queue.size, 0);
});
test("hiding cancels scheduled work and a late callback cannot paint", () => {
  const h = harness(); h.renderer.setActive(true); const late = [...h.queue.values()][0];
  h.renderer.setActive(false); assert.equal(h.queue.size, 0); late(10); assert.equal(h.paints.length, 0);
  h.renderer.setActive(true); h.tick(20); assert.equal(h.paints.length, 1);
});
