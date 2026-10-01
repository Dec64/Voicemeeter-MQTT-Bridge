import { applyEditorValues } from "./editor-config.js";
import { normalizeConfig } from "./meter-model.js";

export class VoicemeeterChannelCardEditor extends HTMLElement {
  constructor() {
    super(); this.attachShadow({ mode: "open" });
    this.shadowRoot.innerHTML = `<style>
      :host{display:block;color:var(--primary-text-color,inherit);font:14px "Segoe UI",sans-serif}*{box-sizing:border-box}
      form{display:grid;gap:18px;max-width:620px;padding:20px 0}fieldset{border:1px solid var(--divider-color,#51616c);border-radius:8px;padding:18px;display:grid;gap:16px}legend{padding:0 8px;font-weight:600}
      label{display:grid;gap:7px}input,select{width:100%;min-height:44px;border:1px solid var(--divider-color,#657681);border-radius:4px;background:var(--card-background-color,#26323b);color:var(--primary-text-color,#e9edf1);padding:9px;font:inherit}input:focus-visible,select:focus-visible{outline:2px solid #57cba0;outline-offset:2px}p{margin:0;font-size:12px;line-height:1.5}.error{color:var(--error-color,#ed7d67)}
    </style><form><fieldset><legend>Source</legend>
    <label>Bridge MQTT base topic<input name="topic" autocomplete="off" placeholder="voicemeeter/my-pc"></label>
    <label>Transport<select name="transport"><option value="auto">Auto · prefer native HA</option><option value="native_ws">Native HA stream</option><option value="entities_only">Sensors only</option></select></label>
    <label>Canonical source<select name="id"><option value="">Choose a source</option></select></label>
    <label>Display name<input name="label" maxlength="511"></label>
    <p>Manual selection. Device metadata and entity suggestions are not connected yet.</p></fieldset>
    <fieldset><legend>Peak meter</legend><label>Input meter tap<select name="tap"><option value="incoming">Incoming · pre-fader</option><option value="post_mute">After mute</option></select></label>
    <label>Display floor · dBFS<input name="floor" type="number" min="-120" max="-20" step="1"></label><p class="bus-note"></p></fieldset>
    <fieldset><legend>Layout</legend><label>Orientation<select name="orientation"><option value="horizontal">Horizontal</option><option value="vertical">Vertical</option></select></label>
    <label>Density<select name="variant"><option value="compact">Compact</option><option value="standard">Standard</option><option value="expanded">Expanded</option></select></label></fieldset>
    <p class="error" role="alert"></p><p class="pending"></p></form>`;
    this.form = this.shadowRoot.querySelector("form");
    for (const kind of ["strip", "bus"]) for (let index = 0; index < 8; index++) {
      const option = document.createElement("option"); option.value = `${kind}:${index}`; option.textContent = `${kind}:${index}`;
      this.form.elements.id.append(option);
    }
    this.form.addEventListener("submit", event => event.preventDefault());
    this.form.addEventListener("change", () => this.updateConfig());
    this.setConfig({});
  }
  setConfig(config) {
    this.config = structuredClone(config);
    const fields = this.form.elements;
    fields.topic.value = config.bridge?.base_topic ?? "";
    fields.transport.value = config.bridge?.transport ?? "auto";
    const id = config.source?.id ?? "";
    // Keep an invalid manually supplied value visible so it can be corrected.
    this.form.querySelectorAll("option[data-invalid]").forEach(option => option.remove());
    if (![...fields.id.options].some(option => option.value === id)) {
      const option = document.createElement("option"); option.value = id; option.textContent = `Invalid: ${id}`; option.dataset.invalid = "true";
      fields.id.append(option);
    }
    fields.id.value = id; fields.label.value = config.source?.display_name ?? "";
    fields.tap.value = config.meter?.mute_display_mode ?? "incoming";
    fields.floor.value = config.meter?.floor_dbfs ?? -90;
    fields.orientation.value = config.meter?.orientation ?? "horizontal";
    fields.variant.value = config.appearance?.variant ?? "standard";
    this.updateHints();
    try { normalizeConfig(config); this.shadowRoot.querySelector(".error").textContent = ""; }
    catch (error) { this.shadowRoot.querySelector(".error").textContent = error.message; }
  }
  updateHints() {
    const bus = this.form.elements.id.value.startsWith("bus:");
    this.form.elements.tap.disabled = bus;
    this.shadowRoot.querySelector(".bus-note").textContent = bus ? "Bus meters use measured output only." : "Tap choice changes this card only.";
    this.shadowRoot.querySelector(".pending").textContent = Object.values(this.config.controls ?? {}).some(Boolean)
      ? "Control settings are preserved, but controls are not implemented in this preview." : "";
  }
  updateConfig() {
    const fields = this.form.elements;
    try {
      const next = applyEditorValues(this.config, { topic: fields.topic.value, transport: fields.transport.value, id: fields.id.value,
        label: fields.label.value, tap: fields.tap.value, floor: fields.floor.value,
        orientation: fields.orientation.value, variant: fields.variant.value });
      this.config = next;
      this.shadowRoot.querySelector(".error").textContent = "";
      this.updateHints();
      this.dispatchEvent(new CustomEvent("config-changed", { detail: { config: structuredClone(next) }, bubbles: true, composed: true }));
    } catch (error) { this.shadowRoot.querySelector(".error").textContent = error.message; }
  }
}
if (!customElements.get("voicemeeter-channel-card-editor")) customElements.define("voicemeeter-channel-card-editor", VoicemeeterChannelCardEditor);
