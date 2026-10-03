// Voicemeeter MQTT Bridge. See repository LICENSE and upstream attribution.
import { MeterModel } from "./meter-model.js";
import { CardFeed } from "./card-feed.js";
import { slowSensorView } from "./slow-sensor.js";
import { VisibleRenderer } from "./visible-renderer.js";
import { ControlPanel } from "./control-panel.js";
import { normalizeControls } from "./control-model.js";
import { MeasurementPanel } from "./measurement-panel.js";
import { PeakMotion } from "./peak-motion.js";
import "./channel-card-editor.js";

const statusLabels = { unconfigured: "Choose a source", waiting: "Waiting for data", stale: "Stale data",
  unavailable: "Unavailable", silence: "Silence", signal: "Signal" };
const tapLabels = { pre: "Incoming · pre-fader", post_mute: "After mute", output: "Bus output" };
const streamLabels = { waiting_metadata: "Waiting for metadata", metadata: "Waiting for matching data", invalid_metadata: "Invalid metadata",
  error: "Stream unavailable", disconnected: "HA disconnected", unsupported: "Source or tap unavailable" };

export class VoicemeeterChannelCard extends HTMLElement {
  constructor() {
    super();
    this.attachShadow({ mode: "open" });
    // Only static markup enters innerHTML. Configuration and readings use textContent.
    this.shadowRoot.innerHTML = `
      <style>
        :host{display:block;min-width:0;color:var(--primary-text-color,#e9edf1);font-family:var(--paper-font-body1_-_font-family,"Segoe UI",sans-serif)}
        *{box-sizing:border-box}article{padding:24px;background:var(--ha-card-background,var(--card-background-color,#20272d));border:1px solid var(--divider-color,#39434b);border-radius:var(--ha-card-border-radius,14px)}
        header{display:flex;align-items:flex-start;justify-content:space-between;gap:16px}.identity{min-width:0}
        .id,footer,.scale{font:11px/1.5 "Cascadia Code",Consolas,monospace;color:var(--secondary-text-color,#a9b7bf)}
        .id{text-transform:uppercase;letter-spacing:.12em}h2{font-size:21px;line-height:1.25;margin:7px 0 0;font-weight:600;overflow-wrap:anywhere}
        .reading{text-align:right;white-space:nowrap}.value{font:32px/1.1 "Cascadia Code",Consolas,monospace;letter-spacing:-.06em}.unit{font-size:10px;letter-spacing:.12em;margin-top:5px;color:var(--secondary-text-color,#a9b7bf)}
        .meter{margin-top:27px}.track{position:relative;height:18px;border-radius:3px;overflow:hidden;background:var(--vm-meter-track,#111a20);outline:1px solid var(--divider-color,#39434b)}
        .color{position:absolute;inset:0;background:linear-gradient(90deg,#57cba0 0%,#8ad5a2 65%,#e6c66b 84%,#ed7d67 100%)}
        .cover{position:absolute;inset:0;background:var(--vm-meter-track,#111a20);transform-origin:right;transform:scaleX(1)}
        .grid{position:absolute;inset:0;background:repeating-linear-gradient(90deg,transparent 0,transparent calc(5% - 1px),#15202780 calc(5% - 1px),#15202780 5%)}
        .scale{display:flex;justify-content:space-between;margin-top:7px;font-size:10px}
        footer{display:flex;justify-content:space-between;gap:12px;margin-top:22px;flex-wrap:wrap}.status{display:flex;align-items:center;gap:7px}.dot{width:6px;height:6px;border-radius:50%;background:#92a2ac}
        article[data-state=signal] .dot{background:#57cba0}article[data-state=stale] .dot,article[data-state=unavailable] .dot{background:#e6c66b}
        article[data-variant=compact]{padding:16px}article[data-variant=compact] .meter{margin-top:16px}article[data-variant=compact] footer{margin-top:14px}article[data-variant=expanded]{padding:30px}
        article[data-orientation=vertical] .meter{display:flex;justify-content:center;height:190px;gap:12px}
        article[data-orientation=vertical] .track{width:26px;height:100%}
        article[data-orientation=vertical] .scale{margin:0;flex-direction:column-reverse}
        article[data-orientation=vertical] .color{background:linear-gradient(0deg,#57cba0 0%,#8ad5a2 65%,#e6c66b 84%,#ed7d67 100%)}
        article[data-orientation=vertical] .cover{transform-origin:top}
        article[data-orientation=vertical] .grid{background:repeating-linear-gradient(0deg,transparent 0,transparent calc(5% - 1px),#15202780 calc(5% - 1px),#15202780 5%)}
        article[data-orientation=vertical][data-variant=compact] .meter{height:130px}
        @media(max-width:340px){article{padding:18px}h2{font-size:18px}.value{font-size:26px}}
      </style>
      <article data-state="unconfigured"><header><div class="identity"><div class="id"></div><h2></h2></div>
      <div class="reading"><div class="value">—</div><div class="unit">PEAK · dBFS</div></div></header>
      <div class="meter"><div class="track" role="meter" aria-label="Combined peak level"><div class="color"></div><div class="cover"></div><div class="grid"></div><div class="peak-marker" hidden></div></div>
      <div class="scale" aria-hidden="true"><span></span><span></span><span></span><span>0</span></div></div>
      <canvas class="history" width="320" height="30" aria-label="Recent sampled peaks" hidden></canvas><div class="clip" role="status" hidden>Observed clipping</div>
      <div class="controls-root"></div><footer><span class="status"><span class="dot" aria-hidden="true"></span><span class="status-text"></span></span><span class="tap"></span></footer><div class="measurement-root" hidden></div></article>`;
    this.nodes = Object.fromEntries(["article", "h2", ".id", ".value", ".track", ".cover", ".status-text", ".tap", ".peak-marker", ".clip", ".history"]
      .map(selector => [selector, this.shadowRoot.querySelector(selector)]));
    this.feed = new CardFeed(value => this.receiveTelemetry(value));
    this.renderer = new VisibleRenderer(() => this.paint());
    this.controls = new ControlPanel(this.shadowRoot.querySelector(".controls-root"), () => this.render());
    this.measurement = new MeasurementPanel(this.shadowRoot.querySelector(".measurement-root"));
    this.visible = false;
    this.visibilityChanged = () => this.updateVisibility();
    this.setConfig({});
  }
  static getStubConfig() { return { type: "custom:voicemeeter-channel-card", source: { id: "" } }; }
  static getConfigElement() { return document.createElement("voicemeeter-channel-card-editor"); }
  getCardSize() { return this.model.config.orientation === "vertical" ? 7 : this.model.config.variant === "compact" ? 3 : 4; }
  getGridOptions() { return { columns: 6, min_columns: 3 }; }
  setConfig(config) {
    const model = new MeterModel(config);
    normalizeControls(config); // Validate before changing the existing card lifecycle.
    this.controls.configure(config); this.controls.setHass(this.ha);
    this.feed.stop(); this.descriptor = null; this.streamStatus = null;
    this.clearTimer();
    this.model = model;
    this.motion = new PeakMotion(model.config.floor, model.config.historySeconds, model.config.holdMs);
    this.measurement.configure(model.config);
    this.render();
    this.updateFeed();
  }
  set hass(value) { this.ha = value; this.controls.setHass(value); this.updateFeed(); this.render(); }
  updateFeed() {
    const { topic, transport, id } = this.model.config;
    this.feed.update(this.ha?.connection, topic, this.visible && !!id && transport !== "entities_only");
  }
  receiveTelemetry(event) {
    if (!this.visible) return;
    const { id, tap } = this.model.config;
    this.descriptor = event.metadata?.sources.find(source => source.id === id);
    if (event.metadata && (!this.descriptor?.enabled || !this.descriptor.taps.includes(tap))) {
      this.clearTimer(); this.model.reset(); this.streamStatus = "unsupported"; this.render(); return;
    }
    if (!event.frame) {
      this.clearTimer(); this.model.reset(); this.streamStatus = event.state; this.render(); return;
    }
    if (this.model.session !== event.frame.session_id) this.model.reset();
    this.streamStatus = null;
    if (this.setFrame(event.frame, Date.now() - Date.parse(event.frame.published_at_utc))) this.measurement.receive(event);
  }
  // Fixture seam. Native frames first pass metadata/session checks in receiveTelemetry.
  setFrame(frame, publicationAgeMs = 0) {
    const previousSession = this.model.session;
    if (!this.visible || !this.model.accept(frame, performance.now(), publicationAgeMs)) return false;
    if (previousSession !== this.model.session) this.motion.reset();
    this.clearTimer();
    const accepted = this.model.view(this.model.receivedAt);
    if (accepted.level !== null) this.motion.observe(accepted.level, this.model.receivedAt);
    else this.motion.reset();
    this.render();
    const expire = () => {
      this.render();
      const remaining = this.model.expiresAt - performance.now();
      if (remaining > 0) this.expiryTimer = setTimeout(expire, Math.ceil(remaining));
      else this.expiryTimer = undefined;
    };
    this.expiryTimer = setTimeout(expire, Math.max(0, this.model.expiresAt - performance.now()));
    return true;
  }
  connectedCallback() {
    document.addEventListener("visibilitychange", this.visibilityChanged);
    this.inViewport = typeof IntersectionObserver === "undefined";
    if (!this.inViewport) {
      const observer = new IntersectionObserver(entries => {
        if (this.observer !== observer || !this.isConnected) return;
        this.inViewport = entries.some(entry => entry.target === this && entry.isIntersecting);
        this.updateVisibility();
      });
      this.observer = observer; observer.observe(this);
    }
    this.updateVisibility();
  }
  disconnectedCallback() {
    this.observer?.disconnect(); this.observer = null;
    document.removeEventListener("visibilitychange", this.visibilityChanged);
    this.updateVisibility();
  }
  updateVisibility() {
    const visible = this.isConnected && this.inViewport && !document.hidden;
    if (visible === this.visible) return;
    this.visible = visible; this.renderer.setActive(visible);
    this.controls.setActive(visible);
    this.measurement.setActive(visible);
    if (!visible) {
      this.feed.stop(); this.clearTimer(); this.model.reset(); this.motion.reset(); this.descriptor = null; this.streamStatus = null;
    } else { this.updateFeed(); this.render(); }
  }
  clearTimer() { clearTimeout(this.expiryTimer); this.expiryTimer = undefined; clearTimeout(this.sensorTimer); this.sensorTimer = undefined; }
  render() { this.renderer.request(); }
  paint() {
    if (!this.visible) return;
    this.controls.paint();
    let view = this.model.view(performance.now());
    const { transport } = this.model.config;
    const slow = (transport === "entities_only" || (transport === "auto" && ["waiting", "stale"].includes(view.state)))
      ? slowSensorView(this.model.config, this.ha) : null;
    clearTimeout(this.sensorTimer); this.sensorTimer = undefined;
    if (slow) {
      view = { ...view, ...slow };
      if (this.isConnected && slow.expiresAt !== null)
        this.sensorTimer = setTimeout(() => this.render(), Math.max(1, slow.expiresAt - Date.now()));
    }
    this.nodes.article.dataset.state = view.state;
    const { orientation, variant } = this.model.config;
    this.nodes.article.dataset.orientation = orientation;
    this.nodes.article.dataset.variant = variant;
    this.nodes.h2.textContent = (view.label === view.id && this.descriptor?.label) || view.label;
    this.nodes[".id"].textContent = view.id ? `${view.id.startsWith("bus:") ? "OUTPUT" : "INPUT"} / ${view.id}` : "UNASSIGNED";
    this.nodes[".value"].textContent = view.level === null ? "—" : Math.max(view.floor, view.level).toFixed(1);
    this.nodes[".status-text"].textContent = slow ? `Slow sensor · ${view.level === null ? statusLabels[view.state] : "reduced freshness"}`
      : streamLabels[this.streamStatus] ?? statusLabels[view.state];
    this.nodes[".tap"].textContent = tapLabels[view.tap];
    let displayed = view.fill;
    const marker = this.nodes[".peak-marker"], clip = this.nodes[".clip"];
    if (!slow && view.level !== null) {
      const motion = this.motion.view(performance.now(), matchMedia("(prefers-reduced-motion: reduce)").matches);
      if (motion.level !== null) displayed = Math.max(0, Math.min(1, (motion.level - view.floor) / -view.floor));
      marker.hidden = motion.hold === null || motion.hold <= view.floor;
      const percent = Math.max(0, Math.min(100, (motion.hold - view.floor) / -view.floor * 100));
      marker.style.cssText = orientation === "vertical"
        ? `position:absolute;left:0;right:0;bottom:${percent}%;height:2px;background:var(--primary-text-color,#fff)`
        : `position:absolute;left:${percent}%;top:0;bottom:0;width:2px;background:var(--primary-text-color,#fff)`;
      clip.hidden = !motion.clipping;
      if (motion.animate) this.renderer.request();
    } else { marker.hidden = true; clip.hidden = true; this.motion.reset(); }
    const canvas = this.nodes[".history"];
    canvas.hidden = !this.model.config.showHistory || !!slow;
    canvas.style.cssText = "width:100%;height:30px;margin-top:12px";
    if (!canvas.hidden) {
      const context = canvas.getContext("2d"), now = performance.now();
      context.clearRect(0, 0, 320, 30); context.strokeStyle = "#57cba0"; context.beginPath();
      this.motion.history.forEach((point, index) => {
        const x = 320 * (1 - (now - point.time) / (this.model.config.historySeconds * 1000));
        const y = 30 * (1 - Math.max(0, Math.min(1, (point.level - view.floor) / -view.floor)));
        if (index) context.lineTo(x, y); else context.moveTo(x, y);
      });
      context.stroke();
    }
    this.nodes[".cover"].style.transform = `scale${orientation === "vertical" ? "Y" : "X"}(${1 - displayed})`;
    this.nodes[".track"].setAttribute("aria-orientation", orientation);
    const track = this.nodes[".track"];
    track.setAttribute("aria-valuemin", view.floor);
    track.setAttribute("aria-valuemax", "0");
    track.setAttribute("aria-valuetext", view.level === null ? this.nodes[".status-text"].textContent : `${view.level.toFixed(1)} dBFS peak`);
    if (view.level === null) track.removeAttribute("aria-valuenow");
    else track.setAttribute("aria-valuenow", Math.max(view.floor, Math.min(0, view.level)));
    this.shadowRoot.querySelectorAll(".scale span").forEach((span, i) => { span.textContent = Math.round(view.floor * (1 - i / 3)); });
    if (!slow && ["signal", "silence", "unavailable"].includes(view.state))
      this.measurement.dom(this.model.session, this.model.sequence);
  }
}
if (!customElements.get("voicemeeter-channel-card")) customElements.define("voicemeeter-channel-card", VoicemeeterChannelCard);
