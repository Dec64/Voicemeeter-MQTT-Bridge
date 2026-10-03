import {
  frequencyPosition,
  positionFrequency,
  quantize,
  eqResponse,
  compressorOutput,
} from "./audio-response.js";
const svgNS = "http://www.w3.org/2000/svg";
function svgNode(name, attributes = {}) {
  const node = document.createElementNS(svgNS, name);
  for (const [key, value] of Object.entries(attributes))
    node.setAttribute(key, value);
  return node;
}
const clamp = (value, min, max) => Math.max(min, Math.min(max, value));
export const bandColors = [
  "#78ff8e",
  "#ffd466",
  "#78cfff",
  "#ff9b70",
  "#d2a5ff",
  "#65e0cc",
];

// The native exact input stays the command/readback authority for each dial.
export class RotaryControl {
  constructor(input, binding, active) {
    this.input = input;
    this.binding = binding;
    this.active = active;
    this.node = document.createElement("button");
    this.node.type = "button";
    this.node.className = "vm-dial";
    this.node.setAttribute("role", "slider");
    this.node.setAttribute("aria-label", `${binding.label} dial`);
    this.node.innerHTML =
      '<span class="vm-dial-face"><span class="vm-dial-needle"></span></span>';
    this.node.addEventListener("pointerdown", (event) => {
      if (event.button !== 0 || !this.canEdit()) return;
      event.preventDefault();
      this.node.focus();
      this.drag = {
        id: event.pointerId,
        y: event.clientY,
        value: Number(input.value),
        moved: false,
      };
      this.node.setPointerCapture(event.pointerId);
    });
    this.node.addEventListener("pointermove", (event) => {
      if (!this.drag || event.pointerId !== this.drag.id) return;
      if (!this.canEdit()) {
        this.cancel();
        return;
      }
      const delta =
        (this.drag.y - event.clientY) / (event.shiftKey ? 1200 : 180);
      this.drag.moved ||= Math.abs(event.clientY - this.drag.y) > 1;
      this.propose(this.fromPosition(this.position(this.drag.value) + delta));
    });
    this.node.addEventListener("pointerup", (event) => {
      if (!this.drag || event.pointerId !== this.drag.id) return;
      const moved = this.drag.moved;
      this.drag = null;
      if (this.node.hasPointerCapture(event.pointerId))
        this.node.releasePointerCapture(event.pointerId);
      if (moved && this.canEdit())
        input.dispatchEvent(new Event("change", { bubbles: true }));
    });
    this.node.addEventListener("pointercancel", () => this.cancel());
    this.node.addEventListener("lostpointercapture", () => {
      if (this.drag) this.cancel();
    });
    this.node.addEventListener("keydown", (event) => {
      if (event.key === "Escape") {
        this.cancel();
        event.preventDefault();
        return;
      }
      if (!this.canEdit()) return;
      const direction = {
        ArrowUp: 1,
        ArrowRight: 1,
        ArrowDown: -1,
        ArrowLeft: -1,
        PageUp: 10,
        PageDown: -10,
      }[event.key];
      if (direction === undefined && !["Home", "End"].includes(event.key))
        return;
      event.preventDefault();
      const view = this.bounds();
      this.propose(
        event.key === "Home"
          ? view.min
          : event.key === "End"
            ? view.max
            : Number(input.value) + direction * view.step,
      );
      input.dispatchEvent(new Event("change", { bubbles: true }));
    });
  }
  bounds() {
    return {
      min: Number(this.input.min),
      max: Number(this.input.max),
      step: Number(this.input.step),
    };
  }
  canEdit() {
    return this.active() && !this.input.disabled;
  }
  position(value) {
    const { min, max } = this.bounds();
    return this.binding.unit === "Hz" ||
      this.binding.unit === "ms" ||
      this.binding.id?.endsWith("_q")
      ? Math.log((value + 1) / (min + 1)) / Math.log((max + 1) / (min + 1))
      : (value - min) / (max - min);
  }
  fromPosition(x) {
    const { min, max } = this.bounds();
    x = clamp(x, 0, 1);
    return this.binding.unit === "Hz" ||
      this.binding.unit === "ms" ||
      this.binding.id?.endsWith("_q")
      ? (min + 1) * ((max + 1) / (min + 1)) ** x - 1
      : min + x * (max - min);
  }
  propose(value) {
    this.input.value = quantize(value, this.bounds());
    this.input.dispatchEvent(new Event("input", { bubbles: true }));
    this.paint();
  }
  cancel() {
    this.drag = null;
    this.input.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true }),
    );
    this.paint();
  }
  paint() {
    const { min, max } = this.bounds(),
      value = Number(this.input.value);
    this.node.disabled = this.input.disabled;
    this.node.setAttribute("aria-valuemin", min);
    this.node.setAttribute("aria-valuemax", max);
    if (!this.input.disabled) {
      this.node.setAttribute("aria-valuenow", value);
      this.node.setAttribute(
        "aria-valuetext",
        `${value}${this.binding.unit || ""}`,
      );
    } else {
      this.node.removeAttribute("aria-valuenow");
      this.node.setAttribute("aria-valuetext", "Unavailable or applying");
    }
    const position = Number.isFinite(this.position(value))
      ? clamp(this.position(value), 0, 1)
      : 0;
    this.node.style.setProperty("--dial-angle", `${-135 + position * 270}deg`);
    this.node.style.setProperty("--dial-sweep", `${position * 270}deg`);
    this.node.title = `${this.binding.label}: ${this.input.disabled ? "unavailable or applying" : value + " " + (this.binding.unit || "")}. Drag up/down; Shift for fine adjustment; arrow keys change one step; Escape cancels.`;
  }
}

