import { applyEditorValues } from "./editor-config.js";
import { normalizeConfig } from "./meter-model.js";
import { normalizeControls, ROUTES } from "./control-model.js";

export class VoicemeeterChannelCardEditor extends HTMLElement {
  constructor() {
    super(); this.attachShadow({ mode: "open" });
    this.shadowRoot.innerHTML = `<style>
      :host{display:block;color:var(--primary-text-color,inherit);font:14px "Segoe UI",sans-serif}*{box-sizing:border-box}
      form{display:grid;gap:18px;max-width:620px;padding:20px 0}fieldset{border:1px solid var(--divider-color,#51616c);border-radius:8px;padding:18px;display:grid;gap:16px}legend{padding:0 8px;font-weight:600}
      label{display:grid;gap:7px}input,select{width:100%;min-height:44px;border:1px solid var(--divider-color,#657681);border-radius:4px;background:var(--card-background-color,#26323b);color:var(--primary-text-color,#e9edf1);padding:9px;font:inherit}input:focus-visible,select:focus-visible{outline:2px solid #57cba0;outline-offset:2px}p{margin:0;font-size:12px;line-height:1.5}.error{color:var(--error-color,#ed7d67)}
      .check{display:flex;align-items:center;gap:10px}.check input{width:20px;min-height:24px}.route-fields{display:grid;gap:14px}
    </style><form><fieldset><legend>Source</legend>
    <label>Bridge MQTT base topic<input name="topic" autocomplete="off" placeholder="voicemeeter/my-pc"></label>
    <label>Transport<select name="transport"><option value="auto">Auto · prefer native HA</option><option value="native_ws">Native HA stream</option><option value="entities_only">Sensors only</option></select></label>
    <label>Canonical source<select name="id"><option value="">Choose a source</option></select></label>
    <label>Display name<input name="label" maxlength="511"></label>
    <p>Manual selection. Changing source or bridge topic clears entity mappings to prevent readings or controls from targeting the previous source.</p></fieldset>
    <fieldset><legend>Peak meter</legend><label>Input meter tap<select name="tap"><option value="incoming">Incoming · pre-fader</option><option value="post_mute">After mute</option></select></label>
    <label>Display floor · dBFS<input name="floor" type="number" min="-120" max="-20" step="1"></label><p class="bus-note"></p></fieldset>
    <fieldset><legend>Slow sensor fallback</legend><label>Sensor for the selected meter tap<input name="sensor" placeholder="sensor.example_peak"></label>
    <p>Requires dBFS. Select a verified entity for this source and tap. Readings show reduced freshness and expire 15 seconds after the entity's last state update.</p></fieldset>
    <fieldset><legend>Core controls</legend><p>Choose verified entities belonging to this source. Names alone do not establish the source mapping.</p>
    <label class="check"><input name="showGain" type="checkbox">Show gain</label><label>Gain entity · number<input name="gainEntity" placeholder="number.example_gain"></label>
    <label class="check"><input name="showMute" type="checkbox">Show mute</label><label>Mute entity · switch<input name="muteEntity" placeholder="switch.example_mute"></label></fieldset>
    <fieldset><legend>Input routing</legend><label class="check"><input name="showRouting" type="checkbox">Show routing</label>
    <p>Leave individual routes blank to hide them. Bus cards do not expose strip routing.</p><div class="route-fields"></div></fieldset>
    <fieldset><legend>Layout</legend><label>Orientation<select name="orientation"><option value="horizontal">Horizontal</option><option value="vertical">Vertical</option></select></label>
    <label>Density<select name="variant"><option value="compact">Compact</option><option value="standard">Standard</option><option value="expanded">Expanded</option></select></label></fieldset>
    <p class="error" role="alert"></p><p class="pending"></p></form>`;
    this.form = this.shadowRoot.querySelector("form");
    for (const route of ROUTES) {
      const label = document.createElement("label"); label.textContent = `${route} switch entity`;
      const input = document.createElement("input"); input.name = `route${route}`; input.placeholder = `switch.example_${route.toLowerCase()}`;
      label.append(input); this.form.querySelector(".route-fields").append(label);
    }
    for (const kind of ["strip", "bus"]) for (let index = 0; index < 8; index++) {
      const option = document.createElement("option"); option.value = `${kind}:${index}`; option.textContent = `${kind}:${index}`;
      this.form.elements.id.append(option);
    }
    this.form.addEventListener("submit", event => event.preventDefault());
    this.form.addEventListener("change", event => {
      if (["id", "topic"].includes(event.target.name)) {
        for (const name of ["sensor", "gainEntity", "muteEntity", ...ROUTES.map(route => `route${route}`)]) this.form.elements[name].value = "";
      }
      else if (event.target.name === "tap") this.loadSensor();
      this.updateConfig();
    });
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
    fields.showGain.checked = config.controls?.gain === true; fields.gainEntity.value = config.entities?.gain ?? "";
    fields.showMute.checked = config.controls?.mute === true; fields.muteEntity.value = config.entities?.mute ?? "";
    fields.showRouting.checked = config.controls?.routing === true;
    for (const route of ROUTES) fields[`route${route}`].value = config.entities?.routes?.[route] ?? "";
    this.loadSensor();
    this.updateHints();
    try { normalizeConfig(config); normalizeControls(config); this.shadowRoot.querySelector(".error").textContent = ""; }
    catch (error) { this.shadowRoot.querySelector(".error").textContent = error.message; }
  }
  loadSensor() {
    const fields = this.form.elements;
    const tap = fields.id.value.startsWith("bus:") ? "output" : fields.tap.value === "post_mute" ? "post_mute" : "pre";
    fields.sensor.value = this.config.entities?.meters?.[tap] ?? "";
  }
  updateHints() {
    const bus = this.form.elements.id.value.startsWith("bus:");
    this.form.elements.tap.disabled = bus;
    this.shadowRoot.querySelector(".bus-note").textContent = bus ? "Bus meters use measured output only." : "Tap choice changes this card only.";
    this.form.elements.showRouting.disabled = bus;
    for (const route of ROUTES) this.form.elements[`route${route}`].disabled = bus;
    try { this.shadowRoot.querySelector(".pending").textContent = normalizeControls(this.config).warnings.join(" "); }
    catch { this.shadowRoot.querySelector(".pending").textContent = "Correct the control entity mappings before saving."; }
  }
  updateConfig() {
    const fields = this.form.elements;
    try {
      const next = applyEditorValues(this.config, { topic: fields.topic.value, transport: fields.transport.value, id: fields.id.value,
        label: fields.label.value, tap: fields.tap.value, floor: fields.floor.value,
        orientation: fields.orientation.value, variant: fields.variant.value, sensor: fields.sensor.value,
        controlSettings: { gain: { visible: fields.showGain.checked, entity: fields.gainEntity.value },
          mute: { visible: fields.showMute.checked, entity: fields.muteEntity.value },
          routing: { visible: fields.showRouting.checked, entities: Object.fromEntries(ROUTES.map(route => [route, fields[`route${route}`].value])) } } });
      this.config = next;
      this.shadowRoot.querySelector(".error").textContent = "";
      this.updateHints();
      this.dispatchEvent(new CustomEvent("config-changed", { detail: { config: structuredClone(next) }, bubbles: true, composed: true }));
    } catch (error) { this.shadowRoot.querySelector(".error").textContent = error.message; }
  }
}
if (!customElements.get("voicemeeter-channel-card-editor")) customElements.define("voicemeeter-channel-card-editor", VoicemeeterChannelCardEditor);
