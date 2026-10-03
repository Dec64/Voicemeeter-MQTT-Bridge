import test from "node:test";
import assert from "node:assert/strict";
import { MeterModel, normalizeConfig } from "../src/meter-model.js";

const config = (id = "strip:0", tap) => ({ source: { id }, ...(tap ? { meter: { mute_display_mode: tap } } : {}) });
const frame = (seq = 0, sources = { "strip:0": { available: true, pre_dbfs: -18, post_mute_dbfs: -90 }, "bus:5": { available: true, output_dbfs: -32 } }) =>
  ({ schema: 2, session_id: "fixture", seq, sources });

test("diagnostics is explicit and off by default", () => {
  assert.equal(normalizeConfig({}).diagnostics, false);
  assert.equal(normalizeConfig({ diagnostics: true }).diagnostics, true);
  assert.throws(() => normalizeConfig({ diagnostics: "true" }));
});

test("orientation and density are independent validated choices", () => {
  for (const orientation of ["horizontal", "vertical"]) for (const variant of ["compact", "standard", "expanded"]) {
    const normalized = normalizeConfig({ meter: { orientation }, appearance: { variant } });
    assert.equal(normalized.orientation, orientation); assert.equal(normalized.variant, variant);
  }
  assert.throws(() => normalizeConfig({ meter: { orientation: "diagonal" } }));
  assert.throws(() => normalizeConfig({ appearance: { variant: "huge" } }));
});

