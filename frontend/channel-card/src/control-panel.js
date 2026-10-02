import { normalizeControls, readControl } from "./control-model.js";
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
      .vm-mute{margin-top:12px}.vm-toggle{min-height:44px;padding:10px 16px;border:1px solid var(--divider-color,#657681);border-radius:6px;background:var(--card-background-color,#26323b);color:inherit;font:inherit;cursor:pointer}
      .vm-toggle[aria-pressed=true]{background:#275646;color:#edfff7;border-color:#57cba0}.vm-toggle:disabled{opacity:.6;cursor:default}.vm-toggle:focus-visible{outline:2px solid #57cba0;outline-offset:2px}
    </style><section class="vm-controls" aria-label="Channel controls" hidden>
      <p class="vm-warning" role="status" hidden></p>
      <div class="vm-gain" hidden><div class="vm-gain-head"><span>Gain</span><output class="vm-gain-readback"></output></div>
      <div class="vm-gain-inputs"><input class="vm-gain-range" type="range" aria-label="Gain in dB"><input class="vm-gain-number" type="number" aria-label="Exact gain in dB"></div>
      <p class="vm-draft" hidden></p><p class="vm-control-note" role="status"></p></div>
      <div class="vm-mute" hidden><button class="vm-toggle" type="button" aria-label="Mute" data-control="mute">Mute</button><p class="vm-mute-note vm-control-note" role="status"></p></div>
    </section>`;
    this.section = root.querySelector("section"); this.gain = root.querySelector(".vm-gain");
    this.inputs = [...root.querySelectorAll("input")];
    root.addEventListener("click", event => {
      const button = event.target.closest("button[data-control]");
      if (!this.active || !button || button.disabled) return;
      const key = button.dataset.control, binding = this.config.bindings.find(item => item.key === key);
      if (binding) { const view = readControl(binding, this.hass); if (view.available) this.commands.send(key, !view.value); }
    });
    for (const input of this.inputs) {
      input.addEventListener("input", () => {
        if (!this.active || input.disabled) return;
        this.draft = input.value.trim() === "" ? NaN : Number(input.value);
        this.showDraft();
      });
      input.addEventListener("change", () => {
        if (!this.active || input.disabled) return;
        const value = input.value.trim() === "" ? NaN : Number(input.value);
        this.draft = null; this.commands.send("gain", value); this.changed();
      });
      input.addEventListener("keydown", event => {
        if (event.key === "Escape") { this.draft = null; this.changed(); input.blur(); }
      });
    }
  }
  configure(config) { this.config = normalizeControls(config); this.commands.configure(this.config); this.draft = null; }
  setHass(hass) { this.hass = hass; this.commands.update(hass); }
  setActive(active) { this.active = active; if (!active) { this.commands.dispose(); this.draft = null; } }
  showDraft() {
    const note = this.root.querySelector(".vm-draft"); note.hidden = this.draft === null || this.draft === undefined;
    note.textContent = Number.isFinite(this.draft) ? `Proposed ${this.draft.toFixed(1)} dB · release to send` : "Enter a valid gain.";
  }
  paint() {
    if (!this.active) return;
    const binding = this.config.bindings.find(item => item.key === "gain");
    const mute = this.config.bindings.find(item => item.key === "mute");
    this.section.hidden = !binding && !mute && !this.config.warnings.length;
    const warning = this.root.querySelector(".vm-warning"); warning.hidden = !this.config.warnings.length;
    warning.textContent = this.config.warnings.join(" "); this.gain.hidden = !binding;
    const muteRoot = this.root.querySelector(".vm-mute"); muteRoot.hidden = !mute;
    if (mute) this.paintSwitch(muteRoot, mute);
    if (!binding) return;
    const view = readControl(binding, this.hass), command = this.commands.status("gain");
    this.root.querySelector(".vm-gain-readback").textContent = view.available ? `${view.value.toFixed(1)} dB` : "Unavailable";
    for (const input of this.inputs) {
      input.disabled = !view.available || command.busy;
      if (view.available) {
        input.min = view.min; input.max = view.max; input.step = view.step;
        if (this.draft === null || this.draft === undefined) input.value = view.value;
      }
    }
    const note = this.root.querySelector(".vm-control-note");
    note.textContent = !view.available ? "Check the gain entity, dB range and HA services." : commandLabels[command.phase] ?? "HA readback · commands send on release";
    note.dataset.error = String(["error", "timeout"].includes(command.phase)); this.showDraft();
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