export class EqGraph {
  constructor(container, { read, commit, active, select }) {
    Object.assign(this, { read, commit, active, select });
    this.selected = 0;
    this.draft = null;
    this.node = document.createElement("div");
    this.node.className = "vm-eq-graph";
    this.node.title =
      "Setting preview using standard 48 kHz biquad filters; not a measured frequency response. Drag a band horizontally for frequency and vertically for gain. Gain applies to bell/shelf filters. Select a band below to edit Q or filter type.";
    this.svg = svgNode("svg", {
      viewBox: "0 0 600 240",
      "aria-hidden": "true",
      preserveAspectRatio: "none",
    });
    for (const db of [-12, -6, 0, 6, 12]) {
      const y = 20 + ((12 - db) / 24) * 190;
      this.svg.append(
        svgNode("line", {
          x1: 36,
          x2: 586,
          y1: y,
          y2: y,
          class: db === 0 ? "vm-graph-zero" : "vm-graph-grid",
        }),
      );
      const text = svgNode("text", { x: 30, y: y + 4, "text-anchor": "end" });
      text.textContent = db > 0 ? `+${db}` : db;
      this.svg.append(text);
    }
    for (const f of [20, 100, 1000, 10000, 20000]) {
      const x = 36 + frequencyPosition(f) * 550;
      this.svg.append(
        svgNode("line", {
          x1: x,
          x2: x,
          y1: 20,
          y2: 210,
          class: "vm-graph-grid",
        }),
      );
      const text = svgNode("text", {
        x,
        y: 232,
        "text-anchor": f === 20 ? "start" : f === 20000 ? "end" : "middle",
      });
      text.textContent = f >= 1000 ? `${f / 1000}k` : f;
      this.svg.append(text);
    }
    this.bandPath = svgNode("path", { class: "vm-eq-band-curve" });
    this.path = svgNode("path", { class: "vm-eq-curve" });
    this.svg.append(this.bandPath, this.path);
    this.node.append(this.svg);
    container.append(this.node);
    this.handles = new Map();
    this.node.addEventListener("pointermove", (event) => this.move(event));
    this.node.addEventListener("pointerup", (event) => this.finish(event));
    this.node.addEventListener("pointercancel", () => this.cancel());
    this.node.addEventListener("lostpointercapture", () => {
      if (this.draft) this.cancel();
    });
  }
  values() {
    const bands = this.read();
    return bands.map((b) =>
      this.draft?.cell === b.cell ? { ...b, ...this.draft.values } : b,
    );
  }
  canDrag(band) {
    return this.active() && band?.editable?.f;
  }
  paint() {
    const bands = this.values();
    if (
      this.draft &&
      !this.canDrag(this.read().find((b) => b.cell === this.draft.cell))
    ) {
      this.cancel();
      return;
    }
    const signature = JSON.stringify([bands, this.selected, this.active()]);
    if (signature === this.signature) return;
    this.signature = signature;
    const positions = [
      ...new Set([
        ...Array.from({ length: 281 }, (_, i) => i / 280),
        ...bands
          .filter((band) => Number.isFinite(band.f))
          .map((band) => frequencyPosition(band.f)),
      ]),
    ].sort((a, b) => a - b);
    const points = (items) =>
      positions
        .map((x, i) => {
          return `${i ? "L" : "M"}${36 + x * 550},${20 + ((12 - clamp(eqResponse(items, positionFrequency(x)), -12, 12)) / 24) * 190}`;
        })
        .join(" ");
    this.path.setAttribute("d", points(bands));
    this.bandPath.setAttribute(
      "d",
      points(bands.filter((b) => b.cell === this.selected)),
    );
    this.bandPath.style.stroke = bandColors[this.selected % 6];
    for (const band of bands) {
      let button = this.handles.get(band.cell);
      if (!button) {
        button = document.createElement("button");
        button.type = "button";
        button.className = "vm-eq-handle";
        button.textContent = band.cell + 1;
        button.style.setProperty("--band-color", bandColors[band.cell % 6]);
        button.dataset.cell = band.cell;
        this.node.append(button);
        this.handles.set(band.cell, button);
        button.addEventListener("click", () => {
          this.selected = band.cell;
          this.select(band.cell);
          this.signature = null;
          this.paint();
        });
        button.addEventListener("pointerdown", (event) => {
          const current = this.read().find((b) => b.cell === band.cell);
          if (event.button !== 0 || !this.canDrag(current)) return;
          event.preventDefault();
          button.focus();
          this.selected = band.cell;
          this.select(band.cell);
          this.draft = {
            cell: band.cell,
            id: event.pointerId,
            values: {},
            moved: false,
          };
          this.node.setPointerCapture(event.pointerId);
          this.paint();
        });
        button.addEventListener("keydown", (event) => {
          if (event.key === "Escape") {
            event.preventDefault();
            this.cancel();
            return;
          }
          const current = this.read().find((b) => b.cell === band.cell);
          if (!this.canDrag(current)) return;
          const direction = {
            ArrowRight: 1,
            ArrowLeft: -1,
            ArrowUp: 1,
            ArrowDown: -1,
          }[event.key];
          if (!direction) return;
          event.preventDefault();
          const prop =
            event.key === "ArrowRight" || event.key === "ArrowLeft"
              ? "f"
              : "gain";
          if (
            !current.editable[prop] ||
            (prop === "gain" && ![0, 5, 6].includes(current.type))
          )
            return;
          const value =
            prop === "f"
              ? current.f *
                2 ** (direction * (event.shiftKey ? 1 / 120 : 1 / 12))
              : current.gain + direction * (event.shiftKey ? 0.1 : 1);
          this.commit(current, prop, value);
        });
      }
      button.hidden = !Number.isFinite(band.f);
      button.disabled = !this.canDrag(band);
      const gain = [0, 5, 6].includes(band.type) ? band.gain : 0;
      button.style.left = `${((36 + frequencyPosition(band.f) * 550) / 600) * 100}%`;
      button.style.top = `${((20 + ((12 - clamp(gain || 0, -12, 12)) / 24) * 190) / 240) * 100}%`;
      button.dataset.enabled = String(band.on === true);
      button.setAttribute("aria-pressed", String(this.selected === band.cell));
      button.setAttribute(
        "aria-label",
        `EQ band ${band.cell + 1}, ${band.f} Hz, ${band.gain ?? "unavailable"} dB`,
      );
      button.title = `Band ${band.cell + 1}: ${band.f} Hz / ${band.gain ?? "—"} dB / Q ${band.q ?? "—"}. ${band.on ? "Enabled" : "Bypassed or unavailable"}. Arrow keys adjust frequency/gain; Shift for fine adjustment.`;
    }
  }
  move(event) {
    if (!this.draft || event.pointerId !== this.draft.id) return;
    const band = this.read().find((b) => b.cell === this.draft.cell);
    if (!this.canDrag(band)) {
      this.cancel();
      return;
    }
    const rect = this.node.getBoundingClientRect(),
      x = ((event.clientX - rect.left) / rect.width) * 600,
      y = ((event.clientY - rect.top) / rect.height) * 240;
    this.draft.values.f = quantize(
      positionFrequency((x - 36) / 550),
      band.bounds.f,
    );
    if (band.editable.gain && [0, 5, 6].includes(band.type))
      this.draft.values.gain = quantize(
        12 - ((y - 20) / 190) * 24,
        band.bounds.gain,
      );
    this.draft.moved = true;
    this.paint();
  }
  finish(event) {
    if (!this.draft || event.pointerId !== this.draft.id) return;
    const draft = this.draft,
      band = this.read().find((b) => b.cell === draft.cell);
    this.draft = null;
    if (this.node.hasPointerCapture(event.pointerId))
      this.node.releasePointerCapture(event.pointerId);
    if (draft.moved && this.canDrag(band))
      for (const [prop, value] of Object.entries(draft.values))
        if (value !== band[prop]) this.commit(band, prop, value);
    this.signature = null;
    this.paint();
  }
  cancel() {
    this.draft = null;
    this.signature = null;
    this.paint();
  }
}

