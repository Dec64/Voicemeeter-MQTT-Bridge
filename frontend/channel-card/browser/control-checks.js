import "../src/voicemeeter-channel-card.js";
const assert = (value, message) => { if (!value) throw new Error(message); };
const wait = async predicate => {
  for (let i = 0; i < 150; i++) { if (predicate()) return; await new Promise(resolve => setTimeout(resolve, 20)); }
  throw new Error("Timed out waiting for control state");
};
const checks = [], calls = [], stage = document.querySelector("#stage");
const card = document.createElement("voicemeeter-channel-card");
let reject = false;
const hass = { connection: { connected: true }, services: { number: { set_value: {} }, switch: { turn_on: {}, turn_off: {} } },
  async callService(...args) { calls.push(args); if (reject) throw new Error("private mock rejection"); },
  states: { "number.input_gain": { state: "-6", attributes: { min: -60, max: 12, step: 0.1, unit_of_measurement: "dB" } } } };
const config = { bridge: { transport: "entities_only" }, source: { id: "strip:0" }, controls: { gain: true }, entities: { gain: "number.input_gain" } };
const state = (entity, value) => { hass.states = { ...hass.states, [entity]: { ...hass.states[entity], state: value } }; card.hass = { ...hass }; };
try {
  card.setConfig(config); card.hass = hass; stage.append(card);
  const root = card.shadowRoot, slider = root.querySelector(".vm-gain-range"), readback = root.querySelector(".vm-gain-readback");
  await wait(() => readback.textContent === "-6.0 dB");
  for (const value of [-10, -11, -12]) { slider.value = value; slider.dispatchEvent(new Event("input", { bubbles: true })); }
  assert(calls.length === 0, "gain drag sent commands"); assert(readback.textContent === "-6.0 dB", "gain draft replaced readback");
  slider.dispatchEvent(new Event("change", { bubbles: true }));
  await wait(() => calls.length === 1 && slider.disabled);
  assert(JSON.stringify(calls[0]) === JSON.stringify(["number", "set_value", { entity_id: "number.input_gain", value: -12 }]), "incorrect gain request");
  assert(readback.textContent === "-6.0 dB", "service ACK replaced readback");
  state("number.input_gain", "-12"); await wait(() => readback.textContent === "-12.0 dB" && !slider.disabled);
  checks.push("gain drag sends once on release and waits for actual HA readback");
  reject = true; slider.value = -10; slider.dispatchEvent(new Event("change", { bubbles: true }));
  await wait(() => root.querySelector(".vm-control-note").textContent.includes("failed"));
  assert(readback.textContent === "-12.0 dB", "failed command changed value");
  assert(!root.textContent.includes("private mock"), "raw error leaked");
  state("number.input_gain", "unavailable"); await wait(() => slider.disabled && readback.textContent === "Unavailable");
  checks.push("failed/unavailable gain retains truthful readback and safe error text");
  card.remove();
  window.controlCheckResult = { passed: true, checks }; document.querySelector("#result").textContent = `PASS (${checks.length} scenarios)\n${checks.join("\n")}`;
} catch (error) {
  card.remove(); window.controlCheckResult = { passed: false, error: error.message };
  document.querySelector("#result").textContent = `FAIL: ${error.message}`; console.error(error);
}
