import { normalizeControls, readControl, controlRequest, ROUTES } from "./control-model.js";
import { ADVANCED_GROUPS } from "./advanced-controls.js";
import { ControlCommands } from "./control-commands.js";

const commandLabels = { pending: "Waiting for HA readback…", error: "Command failed. Check HA and try again.", timeout: "Readback timed out. Check the reported state." };
export class ControlPanel {
  constructor(root, changed) {
    this.root = root; this.changed = changed; this.active = false;
    this.commands = new ControlCommands(changed);
    root.innerHTML = `<style>
      .vm-controls{margin-top:22px;border-top:1px solid var(--divider-color,#39434b);padding-top:18px}
      .vm-controls [hidden]{display:none}.vm-gain-head{display:flex;justify-content:space-between;gap:12px;font-size:13px}
      .vm-gain-inputs{display:flex;align-items:center;gap:12px;margin-top:8px}.vm-gain-range{min-width:0;flex:1;accent-color:#57cba0;min-height:44px}
      .vm-gain-number{width:82px;min-height:44px;background:var(--card-background-color,#20272d);color:inherit;border:1px solid var(--divider-color,#657681);border-radius:4px;padding:8px;font:inherit}
      .vm-controls input:focus-visible{outline:2px solid #57cba0;outline-offset:2px}.vm-control-note,.vm-draft,.vm-warning{font-size:12px;line-height:1.5;margin:6px 0 0;color:var(--secondary-text-color,#a9b7bf)}
      .vm-control-note[data-error=true],.vm-warning{color:var(--error-color,#ed7d67)}
      .vm-mute,.vm-solo{margin-top:12px}.vm-toggle{min-height:44px;padding:10px 16px;border:1px solid var(--divider-color,#657681);border-radius:6px;background:var(--card-background-color,#26323b);color:inherit;font:inherit;cursor:pointer}
      .vm-toggle[aria-pressed=true]{background:#275646;color:#edfff7;border-color:#57cba0}.vm-toggle:disabled{opacity:.6;cursor:default}.vm-toggle:focus-visible{outline:2px solid #57cba0;outline-offset:2px}
      .vm-routes{margin-top:16px}.vm-routes summary{min-height:44px;padding:12px 0;cursor:pointer;font-size:13px}.vm-route-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(110px,1fr));gap:12px}.vm-route .vm-toggle{width:100%}
    </style><section class="vm-controls" aria-label="Channel controls" hidden>
      <p class="vm-warning" role="status" hidden></p>
      <div class="vm-gain" hidden><div class="vm-gain-head"><span>Gain</span><output class="vm-gain-readback"></output></div>
      <div class="vm-gain-inputs"><input class="vm-gain-range" type="range" aria-label="Gain in dB"><input class="vm-gain-number" type="number" aria-label="Exact gain in dB"></div>
      <p class="vm-draft" hidden></p><p class="vm-control-note" role="status"></p></div>
      <div class="vm-mute" hidden><button class="vm-toggle" type="button" aria-label="Mute" data-control="mute">Mute</button><p class="vm-mute-note vm-control-note" role="status"></p></div>
      <div class="vm-solo" hidden><button class="vm-toggle" type="button" aria-label="Solo" data-control="solo">Solo</button><p class="vm-control-note" role="status"></p></div>
      <details class="vm-routes" hidden><summary>Routing</summary><div class="vm-route-grid"></div></details>
      <div class="vm-advanced"></div>
    </section>`;
    this.section = root.querySelector("section"); this.gain = root.querySelector(".vm-gain");
    this.inputs = [...root.querySelectorAll("input")];
    this.routes = new Map(); this.advancedRows = new Map();
    for (const route of ROUTES) {
      const row = document.createElement("div"); row.className = "vm-route"; row.hidden = true;
      const button = document.createElement("button"); button.className = "vm-toggle"; button.type = "button";
      button.dataset.control = `route:${route}`; button.setAttribute("aria-label", `Route to ${route}`);
      const note = document.createElement("p"); note.className = "vm-control-note"; note.setAttribute("role", "status");
      row.append(button, note); root.querySelector(".vm-route-grid").append(row); this.routes.set(`route:${route}`, row);
    }
    root.addEventListener("click", event => {
      const button = event.target.closest("button[data-control]");
      if (!this.active || !button || button.disabled) return;
      const key = button.dataset.control, binding = this.config.bindings.find(item => item.key === key);
      if (binding) { const view = readControl(binding, this.hass); if (view.available) this.commands.send(key, !view.value); }
    });
    for (const input of this.inputs) {
      input.addEventListener("input", () => {
        if (!this.active || input.disabled) return;
        this.gainError = null;
        this.draft = input.value.trim() === "" ? NaN : Number(input.value);
        this.showDraft();
      });
      input.addEventListener("change", () => {
        if (!this.active || input.disabled) return;
        const value = input.value.trim() === "" ? NaN : Number(input.value);
        const binding = this.config.bindings.find(item => item.key === "gain"), view = binding && readControl(binding, this.hass);
        this.gainError = !view || !controlRequest(view, value) ? "Use a valid gain within the displayed range and step." : null;
        this.draft = null;
        if (!this.gainError) this.commands.send("gain", value);
        this.changed();
      });
      input.addEventListener("keydown", event => {
        if (event.key === "Escape") {
          const binding = this.config.bindings.find(item => item.key === "gain");
          const view = binding && readControl(binding, this.hass);
          input.value = view?.available ? view.value : "";
          this.draft = null; this.gainError = null; this.changed(); input.blur();
        }
      });
    }
  }
  configure(config) {
    this.config = normalizeControls(config); this.commands.configure(this.config); this.draft = null; this.gainError = null;
    this.root.querySelector(".vm-advanced").replaceChildren(); this.advancedRows.clear();
    for (const group of ADVANCED_GROUPS) {
      const bindings = this.config.bindings.filter(b => b.group === group); if (!bindings.length) continue;
      const details = document.createElement("details"), summary = document.createElement("summary"), body = document.createElement("div");
      summary.textContent = group === "eq_cells" ? "Parametric EQ cells" : group; summary.style.cssText = "min-height:44px;padding:12px 0;cursor:pointer";
      details.append(summary, body); this.root.querySelector(".vm-advanced").append(details);
      const build = () => { if(body.childElementCount)return;
        for(const binding of bindings) {
          const row = document.createElement("div");row.style.cssText="display:grid;gap:8px;margin:12px 0";
          const label=document.createElement("label");label.textContent=binding.label+(binding.unit?` (${binding.unit})`:"");
          const note=document.createElement("p");note.className="vm-control-note";note.setAttribute("role","status");
          if(binding.domain==='switch') {const button=document.createElement("button");button.className='vm-toggle';button.type='button';button.dataset.control=binding.key;button.textContent=binding.label;row.append(button);}
          else {const input=document.createElement("input");input.className='vm-gain-number';input.type='number';input.setAttribute('aria-label',binding.label);label.append(input);
            input.addEventListener('change',()=>{if(!this.active || input.disabled)return;const value=input.value.trim()===''?NaN:Number(input.value);const view=readControl(binding,this.hass);if(controlRequest(view,value))this.commands.send(binding.key,value);else{note.textContent='Use the displayed range and step.';note.dataset.error='true';}}); row.append(label);}
          row.append(note);body.append(row);this.advancedRows.set(binding.key,{row,binding});
        } this.changed();
      };
      details.addEventListener('toggle',()=>{if(details.open)build();});
    }
  }
  setHass(hass) { this.hass = hass; this.commands.update(hass); }
  setActive(active) { this.active = active; if (!active) { this.commands.dispose(); this.draft = null; } }
  showDraft() {
    const note = this.root.querySelector(".vm-draft"); note.hidden = this.draft === null || this.draft === undefined;
    note.textContent = Number.isFinite(this.draft) ? `Proposed ${this.draft.toFixed(1)} dB · release to send` : "Enter a valid gain.";
  }
  paint() {
    if (!this.active) return;
    const binding = this.config.bindings.find(item => item.key === "gain");
    const toggles = this.config.bindings.filter(item => ["mute", "solo"].includes(item.key));
    const routes = this.config.bindings.filter(item => item.key.startsWith("route:"));
    this.section.hidden = !binding && !toggles.length && !routes.length && !this.config.warnings.length && !this.config.bindings.some(b=>b.advanced);
    for (const {row,binding:item} of this.advancedRows.values()) { if(item.domain==='switch')this.paintSwitch(row,item);else {const view=readControl(item,this.hass),command=this.commands.status(item.key),input=row.querySelector('input');input.disabled=!view.available||command.busy;if(view.available){input.min=view.min;input.max=view.max;input.step=view.step;if(this.root.getRootNode().activeElement!==input)input.value=view.value;}row.querySelector('p').textContent=!view.available?'HA control unavailable.':commandLabels[command.phase]??'HA readback';} }
    const warning = this.root.querySelector(".vm-warning"); warning.hidden = !this.config.warnings.length;
    warning.textContent = this.config.warnings.join(" "); this.gain.hidden = !binding;
    for (const key of ["mute", "solo"]) {
      const toggle = toggles.find(item => item.key === key), row = this.root.querySelector(`.vm-${key}`);
      row.hidden = !toggle; if (toggle) this.paintSwitch(row, toggle);
    }
    this.root.querySelector(".vm-routes").hidden = !routes.length;
    for (const [key, row] of this.routes) {
      const route = routes.find(item => item.key === key); row.hidden = !route;
      if (route) this.paintSwitch(row, route);
    }
    if (!binding) return;
    const view = readControl(binding, this.hass), command = this.commands.status("gain");
    if (!view.available) this.draft = null;
    this.root.querySelector(".vm-gain-readback").textContent = view.available ? `${view.value.toFixed(1)} dB` : "Unavailable";
    for (const input of this.inputs) {
      input.disabled = !view.available || command.busy;
      if (view.available) {
        input.min = view.min; input.max = view.max; input.step = view.step;
        if (this.draft === null || this.draft === undefined) input.value = view.value;
      }
    }
    const note = this.root.querySelector(".vm-control-note");
    note.textContent = !view.available ? "Check the gain entity, dB range and HA services." : this.gainError ?? commandLabels[command.phase] ?? "HA readback · commands send on release";
    note.dataset.error = String(!!this.gainError || ["error", "timeout"].includes(command.phase)); this.showDraft();
  }
  paintSwitch(root, binding) {
    const view = readControl(binding, this.hass), command = this.commands.status(binding.key), button = root.querySelector("button");
    button.disabled = !view.available || command.busy;
    button.textContent = `${binding.label} · ${view.available ? view.value ? "on" : "off" : "unavailable"}`;
    if (view.available) button.setAttribute("aria-pressed", String(view.value)); else button.removeAttribute("aria-pressed");
    const note = root.querySelector(".vm-control-note");
    note.textContent = !view.available ? "HA control unavailable." : commandLabels[command.phase] ?? "HA readback";
    note.dataset.error = String(["error", "timeout"].includes(command.phase));
  }
}