export class ProcessingPlot {
  constructor(container, group, read) {
    this.group = group;
    this.read = read;
    this.node = document.createElement("div");
    this.node.className = "vm-processing-plot";
    this.node.title =
      group === "compressor"
        ? "Illustrative static compression transfer curve from threshold, ratio, knee and manual input/output gain. Native amount and automatic makeup are not modeled; this is not a gain-reduction meter."
        : "Illustrative gate timing envelope from attack, hold and release; not a live gate activity meter.";
    this.svg = svgNode("svg", {
      viewBox: "0 0 320 145",
      "aria-hidden": "true",
      preserveAspectRatio: "none",
    });
    for (let i = 0; i < 5; i++)
      this.svg.append(
        svgNode("line", {
          x1: 28,
          x2: 308,
          y1: 12 + i * 26,
          y2: 12 + i * 26,
          class: "vm-graph-grid",
        }),
      );
    this.path = svgNode("path", { class: "vm-eq-curve" });
    this.svg.append(this.path);
    const label = svgNode("text", { x: 28, y: 140 });
    label.textContent =
      group === "compressor"
        ? "INPUT dB → OUTPUT dB"
        : "ATTACK → HOLD → RELEASE";
    this.svg.append(label);
    this.node.append(this.svg);
    container.append(this.node);
  }
  paint() {
    const p = this.read(),
      signature = JSON.stringify(p);
    if (signature === this.signature) return;
    this.signature = signature;
    let d = "";
    if (this.group === "compressor") {
      if (Number.isFinite(p.threshold) && Number.isFinite(p.ratio))
        d = Array.from({ length: 121 }, (_, i) => {
          const input = -60 + i / 2,
            out = compressorOutput(input, p);
          return `${i ? "L" : "M"}${28 + (i / 120) * 280},${116 - clamp((out + 60) / 60, 0, 1) * 104}`;
        }).join(" ");
    } else if ([p.attack, p.hold, p.release].every(Number.isFinite)) {
      const total = Math.max(1, p.attack + p.hold + p.release),
        x1 = 28 + (280 * p.attack) / total,
        x2 = x1 + (280 * p.hold) / total;
      d = `M28,116 L${x1},12 L${x2},12 L308,116`;
    }
    this.path.setAttribute("d", d);
    this.node.dataset.available = String(!!d);
  }
}
