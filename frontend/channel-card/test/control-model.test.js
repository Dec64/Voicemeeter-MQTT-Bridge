import test from "node:test";
import assert from "node:assert/strict";
import { normalizeControls, readControl, controlRequest, ROUTES } from "../src/control-model.js";
import { ControlCommands } from "../src/control-commands.js";
const raw = { source: { id: "strip:0" }, controls: { gain: true, mute: true, routing: true },
  entities: { gain: "number.input_gain", mute: "switch.input_mute", routes: { A1: "switch.input_a1" } } };
const config = normalizeControls(raw);
function hass(callService = async () => {}) { return { callService, connection: { connected: true },
  services: { number: { set_value: {} }, switch: { turn_on: {}, turn_off: {} } }, states: {
    "number.input_gain": { state: "-6", attributes: { min: -60, max: 12, step: 0.1, unit_of_measurement: "dB" } },
    "switch.input_mute": { state: "off", attributes: {} }, "switch.input_a1": { state: "on", attributes: {} }
  } }; }
test("controls require explicit visibility/bindings and bus routing is absent", () => {
  assert.equal(normalizeControls({}).bindings.length, 0);
  assert.deepEqual(config.bindings.map(b => b.key), ["gain", "mute", "route:A1"]);
  assert.deepEqual(normalizeControls({ ...raw, source: { id: "bus:0" } }).bindings.map(b => b.key), ["gain", "mute"]);
  assert.throws(() => normalizeControls({ ...raw, entities: { mute: "light.wrong" } }));
  assert.throws(() => normalizeControls({ ...raw, entities: { mute: "switch.same", routes: { A1: "switch.same" } } }));
});
test("readback and commands enforce gain range, step and exact entity", () => {
  const view = readControl(config.bindings[0], hass()); assert.equal(view.value, -6);
  assert.deepEqual(controlRequest(view, -12.3), { domain: "number", service: "set_value", data: { entity_id: "number.input_gain", value: -12.3 } });
  for (const value of [-61, 13, -12.35, NaN, "-12"]) assert.equal(controlRequest(view, value), null);
  assert.deepEqual(controlRequest(readControl(config.bindings[1], hass()), true), {
    domain: "switch", service: "turn_on", data: { entity_id: "switch.input_mute" } });
});
test("explicit legacy gain mappings allow an omitted unit within the existing gain range", () => {
  const h = hass(); delete h.states["number.input_gain"].attributes.unit_of_measurement;
  assert.equal(readControl(config.bindings[0], h).available, true);
  h.states["number.input_gain"].attributes.max = 100; assert.equal(readControl(config.bindings[0], h).available, false);
});
test("unknown, disconnected, missing service and invalid number attributes disable commands", () => {
  for (const mutate of [h => h.connection.connected = false, h => h.states["number.input_gain"].state = "unknown",
    h => h.states["number.input_gain"].attributes.step = 0, h => h.states["number.input_gain"].attributes.unit_of_measurement = "%",
    h => delete h.services.number.set_value]) {
    const h = hass(); mutate(h); assert.equal(readControl(config.bindings[0], h).available, false);
  }
});
test("service acknowledgement alone does not confirm a command", async () => {
  const calls = [], h = hass(async (...args) => calls.push(args)), commands = new ControlCommands(() => {});
  commands.configure(config); commands.update(h); assert.equal(commands.send("gain", -12), true);
  await Promise.resolve(); await Promise.resolve(); assert.equal(commands.status("gain").phase, "pending");
  assert.equal(commands.send("gain", -10), false); assert.equal(calls.length, 1);
  h.states = { ...h.states, "number.input_gain": { ...h.states["number.input_gain"], state: "-12" } }; commands.update(h);
  assert.equal(commands.status("gain").phase, "idle"); commands.dispose();
});
test("timeout blocks retry while service is still in flight, and late completion stays failed", async () => {
  let finish; const timers = new Map(); let id = 0;
  const commands = new ControlCommands(() => {}, callback => { timers.set(++id, callback); return id; }, key => timers.delete(key));
  commands.configure(config); commands.update(hass(() => new Promise(resolve => { finish = resolve; })));
  commands.send("mute", true); [...timers.values()][0]();
  assert.equal(commands.status("mute").phase, "timeout"); assert.equal(commands.send("mute", true), false);
  finish(); await Promise.resolve(); await Promise.resolve(); assert.equal(commands.status("mute").phase, "timeout");
  assert.equal(commands.status("mute").busy, false); commands.dispose();
});
test("service errors are bounded text and reconfiguration ignores old completion", async () => {
  let reject; const commands = new ControlCommands(() => {}); commands.configure(config);
  commands.update(hass(() => new Promise((_, fail) => { reject = fail; }))); commands.send("mute", true);
  commands.configure(normalizeControls({})); reject(new Error("private details"));
  await Promise.resolve(); await Promise.resolve(); assert.equal(commands.status("mute").phase, "idle"); commands.dispose();
});
test("mute waits for service completion and observed state, and sends explicit off after external on", async () => {
  let finish; const calls = [], h = hass((...args) => { calls.push(args); return new Promise(resolve => { finish = resolve; }); });
  const commands = new ControlCommands(() => {}); commands.configure(config); commands.update(h);
  commands.send("mute", true); h.states["switch.input_mute"] = { state: "on", attributes: {} }; commands.update(h);
  assert.equal(commands.status("mute").phase, "pending"); finish(); await Promise.resolve();
  assert.equal(commands.status("mute").phase, "idle"); commands.send("mute", false);
  assert.deepEqual(calls[1], ["switch", "turn_off", { entity_id: "switch.input_mute" }]);
  commands.dispose(); finish();
});
test("unknown switch readback and rejected mute actions cannot invent state", async () => {
  const h = hass(async () => { throw new Error("permission denied"); }), commands = new ControlCommands(() => {});
  commands.configure(config); commands.update(h); commands.send("mute", true); await Promise.resolve();
  assert.equal(commands.status("mute").phase, "error"); assert.equal(readControl(config.bindings[1], h).value, false);
  h.states["switch.input_mute"].state = "unknown"; commands.update(h);
  assert.equal(commands.send("mute", true), false); commands.dispose();
});
test("every route targets its own entity and route pending state does not block siblings", () => {
  const routes = Object.fromEntries(ROUTES.map(route => [route, `switch.input_${route.toLowerCase()}`]));
  const mapped = normalizeControls({ ...raw, controls: { routing: true }, entities: { routes } });
  const calls = [], h = hass((...args) => { calls.push(args); return new Promise(() => {}); });
  for (const entity of Object.values(routes)) h.states[entity] = { state: "off", attributes: {} };
  const commands = new ControlCommands(() => {}); commands.configure(mapped); commands.update(h);
  for (const route of ROUTES) assert.equal(commands.send(`route:${route}`, true), true);
  assert.deepEqual(calls.map(call => call[2].entity_id), Object.values(routes));
  assert.equal(commands.send("route:A1", false), false); commands.dispose();
  assert.throws(() => normalizeControls({ ...raw, entities: { routes: { A6: "switch.wrong" } } }));
});
