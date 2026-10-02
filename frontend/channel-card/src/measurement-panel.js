import { TelemetryMeasurement } from "./telemetry-measurement.js";

// Developer diagnostics only. No timers/histograms until the user starts a run.
export class MeasurementPanel {
  constructor(root) {
    this.root = root; this.active = false; this.run = null; this.timer = null;
    root.innerHTML = `<style>
      .measurement{margin-top:18px;border-top:1px solid var(--divider-color,#39434b);padding-top:12px;font:12px/1.5 system-ui}
      .measurement-controls{display:flex;flex-wrap:wrap;gap:8px;align-items:center}
      .measurement button,.measurement select{font:inherit;min-height:36px;max-width:100%}
      .measurement textarea{box-sizing:border-box;width:100%;height:180px;font:11px/1.5 monospace;resize:vertical}
    </style><section class="measurement" aria-label="Telemetry measurement">
      <p>Developer measurement · DOM update timing is not screen presentation latency.</p>
      <div class="measurement-controls"><label>Duration <select aria-label="Measurement duration">
        <option value="10">10 seconds (smoke)</option><option value="60">1 minute (smoke)</option>
        <option value="900" selected>15 minutes</option><option value="1800">30 minutes</option>
      </select></label><button data-action="start" type="button">Start measurement</button>
      <button data-action="stop" type="button" disabled>Stop measurement</button></div>
      <p class="measurement-status" role="status">Not started</p>
      <textarea aria-label="Measurement report" readonly hidden></textarea></section>`;
    this.startButton = root.querySelector("[data-action=start]"); this.stopButton = root.querySelector("[data-action=stop]");
    this.duration = root.querySelector("select"); this.status = root.querySelector(".measurement-status");
    this.output = root.querySelector("textarea");
    this.startButton.addEventListener("click", () => this.start());
    this.stopButton.addEventListener("click", () => this.finish("stopped"));
  }
  configure(config) {
    clearTimeout(this.timer); this.timer = null; this.run = null; this.transport = null;
    this.config = config; this.root.hidden = !config.diagnostics;
    this.output.hidden = true; this.output.value = ""; this.status.textContent = "Not started";
    this.buttons();
  }
  buttons() {
    this.startButton.disabled = !this.active || !!this.run || !this.config?.diagnostics ||
      !this.config.id || !this.config.topic || this.config.transport === "entities_only";
    this.stopButton.disabled = !this.run; this.duration.disabled = !!this.run;
  }
  setActive(active) {
    this.active = active;
    if (!active) this.finish("hidden");
    this.buttons();
  }
  start() {
    if (this.startButton.disabled) return;
    const seconds = Number(this.duration.value);
    if (![10, 60, 900, 1800].includes(seconds)) return;
    this.run = new TelemetryMeasurement(performance.now(), seconds * 1000);
    this.started = new Date().toISOString(); this.output.hidden = true; this.output.value = "";
    this.buttons(); this.tick();
  }
  tick() {
    if (!this.run) return;
    const now = performance.now();
    if (!this.run.running(now)) { this.finish("duration_complete"); return; }
    this.status.textContent = `Measuring · ${this.run.received} accepted frames · ${Math.ceil((this.run.deadline - now) / 1000)} s remaining`;
    this.timer = setTimeout(() => this.tick(), Math.min(1000, this.run.deadline - now));
  }
  receive(event) {
    this.transport = event.transport ?? null;
    if (event.timing) this.run?.receive(event.timing);
  }
  dom(session, sequence) { this.run?.dom(session, sequence, performance.now()); }
  finish(reason) {
    if (!this.run) return;
    clearTimeout(this.timer); this.timer = null;
    const now = performance.now(); this.run.stop(now);
    this.output.value = JSON.stringify({ schema: 1, source: this.config.id, tap: this.config.tap,
      transport_mode: this.config.transport, started_at_utc: this.started, end_reason: reason,
      scope: "accepted_native_frames_only; DOM update, not screen presentation; UTC clock skew applies",
      ...this.run.snapshot(now), transport_since_subscription_at_last_accepted: this.transport }, null, 2);
    this.output.hidden = false; this.status.textContent = reason === "duration_complete" ? "Measurement complete" : `Measurement ended: ${reason}`;
    this.run = null; this.buttons();
  }
}
