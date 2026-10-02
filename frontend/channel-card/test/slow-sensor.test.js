import test from "node:test";
import assert from "node:assert/strict";
import { slowSensorView } from "../src/slow-sensor.js";
import { normalizeConfig } from "../src/meter-model.js";
import { applyEditorValues } from "../src/editor-config.js";
const now = Date.parse("2026-10-01T12:00:00Z");
const config = normalizeConfig({ source: { id: "strip:0" }, entities: { meters: { pre: "sensor.input_peak", post_mute: "sensor.muted_peak" } } });
const hass = (state = "-18", changes = {}) => ({ states: { "sensor.input_peak": { state,
  last_updated: new Date(now).toISOString(), attributes: { unit_of_measurement: "dBFS" }, ...changes } } });
test("slow sensor uses only the explicitly mapped tap and preserves silence", () => {
  assert.equal(slowSensorView(config, hass(), now).level, -18);
  assert.equal(slowSensorView(config, hass("-90"), now).state, "silence");
  assert.equal(slowSensorView({ ...config, tap: "post_mute" }, hass(), now).state, "unavailable");
  assert.equal(slowSensorView({ ...config, meters: {} }, hass(), now), null);
});
test("missing/unavailable/non-numeric/wrong-unit states cannot become zero", () => {
  for (const state of ["", " ", "unknown", "unavailable", "NaN", "Infinity", "0x10", "-18 dB", true, "61", "-201"])
    assert.equal(slowSensorView(config, hass(state), now).level, null);
  assert.equal(slowSensorView(config, hass("-18", { attributes: { unit_of_measurement: "dB" } }), now).level, null);
  assert.equal(slowSensorView(config, { connection: { connected: false }, ...hass() }, now).level, null);
});
test("repeated hass updates never refresh stale sensor timestamps", () => {
  const value = hass();
  assert.equal(slowSensorView(config, value, now + 14999).level, -18);
  assert.equal(slowSensorView(config, value, now + 15000).state, "stale");
  assert.equal(slowSensorView(config, value, now - 1).level, null);
  assert.equal(slowSensorView(config, hass("-18", { last_updated: "bad" }), now).level, null);
});
test("sensor config is copied and source changes cannot inherit another source's entities", () => {
  assert.ok(Object.isFrozen(config.meters));
  assert.throws(() => normalizeConfig({ entities: { meters: { pre: "switch.input" } } }));
  const original = { source: { id: "strip:0" }, entities: { gain: "number.gain", meters: { pre: "sensor.input", post_mute: "sensor.muted" } } };
  const next = applyEditorValues(original, { topic: "", id: "bus:5", label: "", floor: "-90", sensor: "sensor.output" });
  assert.equal(next.entities.meters.output, "sensor.output"); assert.equal(next.entities.meters.pre, undefined);
  assert.equal(next.entities.gain, undefined); assert.equal(original.entities.meters.output, undefined);
  const same = applyEditorValues(original, { topic: "", id: "strip:0", tap: "incoming", label: "", floor: "-90", sensor: "sensor.replacement" });
  assert.equal(same.entities.meters.post_mute, "sensor.muted"); assert.equal(same.entities.gain, "number.gain");
});
