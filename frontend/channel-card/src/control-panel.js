import {
  normalizeControls,
  readControl,
  controlRequest,
  ROUTES,
} from "./control-model.js";
import { ADVANCED_GROUPS } from "./advanced-controls.js";
import { ControlCommands } from "./control-commands.js";

const commandLabels = {
  pending: "Applying; waiting for the mixer state.",
  error: "Command failed. Check HA and try again.",
  timeout: "Readback timed out. Check the reported state.",
};
const groupLabels = {
  mono: "Mono",
  compressor: "Compressor",
  gate: "Gate",
  denoiser: "Denoiser",
  eq: "Equalizer",
  eq_cells: "Parametric EQ",
};
const styles = `
.vm-controls{margin-top:18px;padding-top:16px;border-top:1px solid var(--divider-color,#39434b);font-size:13px}.vm-controls [hidden]{display:none!important}
.vm-gain-head{display:flex;justify-content:space-between;align-items:center;gap:12px;font-size:11px;letter-spacing:.08em;text-transform:uppercase}.vm-gain-readback{font:13px Consolas,monospace;letter-spacing:0;text-transform:none}.vm-gain-inputs{display:flex;align-items:center;gap:12px}
.vm-gain-range,.vm-param-range{min-width:0;flex:1;height:44px;accent-color:var(--vm-accent,#78ff8e);cursor:pointer}.vm-gain-number,.vm-param-number,.vm-controls select{min-height:44px;padding:8px 10px;background:var(--secondary-background-color,#18211d);color:inherit;border:1px solid var(--divider-color,#455248);border-radius:5px;font:13px Consolas,monospace;width:82px}
.vm-controls input:focus-visible,.vm-controls select:focus-visible,.vm-controls button:focus-visible,.vm-controls summary:focus-visible{outline:2px solid var(--vm-accent,#78ff8e);outline-offset:3px}
.vm-control-note,.vm-draft,.vm-warning{font-size:11px;line-height:1.45;margin:8px 0;color:var(--secondary-text-color,#a9b7bf)}.vm-control-note[data-error=true],.vm-warning{color:var(--error-color,#ff756b)}
.vm-button-bank{display:flex;gap:8px;flex-wrap:wrap;margin-top:10px}.vm-toggle{min-height:44px;min-width:64px;padding:10px 13px;border:1px solid var(--divider-color,#455248);border-radius:5px;background:var(--secondary-background-color,#19211c);color:var(--secondary-text-color,#abb8ad);font:600 11px "Segoe UI",sans-serif;letter-spacing:.05em;text-transform:uppercase;cursor:pointer;box-shadow:inset 0 1px 0 #ffffff08;position:relative}
.vm-toggle[aria-pressed=true]{color:var(--vm-accent,#78ff8e);border-color:currentColor;background:color-mix(in srgb,var(--vm-accent,#78ff8e) 12%,var(--secondary-background-color,#18211d));box-shadow:inset 0 -2px 0 currentColor,0 0 8px color-mix(in srgb,var(--vm-accent,#78ff8e) 18%,transparent)}
.vm-mute .vm-toggle[aria-pressed=true]{color:var(--vm-mute,#ff756b);background:color-mix(in srgb,var(--vm-mute,#ff756b) 12%,var(--secondary-background-color,#18211d))}.vm-solo .vm-toggle[aria-pressed=true]{color:var(--vm-solo,#ffd466);background:color-mix(in srgb,var(--vm-solo,#ffd466) 12%,var(--secondary-background-color,#18211d))}
.vm-toggle:disabled,.vm-controls input:disabled,.vm-controls select:disabled{opacity:.4;cursor:default}.vm-toggle[data-pending=true]::after{content:'…';position:absolute;right:3px;top:1px;font-size:14px}.vm-toggle[data-error=true]{border-color:var(--error-color,#ff756b)}
.vm-routes,.vm-processing{margin-top:10px;border:1px solid var(--divider-color,#394b3e);border-radius:6px;overflow:hidden}.vm-controls summary{padding:13px 12px;min-height:44px;font-size:11px;font-weight:650;letter-spacing:.07em;cursor:pointer;text-transform:uppercase;display:flex;align-items:center;justify-content:space-between;gap:8px;list-style:none}.vm-controls summary::-webkit-details-marker{display:none}.vm-controls summary::after{content:'+';font:18px Consolas,monospace;color:var(--vm-accent,#78ff8e)}.vm-controls details[open]>summary::after{content:'−'}
.vm-route-grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:6px;padding:0 10px 10px}.vm-route .vm-toggle{width:100%;min-width:0}.vm-process-body{display:grid;gap:12px;padding:0 12px 12px}.vm-param-row{display:grid;gap:6px;min-width:0}.vm-param-label{font-size:11px;color:var(--secondary-text-color,#a9b7bf)}.vm-param-inputs{display:flex;gap:8px;align-items:center}.vm-param-number{flex:1;width:100%;min-width:0}.vm-param-row select{width:100%;font:inherit}.vm-param-unit{font:11px Consolas,monospace;color:var(--secondary-text-color,#a9b7bf)}.vm-memory{display:flex;gap:4px}.vm-memory .vm-toggle{flex:1}
.vm-eq-toolbar{display:flex;align-items:center;gap:10px;padding:0 12px 12px;font-size:11px}.vm-eq-toolbar select{width:auto;flex:1;font:inherit}.vm-eq-cell{border:1px solid var(--divider-color,#394b3e);border-radius:5px;margin:0 10px 8px}.vm-eq-cell>.vm-process-body{grid-template-columns:repeat(2,minmax(0,1fr))}.vm-eq-cell .vm-param-range{display:none}.vm-eq-cell .vm-param-row:first-child,.vm-eq-cell .vm-param-row:nth-child(2){grid-column:1/-1}.vm-eq-cell summary{padding:10px 12px}
`;

