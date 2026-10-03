import test from "node:test";
import assert from "node:assert/strict";
import { createNativeSessionTelemetry } from "../src/native-session.js";
import { describeAdvanced } from "../src/advanced-controls.js";

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

test("full multi-source EQ metadata reaches cards without exceeding shared fan-out bounds", async () => {
  const value = metadata("full"); value.sources = [];
  for (const kind of ["strip", "bus"]) for (let index=0;index<8;index++) {
    const id=`${kind}:${index}`, controls=[];
    if(kind==='bus'||index<5)for(let channel=0;channel<8;channel++)for(let cell=0;cell<6;cell++)for(const field of ['on','type','f','gain','q']) {
      const spec=describeAdvanced(`${kind}_${index}_eq_channel_${channel}_cell_${cell}_${field}`,id);
      controls.push({id:spec.id,group:spec.group,min:spec.min,max:spec.max,step:spec.step,kind:spec.domain,discovery_unique_id:`vm_${spec.id}`});
    }
    value.sources.push({id,kind,index,label:id,enabled:true,taps:kind==='bus'?['output']:['pre','post_mute'],controls});
  }
  const c=fakeConnection(), seen=[], errors=[], hub=createNativeSessionTelemetry(()=>now);
  const lease=hub.acquire(c,base,event=>seen.push(event),error=>errors.push(error)); await lease.ready;
  c.emit('metadata',value); c.emit('meters/fast',frame('full'));
  assert.deepEqual(errors,[]);
  assert.equal(seen.at(-1).state,'frame');
  assert.equal(seen.at(-1).metadata.sources[0].controls.length,240);
  assert.ok(Object.isFrozen(seen.at(-1).metadata.sources[0].controls[0]));
  await lease.release();
});
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

test("shared deliveries retain callback-entry timing and count rejected events once", async () => {
  const c = fakeConnection(), a = [], b = [];
  let clockCalls = 0;
  const hub = createNativeSessionTelemetry(() => now + 25, () => { clockCalls++; return 100; });
  const first = hub.acquire(c, base, value => a.push(value)); await first.ready;
  const second = hub.acquire(c, base, value => b.push(value)); await second.ready;
  c.emit("meters/fast", frame("wrong"));
  c.emit("meters/fast", {});
  c.emit("metadata", metadata("one")); c.emit("meters/fast", frame("one"));
  assert.deepEqual(a.at(-1).timing, { session: "one", sequence: 0,
    receivedMonoMs: 100, receivedUtcMs: now + 25, publishedUtcMs: now });
  assert.deepEqual(b.at(-1).timing, a.at(-1).timing);
  assert.deepEqual(a.at(-1).transport, { fast_events: 3, accepted: 1, decoder_rejected: 1, session_rejected: 1 });
  assert.equal(clockCalls, 3); // One receipt clock read per event, not per card.
  await first.release(); await second.release();
});
