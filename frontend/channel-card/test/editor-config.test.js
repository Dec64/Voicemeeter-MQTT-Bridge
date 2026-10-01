import test from "node:test";
import assert from "node:assert/strict";
import { applyEditorValues } from "../src/editor-config.js";
const values = { topic: "example/pc", id: "strip:0", label: "Input", tap: "post_mute", floor: "-90" };
test("editor preserves unrelated config without mutating the input", () => {
  const config = { source: { id: "strip:1", custom: true }, controls: { mute: true }, bridge: { transport: "auto" }, meter: { history_seconds: 5 } };
  const result = applyEditorValues(config, values);
  assert.equal(result.source.id, "strip:0"); assert.equal(config.source.id, "strip:1");
  assert.equal(result.source.custom, true); assert.deepEqual(result.controls, { mute: true });
  assert.equal(result.bridge.transport, "auto"); assert.equal(result.meter.history_seconds, 5);
});
test("switching to a bus removes only the incompatible input tap", () => {
  const result = applyEditorValues({ meter: { mute_display_mode: "post_mute", history_seconds: 3 } }, { ...values, id: "bus:7" });
  assert.equal(result.meter.mute_display_mode, undefined); assert.equal(result.meter.history_seconds, 3);
});
test("invalid topic and floor are rejected; empty source remains unassigned", () => {
  for (const topic of ["bad/#", "bad/+", "bad\0", " leading"]) assert.throws(() => applyEditorValues({}, { ...values, topic }));
  for (const floor of ["", "NaN", "-121", "0"]) assert.throws(() => applyEditorValues({}, { ...values, floor }));
  assert.equal(applyEditorValues({}, { ...values, id: "" }).source.id, "");
});
