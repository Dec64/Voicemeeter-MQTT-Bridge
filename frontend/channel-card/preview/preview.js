import "../src/voicemeeter-channel-card.js";
import { SharedTelemetry } from "../src/shared-telemetry.js";
const incoming = document.querySelector("#incoming"), post = document.querySelector("#post"), output = document.querySelector("#output");
const incomingConfig = { source: { id: "strip:0", display_name: "Input example" }, controls: { gain: true, mute: true, solo: true, routing: true },
  entities: { gain: "number.fixture_gain", mute: "switch.fixture_mute", solo: "switch.fixture_solo", routes: { A1: "switch.fixture_a1", B1: "switch.fixture_b1" } } };
incoming.setConfig(incomingConfig);
post.setConfig({ source: { id: "strip:0", display_name: "Input example" }, meter: { mute_display_mode: "post_mute" } });
output.setConfig({ source: { id: "bus:5", display_name: "Bus example" } });
const editor = document.querySelector("#editor");
editor.setConfig(incomingConfig);
editor.addEventListener("config-changed", event => { incoming.setConfig(event.detail.config); sendFixture(); });
let sequence = 0;
const fixtureHa = { connection: { connected: true }, services: { number: { set_value: {} }, switch: { turn_on: {}, turn_off: {} } },
  states: { "number.fixture_gain": { state: "-6", attributes: { min: -60, max: 12, step: 0.1, unit_of_measurement: "dB" } },
    "switch.fixture_solo": { state: "off", attributes: {} },
    "switch.fixture_mute": { state: "off", attributes: {} }, "switch.fixture_a1": { state: "on", attributes: {} }, "switch.fixture_b1": { state: "off", attributes: {} } },
  async callService(domain, service, data) {
    if (!Object.hasOwn(this.states, data.entity_id) || !this.services[domain]?.[service]) throw new Error("Only local fixture controls are available.");
    await new Promise(resolve => setTimeout(resolve, 250));
    const state = domain === "number" ? String(data.value) : service === "turn_on" ? "on" : "off";
    this.states = { ...this.states, [data.entity_id]: { ...this.states[data.entity_id], state } };
    incoming.hass = { ...this }; sendFixture();
  } };
incoming.hass = { ...fixtureHa };
const fixtureConnection = {};
let deliverFixture = () => {};
const hub = new SharedTelemetry(async (_, __, callback) => {
  deliverFixture = callback;
  return () => { deliverFixture = () => {}; };
});
const leases = [incoming, post, output].map(card => hub.acquire(fixtureConnection, "fixture/v2/meters/fast", frame => card.setFrame(frame)));
function sendFixture() {
  const scenario = document.querySelector("#scenario").value;
  if (scenario === "stale" || document.hidden) return;
  const available = scenario !== "unavailable";
  const input = scenario === "silence" ? -90 : Number(document.querySelector("#input").value);
  const bus = scenario === "silence" ? -90 : Number(document.querySelector("#bus").value);
  const frame = { schema: 2, session_id: "local-fixture-session", seq: sequence++, published_at_utc: new Date().toISOString(), sample_window_ms: 50,
    sources: {
      "strip:0": { available, pre_dbfs: available ? input : null, post_mute_dbfs: available ? scenario === "muted" || incoming.ha?.states["switch.fixture_mute"].state === "on" ? -90 : input : null, active: available ? input > -90 : null, clipping: available ? input >= 0 : null },
      "bus:5": { available, output_dbfs: available ? bus : null, active: available ? bus > -90 : null, clipping: available ? bus >= 0 : null }
    } };
  deliverFixture(frame);
}
document.querySelector("#scenario").addEventListener("change", sendFixture);
document.querySelectorAll("input").forEach(input => input.addEventListener("input", sendFixture));
document.querySelector("#theme").addEventListener("click", event => {
  const light = document.body.classList.toggle("light");
  event.target.textContent = light ? "Dark theme" : "Light theme";
  event.target.setAttribute("aria-pressed", String(light));
});
await Promise.all(leases.map(lease => lease.ready));
sendFixture();
const timer = setInterval(sendFixture, 250);
addEventListener("pagehide", () => { clearInterval(timer); leases.forEach(lease => lease.release()); }, { once: true });
