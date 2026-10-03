import "../src/voicemeeter-channel-card.js";
import { describeAdvanced } from "../src/advanced-controls.js";
const cards = [...document.querySelectorAll("voicemeeter-channel-card")],
  states = {},
  advanced = {};
const defaults = {
  comp: 3,
  comp_gainin: 0,
  comp_ratio: 4,
  comp_threshold: -18,
  comp_attack: 8,
  comp_release: 250,
  comp_knee: 0.5,
  comp_gainout: 2,
  comp_makeup: false,
  gate: 2,
  gate_threshold: -40,
  gate_damping: -40,
  gate_bpsidechain: 200,
  gate_attack: 5,
  gate_hold: 150,
  gate_release: 180,
  denoiser: 3,
  denoiser_threshold: 2,
  eq_on: true,
  eq_ab: false,
};
function add(suffix, value) {
  const id = `strip_0_${suffix}`,
    descriptor = describeAdvanced(id, "strip:0"),
    entity = `${descriptor.domain}.preview_${suffix}`;
  advanced[id] = entity;
  states[entity] = {
    state:
      descriptor.domain === "switch" ? (value ? "on" : "off") : String(value),
    attributes:
      descriptor.domain === "number"
        ? {
            min: descriptor.min,
            max: descriptor.max,
            step: descriptor.step,
            ...(descriptor.unit
              ? { unit_of_measurement: descriptor.unit }
              : {}),
          }
        : {},
  };
}
for (const [suffix, value] of Object.entries(defaults)) add(suffix, value);
for (let channel = 0; channel < 2; channel++)
  for (let cell = 0; cell < 6; cell++)
    for (const [prop, value] of Object.entries({
      on: true,
      type: cell === 0 ? 4 : cell === 5 ? 6 : 0,
      f: [75, 200, 650, 2200, 6000, 12000][cell],
      gain: [0, -3, 2.5, 4, -2, 1.5][cell],
      q: cell === 0 ? 1 : 2,
    }))
      add(`eq_channel_${channel}_cell_${cell}_${prop}`, value);
states["number.preview_gain"] = {
  state: "-6",
  attributes: { min: -60, max: 12, step: 0.1, unit_of_measurement: "dB" },
};
states["switch.preview_mute"] = { state: "off", attributes: {} };
const hass = {
  connection: { connected: true },
  services: {
    number: { set_value: {} },
    switch: { turn_on: {}, turn_off: {} },
  },
  states,
  async callService(domain, service, data) {
    await new Promise((resolve) => setTimeout(resolve, 120));
    states[data.entity_id].state =
      domain === "number"
        ? String(data.value)
        : service === "turn_on"
          ? "on"
          : "off";
    for (const card of cards) card.hass = { ...hass, states: { ...states } };
  },
};
const config = {
  bridge: { transport: "entities_only" },
  source: { id: "strip:0", display_name: "Main Mic" },
  controls: {
    gain: true,
    mute: true,
    compressor: true,
    gate: true,
    denoiser: true,
    eq: true,
    eq_cells: true,
  },
  entities: {
    gain: "number.preview_gain",
    mute: "switch.preview_mute",
    advanced,
  },
};
cards[0].setConfig({ ...config, appearance: { variant: "expanded" } });
cards[1].setConfig({ ...config, appearance: { variant: "standard" } });
cards[2].setConfig({
  ...config,
  source: { id: "strip:0", display_name: "MIC" },
  appearance: { presentation: "meter", name_style: "minimal" },
  meter: { show_peak_value: false, show_status: false },
});
cards[3].setConfig({
  ...config,
  appearance: { presentation: "meter", name_style: "hidden" },
  meter: { show_peak_value: false, show_status: false, show_scale: false },
});
for (const card of cards) card.hass = { ...hass };
let seq = 0;
setInterval(() => {
  const level = -22 + Math.sin(seq / 4) * 8;
  for (const card of cards)
    card.setFrame({
      schema: 2,
      session_id: "preview",
      seq: ++seq,
      sources: {
        "strip:0": {
          available: true,
          pre_dbfs: level,
          active: true,
          clipping: false,
        },
      },
    });
}, 100);
document.querySelector("#theme").addEventListener("click", () => {
  const light = document.body.classList.toggle("light");
  document.querySelector("#theme").textContent = light
    ? "Dark theme"
    : "Light theme";
});
