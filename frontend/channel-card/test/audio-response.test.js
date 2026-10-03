import test from "node:test";
import assert from "node:assert/strict";
import {
  eqResponse,
  compressorOutput,
  quantize,
  frequencyPosition,
  positionFrequency,
} from "../src/audio-response.js";
import { normalizeConfig } from "../src/meter-model.js";

test("meter-only presentation and minimal names are independent of density", () => {
  const view = normalizeConfig({
    appearance: {
      presentation: "meter",
      name_style: "minimal",
      variant: "expanded",
    },
  });
  assert.equal(view.presentation, "meter");
  assert.equal(view.nameStyle, "minimal");
  assert.throws(() => normalizeConfig({ appearance: { name_style: "wrong" } }));
});
test("EQ graph has real bell boost, bypass, cuts and shelf direction", () => {
  const band = { on: true, type: 0, f: 1000, gain: 6, q: 2 };
  assert.ok(Math.abs(eqResponse([band], 1000) - 6) < 1e-6);
  assert.equal(eqResponse([{ ...band, on: false }], 1000), 0);
  assert.ok(Math.abs(eqResponse([{ ...band, gain: -6 }], 1000) + 6) < 1e-6);
  assert.ok(eqResponse([{ ...band, type: 4 }], 30) < -40);
  assert.ok(eqResponse([{ ...band, type: 3 }], 15000) < -30);
  assert.ok(eqResponse([{ ...band, type: 5 }], 30) > 5.9);
  assert.ok(eqResponse([{ ...band, type: 6 }], 15000) > 5.9);
});
test("frequency mapping is logarithmic and rotary values respect advertised steps", () => {
  assert.ok(Math.abs(frequencyPosition(200) - 1 / 3) < 1e-12);
  assert.ok(Math.abs(positionFrequency(2 / 3) - 2000) < 1e-9);
  assert.equal(quantize(-12.34, { min: -40, max: -3, step: 0.1 }), -12.3);
  assert.equal(quantize(100, { min: -40, max: -3, step: 0.1 }), -3);
});
test("compressor preview applies threshold, ratio, knee and gain", () => {
  assert.equal(compressorOutput(-30, { threshold: -20, ratio: 4 }), -30);
  assert.equal(compressorOutput(-4, { threshold: -20, ratio: 4 }), -16);
  assert.equal(
    compressorOutput(-30, { threshold: -20, ratio: 4, gainin: 2, gainout: 3 }),
    -25,
  );
  assert.ok(compressorOutput(-20, { threshold: -20, ratio: 4, knee: 1 }) < -20);
});