export class ControlPanel {
  constructor(root, changed) {
    this.root = root;
    this.changed = changed;
    this.active = false;
    this.commands = new ControlCommands(changed);
    root.innerHTML = `<style>${styles}</style><section class="vm-controls" aria-label="Channel controls" hidden>
      <p class="vm-warning" role="status" hidden></p><div class="vm-gain" hidden><div class="vm-gain-head"><span>Gain</span><output class="vm-gain-readback" title="Actual mixer gain"></output></div>
      <div class="vm-gain-inputs"><input class="vm-gain-range" type="range" aria-label="Gain in dB"><input class="vm-gain-number" type="number" aria-label="Exact gain in dB"></div><p class="vm-draft" hidden></p><p class="vm-control-note" role="status" hidden></p></div>
      <div class="vm-button-bank"><div class="vm-mute" hidden><button class="vm-toggle" type="button" aria-label="Mute" data-control="mute">Mute</button><p class="vm-mute-note vm-control-note" role="status" hidden></p></div>
      <div class="vm-solo" hidden><button class="vm-toggle" type="button" aria-label="Solo" data-control="solo">Solo</button><p class="vm-control-note" role="status" hidden></p></div><div class="vm-mono" hidden></div></div>
      <details class="vm-routes" hidden><summary>Routing</summary><div class="vm-route-grid"></div></details><div class="vm-advanced"></div></section>`;
    this.section = root.querySelector("section");
    this.gain = root.querySelector(".vm-gain");
    this.inputs = [...this.gain.querySelectorAll("input")];
    this.routes = new Map();
    this.advancedRows = new Map();
    for (const route of ROUTES) {
      const row = document.createElement("div");
      row.className = "vm-route";
      row.hidden = true;
      row.append(
        this.button(`route:${route}`, route, `Route to ${route}`),
        this.note(),
      );
      root.querySelector(".vm-route-grid").append(row);
      this.routes.set(`route:${route}`, row);
    }
    root.addEventListener("click", (event) => {
      const button = event.target.closest("button[data-control]");
      if (!this.active || !button || button.disabled) return;
      const binding = this.config.bindings.find(
        (item) => item.key === button.dataset.control,
      );
      if (!binding) return;
      const view = readControl(binding, this.hass);
      if (view.available)
        this.commands.send(
          binding.key,
          button.dataset.value !== undefined
            ? button.dataset.value === "true"
            : !view.value,
        );
    });
    for (const input of this.inputs) {
      input.addEventListener("input", () => {
        if (!this.active || input.disabled) return;
        this.gainError = null;
        this.draft = this.number(input);
        this.showDraft();
      });
      input.addEventListener("change", () => {
        if (!this.active || input.disabled) return;
        const value = this.number(input),
          binding = this.config.bindings.find((item) => item.key === "gain"),
          view = binding && readControl(binding, this.hass);
        this.gainError =
          !view || !controlRequest(view, value)
            ? "Use a valid gain within the displayed range and step."
            : null;
        this.draft = null;
        if (!this.gainError) this.commands.send("gain", value);
        this.changed();
      });
      input.addEventListener("keydown", (event) => {
        if (event.key === "Escape") {
          const binding = this.config.bindings.find(
              (item) => item.key === "gain",
            ),
            view = binding && readControl(binding, this.hass);
          input.value = view?.available ? view.value : "";
          this.draft = null;
          this.gainError = null;
          this.changed();
          input.blur();
        }
      });
    }
  }
  number(input) {
    return input.value.trim() === "" ? NaN : Number(input.value);
  }
  button(key, label, aria = label) {
    const button = document.createElement("button");
    button.className = "vm-toggle";
    button.type = "button";
    button.dataset.control = key;
    button.textContent = label;
    button.setAttribute("aria-label", aria);
    return button;
  }
  note() {
    const note = document.createElement("p");
    note.className = "vm-control-note";
    note.setAttribute("role", "status");
    note.hidden = true;
    return note;
  }
  configure(config) {
    for (const note of this.root.querySelectorAll(".vm-control-note")) {
      note.hidden = true;
      note.textContent = "";
    }
    this.config = normalizeControls(config);
    this.commands.configure(this.config);
    this.draft = null;
    this.gainError = null;
    this.root.querySelector(".vm-advanced").replaceChildren();
    this.root.querySelector(".vm-mono").replaceChildren();
    this.advancedRows.clear();
    this.root.querySelector(".vm-mono").hidden = true;
    for (const group of ADVANCED_GROUPS) {
      const bindings = this.config.bindings.filter(
        (binding) => binding.group === group,
      );
      if (!bindings.length) continue;
      if (group === "mono") {
        const container = this.root.querySelector(".vm-mono");
        container.hidden = false;
        container.append(...bindings.map((binding) => this.buildRow(binding)));
        continue;
      }
      const details = document.createElement("details");
      details.className = "vm-processing";
      const summary = document.createElement("summary");
      summary.textContent = groupLabels[group];
      details.append(summary);
      this.root.querySelector(".vm-advanced").append(details);
      let built = false;
      details.addEventListener("toggle", () => {
        if (!details.open || built) return;
        built = true;
        if (group === "eq_cells") this.buildEq(details, bindings);
        else {
          const body = document.createElement("div");
          body.className = "vm-process-body";
          body.append(...bindings.map((binding) => this.buildRow(binding)));
          details.append(body);
        }
        this.changed();
      });
    }
  }
  buildEq(details, bindings) {
    const toolbar = document.createElement("label");
    toolbar.className = "vm-eq-toolbar";
    toolbar.textContent = "Channel";
    const select = document.createElement("select");
    select.setAttribute("aria-label", "EQ channel");
    toolbar.append(select);
    details.append(toolbar);
    for (const channel of [
      ...new Set(bindings.map((binding) => binding.channel)),
    ].sort((a, b) => a - b)) {
      const option = document.createElement("option");
      option.value = channel;
      option.textContent = `Channel ${channel + 1}`;
      select.append(option);
    }
    const content = document.createElement("div");
    details.append(content);
    const render = () => {
      for (const [key, item] of this.advancedRows)
        if (item.binding.group === "eq_cells") this.advancedRows.delete(key);
      content.replaceChildren();
      const selected = bindings.filter(
        (binding) => binding.channel === Number(select.value),
      );
      for (const cell of [
        ...new Set(selected.map((binding) => binding.cell)),
      ].sort((a, b) => a - b)) {
        const cellDetails = document.createElement("details");
        cellDetails.className = "vm-eq-cell";
        const summary = document.createElement("summary");
        summary.textContent = `Band ${cell + 1}`;
        cellDetails.append(summary);
        content.append(cellDetails);
        let built = false;
        cellDetails.addEventListener("toggle", () => {
          if (!cellDetails.open || built) return;
          built = true;
          const body = document.createElement("div");
          body.className = "vm-process-body";
          body.append(
            ...selected
              .filter((binding) => binding.cell === cell)
              .map((binding) => this.buildRow(binding)),
          );
          cellDetails.append(body);
          this.changed();
        });
        if (cell === 0) cellDetails.open = true;
      }
      this.changed();
    };
    select.addEventListener("change", render);
    render();
  }
  buildRow(binding) {
    const row = document.createElement("div");
    row.className = "vm-param-row";
    if (binding.domain === "switch") {
      if (binding.id?.endsWith("_eq_ab")) {
        const label = document.createElement("span");
        label.className = "vm-param-label";
        label.textContent = "EQ memory";
        const bank = document.createElement("div");
        bank.className = "vm-memory";
        for (const [text, value] of [
          ["A", false],
          ["B", true],
        ]) {
          const button = this.button(binding.key, text, `EQ memory ${text}`);
          button.dataset.value = String(value);
          bank.append(button);
        }
        row.append(label, bank);
      } else row.append(this.button(binding.key, binding.label));
    } else {
      const label = document.createElement("label");
      label.className = "vm-param-label";
      label.textContent = binding.label;
      const inputs = document.createElement("div");
      inputs.className = "vm-param-inputs";
      const exact = document.createElement(
        binding.input === "select" ? "select" : "input",
      );
      exact.setAttribute("aria-label", binding.label);
      if (binding.input === "select") {
        for (const choice of binding.choices) {
          const option = document.createElement("option");
          option.value = choice.value;
          option.textContent = choice.label;
          exact.append(option);
        }
      } else {
        exact.type = "number";
        exact.className = "vm-param-number";
        exact.inputMode = "decimal";
      }
      if (binding.input === "fader" && binding.group !== "eq_cells") {
        const slider = document.createElement("input");
        slider.type = "range";
        slider.className = "vm-param-range";
        slider.setAttribute("aria-label", `${binding.label} fader`);
        slider.addEventListener("input", () => {
          exact.value = slider.value;
        });
        inputs.append(slider);
      }
      inputs.append(exact);
      if (binding.unit) {
        const unit = document.createElement("span");
        unit.className = "vm-param-unit";
        unit.textContent = binding.unit;
        inputs.append(unit);
      }
      label.append(inputs);
      row.append(label);
      for (const input of inputs.querySelectorAll("input,select")) {
        input.addEventListener("input", () => {
          const item = this.advancedRows.get(binding.key);
          if (item) {
            item.draft = true;
            item.error = null;
          }
        });
        input.addEventListener("change", () => {
          if (!this.active || input.disabled) return;
          const view = readControl(binding, this.hass),
            value = this.number(input),
            item = this.advancedRows.get(binding.key);
          if (!item) return;
          item.draft = false;
          item.error = !controlRequest(view, value)
            ? "Use the displayed range and step."
            : null;
          if (!item.error) this.commands.send(binding.key, value);
          this.changed();
        });
        input.addEventListener("keydown", (event) => {
          if (event.key === "Escape") {
            const view = readControl(binding, this.hass);
            input.value = view.available ? view.value : "";
            const item = this.advancedRows.get(binding.key);
            if (item) {
              item.error = null;
              item.draft = false;
            }
            input.blur();
            this.changed();
          }
        });
      }
    }
    row.append(this.note());
    this.advancedRows.set(binding.key, { row, binding, error: null });
    return row;
  }
  setHass(hass) {
    this.hass = hass;
    this.commands.update(hass);
  }
  setActive(active) {
    this.active = active;
    if (!active) {
      this.commands.dispose();
      this.draft = null;
      for (const item of this.advancedRows.values()) item.draft = false;
    }
  }
  showDraft() {
    const note = this.root.querySelector(".vm-draft");
    note.hidden = true;
    if (this.draft != null)
      for (const input of this.inputs)
        input.title = Number.isFinite(this.draft)
          ? `Proposed ${this.draft.toFixed(1)} dB; release to apply.`
          : "Enter a valid gain.";
  }
  feedback(root, view, command, error = null) {
    const note = root.querySelector(".vm-control-note");
    if (!note) return;
    note.textContent =
      error ??
      commandLabels[command.phase] ??
      (!view.available ? "Control unavailable. Check its entity mapping." : "");
    note.hidden = !error && !["error", "timeout"].includes(command.phase);
    note.dataset.error = String(
      !!error || ["error", "timeout"].includes(command.phase),
    );
    for (const input of root.querySelectorAll("input,select,button"))
      input.title =
        note.textContent ||
        `${view.label}; actual mixer state. ${view.domain === "number" && view.available ? `Range ${view.min} to ${view.max}${view.unit ? ` ${view.unit}` : ""}; step ${view.step}. ` : ""}Change to apply.`;
  }
  paint() {
    if (!this.active) return;
    const binding = this.config.bindings.find((item) => item.key === "gain"),
      toggles = this.config.bindings.filter((item) =>
        ["mute", "solo"].includes(item.key),
      ),
      routes = this.config.bindings.filter((item) =>
        item.key.startsWith("route:"),
      );
    this.section.hidden =
      !this.config.bindings.length && !this.config.warnings.length;
    const warning = this.root.querySelector(".vm-warning");
    warning.hidden = !this.config.warnings.length;
    warning.textContent = this.config.warnings.join(" ");
    for (const key of ["mute", "solo"]) {
      const item = toggles.find((binding) => binding.key === key),
        row = this.root.querySelector(`.vm-${key}`);
      row.hidden = !item;
      if (item) this.paintSwitch(row, item);
    }
    this.root.querySelector(".vm-routes").hidden = !routes.length;
    for (const [key, row] of this.routes) {
      const item = routes.find((binding) => binding.key === key);
      row.hidden = !item;
      if (item) this.paintSwitch(row, item);
    }
    for (const item of this.advancedRows.values()) {
      if (item.binding.domain === "switch") {
        this.paintSwitch(item.row, item.binding);
        continue;
      }
      const view = readControl(item.binding, this.hass),
        command = this.commands.status(item.binding.key);
      if (!view.available) item.draft = false;
      for (const input of item.row.querySelectorAll("input,select")) {
        input.disabled = !view.available || command.busy;
        if (view.available) {
          if (input.tagName === "INPUT") {
            input.min = view.min;
            input.max = view.max;
            input.step = view.step;
          }
          if (!item.draft || command.busy) input.value = view.value;
        }
      }
      this.feedback(item.row, view, command, item.error);
    }
    this.gain.hidden = !binding;
    if (!binding) return;
    const view = readControl(binding, this.hass),
      command = this.commands.status("gain");
    if (!view.available) this.draft = null;
    this.root.querySelector(".vm-gain-readback").textContent = view.available
      ? `${view.value.toFixed(1)} dB`
      : "Unavailable";
    for (const input of this.inputs) {
      input.disabled = !view.available || command.busy;
      if (view.available) {
        input.min = view.min;
        input.max = view.max;
        input.step = view.step;
        if (this.draft == null) input.value = view.value;
      }
    }
    this.feedback(this.gain, view, command, this.gainError);
    this.showDraft();
  }
  paintSwitch(root, binding) {
    const view = readControl(binding, this.hass),
      command = this.commands.status(binding.key);
    for (const button of root.querySelectorAll("button")) {
      button.disabled = !view.available || command.busy;
      button.dataset.pending = String(command.busy);
      button.dataset.error = String(
        ["error", "timeout"].includes(command.phase),
      );
      if (view.available)
        button.setAttribute(
          "aria-pressed",
          String(
            button.dataset.value !== undefined
              ? view.value === (button.dataset.value === "true")
              : view.value,
          ),
        );
      else button.removeAttribute("aria-pressed");
      button.setAttribute("aria-busy", String(command.busy));
    }
    this.feedback(root, view, command);
  }
}