test("all sixteen canonical sources are selectable; default selects none", () => {
  for (const kind of ["strip", "bus"]) for (let i = 0; i < 8; i++) assert.equal(normalizeConfig(config(`${kind}:${i}`)).id, `${kind}:${i}`);
  assert.equal(new MeterModel({}).view(0).state, "unconfigured");
});
test("same-source cards retain independent tap choice and another source stays isolated", () => {
  const models = [new MeterModel(config()), new MeterModel(config("strip:0", "post_mute")), new MeterModel(config("bus:5"))];
  models.forEach(model => assert.equal(model.accept(frame(), 0), true));
  assert.deepEqual(models.map(model => model.view(0).level), [-18, -90, -32]);
  assert.equal(models[1].view(0).state, "silence");
});
test("silence has zero fill while stale and unavailable carry no value", () => {
  const model = new MeterModel(config("strip:0", "post_mute"));
  model.accept(frame(), 10);
  assert.equal(model.view(759).state, "silence"); assert.equal(model.view(759).fill, 0);
  assert.equal(model.view(760).state, "stale"); assert.equal(model.view(760).level, null);
  model.accept(frame(1, { "strip:0": { available: false, post_mute_dbfs: 0 } }), 800);
  assert.equal(model.view(800).state, "unavailable"); assert.equal(model.view(800).level, null);
});
test("missing selected source and missing tap never borrow another level", () => {
  const model = new MeterModel(config());
  model.accept(frame(0, { "bus:5": { available: true, output_dbfs: -1 } }), 0);
  assert.equal(model.view(0).state, "unavailable");
  model.accept(frame(1, { "strip:0": { available: true, post_mute_dbfs: -1 } }), 1);
  assert.equal(model.view(1).state, "unavailable");
});
test("malformed envelopes do not refresh freshness or sequence", () => {
  const model = new MeterModel(config()); model.accept(frame(), 0);
  for (const bad of [null, {}, { ...frame(1), schema: 1 }, { ...frame(1), sources: [] }, { ...frame(1), seq: 1.1 }, { ...frame(1), seq: Number.MAX_SAFE_INTEGER + 1 }])
    assert.equal(model.accept(bad, 700), false);
  assert.equal(model.view(750).state, "stale");
});
test("duplicate, old and different-session frames cannot refresh a card", () => {
  const model = new MeterModel(config()); model.accept(frame(3), 0);
  for (const value of [frame(3), frame(2), { ...frame(4), session_id: "other" }]) assert.equal(model.accept(value, 700), false);
  assert.equal(model.view(750).state, "stale");
  model.reset(); assert.equal(model.accept({ ...frame(), session_id: "other" }, 800), true);
});
for (const value of [null, "-18", true, NaN, Infinity, -Infinity, -201, 61]) {
  test(`invalid numeric level ${String(value)} is unavailable`, () => {
    const model = new MeterModel(config()); model.accept(frame(0, { "strip:0": { available: true, pre_dbfs: value } }), 0);
    assert.equal(model.view(0).state, "unavailable"); assert.equal(model.view(0).fill, 0);
  });
}
test("valid levels clamp visual fill while keeping numeric over-range reading", () => {
  const model = new MeterModel(config()); model.accept(frame(0, { "strip:0": { available: true, pre_dbfs: 3 } }), 0);
  assert.equal(model.view(0).fill, 93 / 102); assert.equal(model.view(0).level, 3);
  model.accept(frame(1, { "strip:0": { available: true, pre_dbfs: -100 } }), 1);
  assert.equal(model.view(1).fill, 0);
});
test("the configurable ceiling maps real positive peaks into a red headroom region", () => {
  const model = new MeterModel({source:{id:'strip:0'},meter:{floor_dbfs:-60,ceiling_dbfs:12}});
  model.accept(frame(0, {'strip:0':{available:true,pre_dbfs:0}}),0);
  assert.equal(model.view(0).fill,60/72);
  model.accept(frame(1, {'strip:0':{available:true,pre_dbfs:6}}),1);
  assert.equal(model.view(1).fill,66/72); assert.equal(model.view(1).level,6);
  model.accept(frame(2, {'strip:0':{available:true,pre_dbfs:20}}),2);
  assert.equal(model.view(2).fill,1); assert.equal(model.view(2).level,20);
  assert.equal(normalizeConfig({meter:{ceiling_dbfs:0}}).ceiling,0);
  for(const ceiling of [-1,25,NaN,'12'])assert.throws(()=>normalizeConfig({meter:{ceiling_dbfs:ceiling}}));
});
test("meter visibility and palette choices are explicit and copied", () => {
  const raw={meter:{show_peak_value:false,show_scale:false,show_status:false,show_peak_hold:false,show_clip:false,colors:{normal:'#00ff66'}},appearance:{show_source_id:true,show_tap:true,colors:{accent:'#44ff88'}}};
  const c=normalizeConfig(raw);assert.equal(c.showPeakValue,false);assert.equal(c.showScale,false);assert.equal(c.showStatus,false);assert.equal(c.colors.normal,'#00ff66');assert.equal(c.showSourceId,true);
  raw.meter.colors.normal='#ffffff';assert.equal(c.colors.normal,'#00ff66');
  assert.throws(()=>normalizeConfig({meter:{show_peak_value:'false'}}));
  assert.throws(()=>normalizeConfig({meter:{colors:{normal:'red;display:none'}}}));
});
test("configuration rejects invalid IDs, invented bus taps and nonfinite floors", () => {
  for (const id of ["strip:8", "strip:01", "guest", "bus:-1", 0]) assert.throws(() => normalizeConfig(config(id)));
  assert.throws(() => normalizeConfig(config("bus:5", "incoming")));
  assert.throws(() => normalizeConfig(config("strip:0", "output")));
  assert.throws(() => normalizeConfig({ meter: { floor_dbfs: NaN } }));
});
test("configuration is copied and labels remain inert text", () => {
  assert.equal(new MeterModel({source:{id:'strip:0',display_name:''}}).view(0).label,'strip:0');
  const input = { source: { id: "strip:0", display_name: "<img onerror=alert(1)>" } };
  const model = new MeterModel(input); input.source.id = "bus:0";
  assert.equal(model.view(0).id, "strip:0"); assert.equal(model.view(0).label, "<img onerror=alert(1)>");
});
test("invalid or backwards receipt clocks never create freshness", () => {
  const model = new MeterModel(config()); assert.equal(model.accept(frame(), NaN), false);
  model.accept(frame(), 100); assert.equal(model.accept(frame(1), 99), false);
  assert.equal(model.view(99).state, "stale");
});
