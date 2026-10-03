// Voicemeeter MQTT Bridge. See repository LICENSE and upstream attribution.
import { MeterModel, meterFraction } from "./meter-model.js";
import { cardStyles } from "./card-styles.js";
import { CardFeed } from "./card-feed.js";
import { slowSensorView } from "./slow-sensor.js";
import { VisibleRenderer } from "./visible-renderer.js";
import { ControlPanel } from "./control-panel.js";
import { normalizeControls } from "./control-model.js";
import { MeasurementPanel } from "./measurement-panel.js";
import { PeakMotion } from "./peak-motion.js";
import "./channel-card-editor.js";

const statusLabels = {
  unconfigured: "Choose a source",
  waiting: "Waiting for data",
  stale: "Stale data",
  unavailable: "Unavailable",
  silence: "Silence",
  signal: "Signal",
};
const tapLabels = {
  pre: "Incoming · pre-fader",
  post_mute: "After mute",
  output: "Bus output",
};
const streamLabels = {
  waiting_metadata: "Waiting for metadata",
  metadata: "Waiting for matching data",
  invalid_metadata: "Invalid metadata",
  error: "Stream unavailable",
  disconnected: "HA disconnected",
  unsupported: "Source or tap unavailable",
};

export class VoicemeeterChannelCard extends HTMLElement {
  constructor() {
    super();
    this.attachShadow({ mode: "open" });
    // Only static markup enters innerHTML. Configuration and readings use textContent.
    this.shadowRoot.innerHTML = `
      <style>${cardStyles}</style>
      <article data-state="unconfigured"><header><div class="identity"><div class="id"></div><h2></h2></div>
      <div class="reading"><div class="value">—</div><div class="unit">PEAK · dBFS</div></div></header>
      <div class="meter"><div class="track" role="meter" aria-label="Combined peak level"><div class="color"></div><div class="cover"></div><div class="grid"></div><div class="peak-marker" hidden></div></div>
      <div class="scale" aria-hidden="true"><span></span><span></span><span></span><span>0</span></div></div>
      <canvas class="history" width="320" height="30" aria-label="Recent sampled peaks" hidden></canvas><div class="clip" role="status" title="A real observed peak reached the clipping threshold; held for two seconds." hidden>CLIP</div>
      <div class="controls-root"></div><footer><span class="status"><span class="dot" aria-hidden="true"></span><span class="status-text"></span></span><span class="tap"></span></footer><div class="measurement-root" hidden></div></article>`;
    this.nodes = Object.fromEntries(
      [
        "article",
        "h2",
        ".id",
        ".value",
        ".track",
        ".cover",
        ".status-text",
        ".tap",
        ".peak-marker",
        ".clip",
        ".history",
      ].map((selector) => [selector, this.shadowRoot.querySelector(selector)]),
    );
    this.feed = new CardFeed((value) => this.receiveTelemetry(value));
    this.renderer = new VisibleRenderer(() => this.paint());
    this.controls = new ControlPanel(
      this.shadowRoot.querySelector(".controls-root"),
      () => this.render(),
    );
    this.measurement = new MeasurementPanel(
      this.shadowRoot.querySelector(".measurement-root"),
    );
    this.visible = false;
    this.visibilityChanged = () => this.updateVisibility();
    this.setConfig({});
  }
  static getStubConfig() {
    return { type: "custom:voicemeeter-channel-card", source: { id: "" } };
  }
  static getConfigElement() {
    return document.createElement("voicemeeter-channel-card-editor");
  }
  getCardSize() {
    return this.model.config.orientation === "vertical"
      ? 7
      : this.model.config.variant === "compact"
        ? 3
        : 4;
  }
  getGridOptions() {
    return { columns: 6, min_columns: 3 };
  }
  setConfig(config) {
    const model = new MeterModel(config);
    normalizeControls(config); // Validate before changing the existing card lifecycle.
    this.controls.configure(config);
    this.controls.setHass(this.ha);
    this.feed.stop();
    this.descriptor = null;
    this.streamStatus = null;
    this.clearTimer();
    this.model = model;
    this.slowReading = null;
    this.motion = new PeakMotion(
      model.config.floor,
      model.config.historySeconds,
      model.config.holdMs,
    );
    this.measurement.configure(model.config);
    this.render();
    this.updateFeed();
  }
  set hass(value) {
    this.ha = value;
    this.controls.setHass(value);
    this.updateFeed();
    this.render();
  }
  updateFeed() {
    const { topic, transport, id } = this.model.config;
    this.feed.update(
      this.ha?.connection,
      topic,
      this.visible && !!id && transport !== "entities_only",
    );
  }
  receiveTelemetry(event) {
    if (!this.visible) return;
    const { id, tap } = this.model.config;
    this.descriptor = event.metadata?.sources.find(
      (source) => source.id === id,
    );
    if (
      event.metadata &&
      (!this.descriptor?.enabled || !this.descriptor.taps.includes(tap))
    ) {
      this.clearTimer();
      this.model.reset();
      this.streamStatus = "unsupported";
      this.render();
      return;
    }
    if (!event.frame) {
      this.clearTimer();
      this.model.reset();
      this.streamStatus = event.state;
      this.render();
      return;
    }
    if (this.model.session !== event.frame.session_id) this.model.reset();
    this.streamStatus = null;
    if (
      this.setFrame(
        event.frame,
        Date.now() - Date.parse(event.frame.published_at_utc),
      )
    )
      this.measurement.receive(event);
  }
  // Fixture seam. Native frames first pass metadata/session checks in receiveTelemetry.
  setFrame(frame, publicationAgeMs = 0) {
    const previousSession = this.model.session;
    if (
      !this.visible ||
      !this.model.accept(frame, performance.now(), publicationAgeMs)
    )
      return false;
    if (previousSession !== this.model.session) this.motion.reset();
    this.clearTimer();
    const accepted = this.model.view(this.model.receivedAt);
    if (accepted.level !== null)
      this.motion.observe(accepted.level, this.model.receivedAt);
    else this.motion.reset();
    this.render();
    const expire = () => {
      this.render();
      const remaining = this.model.expiresAt - performance.now();
      if (remaining > 0)
        this.expiryTimer = setTimeout(expire, Math.ceil(remaining));
      else this.expiryTimer = undefined;
    };
    this.expiryTimer = setTimeout(
      expire,
      Math.max(0, this.model.expiresAt - performance.now()),
    );
    return true;
  }
  connectedCallback() {
    document.addEventListener("visibilitychange", this.visibilityChanged);
    this.inViewport = typeof IntersectionObserver === "undefined";
    if (!this.inViewport) {
      const observer = new IntersectionObserver((entries) => {
        if (this.observer !== observer || !this.isConnected) return;
        this.inViewport = entries.some(
          (entry) => entry.target === this && entry.isIntersecting,
        );
        this.updateVisibility();
      });
      this.observer = observer;
      observer.observe(this);
    }
    this.updateVisibility();
  }
  disconnectedCallback() {
    this.observer?.disconnect();
    this.observer = null;
    document.removeEventListener("visibilitychange", this.visibilityChanged);
    this.updateVisibility();
  }
  updateVisibility() {
    const visible = this.isConnected && this.inViewport && !document.hidden;
    if (visible === this.visible) return;
    this.visible = visible;
    this.renderer.setActive(visible);
    this.controls.setActive(visible);
    this.measurement.setActive(visible);
    if (!visible) {
      this.feed.stop();
      this.clearTimer();
      this.model.reset();
      this.motion.reset();
      this.descriptor = null;
      this.streamStatus = null;
    } else {
      this.updateFeed();
      this.render();
    }
  }
  clearTimer() {
    clearTimeout(this.expiryTimer);
    this.expiryTimer = undefined;
    clearTimeout(this.sensorTimer);
    this.sensorTimer = undefined;
  }
  render() {
    this.renderer.request();
  }
  paint() {
    if (!this.visible) return;
    this.controls.paint();
    let view = this.model.view(performance.now());
    const { transport } = this.model.config;
    const slow =
      transport === "entities_only" ||
      (transport === "auto" && ["waiting", "stale"].includes(view.state))
        ? slowSensorView(this.model.config, this.ha)
        : null;
    clearTimeout(this.sensorTimer);
    this.sensorTimer = undefined;
    if (slow) {
      view = { ...view, ...slow };
      if (this.isConnected && slow.expiresAt !== null)
        this.sensorTimer = setTimeout(
          () => this.render(),
          Math.max(1, slow.expiresAt - Date.now()),
        );
    }
    this.nodes.article.dataset.state = view.state;
    const config = this.model.config;
    const { orientation, variant } = config;
    for (const [key, color] of Object.entries(config.colors))
      this.nodes.article.style.setProperty(`--vm-meter-${key}`, color);
    for (const [key, color] of Object.entries(config.controlColors))
      this.nodes.article.style.setProperty(`--vm-${key}`, color);
    this.nodes.article.style.setProperty(
      "--vm-warning-start",
      `${meterFraction(-6, view.floor, config.ceiling) * 100}%`,
    );
    this.nodes.article.style.setProperty(
      "--vm-clip-start",
      `${meterFraction(0, view.floor, config.ceiling) * 100}%`,
    );
    this.nodes.article.dataset.orientation = orientation;
    this.nodes.article.dataset.variant = variant;
    this.nodes.h2.textContent =
      ((!view.label || view.label === view.id) && this.descriptor?.label) ||
      view.label ||
      view.id;
    this.nodes[".id"].textContent = view.id
      ? `${view.id.startsWith("bus:") ? "OUTPUT" : "INPUT"} / ${view.id}`
      : "UNASSIGNED";
    this.nodes[".value"].textContent =
      view.level === null ? "—" : Math.max(view.floor, view.level).toFixed(1);
    this.shadowRoot.querySelector(".reading").hidden = !config.showPeakValue;
    this.nodes[".id"].hidden = !config.showSourceId;
    this.nodes[".status-text"].textContent =
      slow && view.level === null
        ? `Slow sensor · ${statusLabels[view.state]}`
        : (streamLabels[this.streamStatus] ?? statusLabels[view.state]);
    const status = this.shadowRoot.querySelector(".status");
    status.hidden = !config.showStatus;
    status.title = slow
      ? "Slow sensor fallback; reduced freshness."
      : this.nodes[".status-text"].textContent;
    this.nodes[".tap"].textContent = tapLabels[view.tap];
    this.nodes[".tap"].hidden = !config.showTap;
    this.nodes.article.title = `${view.id || "Unassigned source"} · ${tapLabels[view.tap]}`;
    this.shadowRoot.querySelector("footer").hidden =
      !config.showStatus && !config.showTap;
    let displayed = view.fill;
    const marker = this.nodes[".peak-marker"],
      clip = this.nodes[".clip"];
    if (view.level !== null) {
      if (
        slow &&
        this.slowReading !==
          `${this.model.session}:${slow.expiresAt}:${slow.level}`
      ) {
        if (this.slowReading === null) this.motion.reset();
        this.motion.observe(slow.level, performance.now());
        this.slowReading = `${this.model.session}:${slow.expiresAt}:${slow.level}`;
      }
      if (!slow) this.slowReading = null;
      const motion = this.motion.view(
        performance.now(),
        matchMedia("(prefers-reduced-motion: reduce)").matches,
      );
      if (motion.level !== null)
        displayed = meterFraction(motion.level, view.floor, config.ceiling);
      marker.hidden =
        !config.showPeakHold ||
        motion.hold === null ||
        motion.hold <= view.floor;
      const percent = Math.min(
        99.5,
        meterFraction(motion.hold, view.floor, config.ceiling) * 100,
      );
      marker.style.cssText =
        orientation === "vertical" ? `bottom:${percent}%` : `left:${percent}%`;
      clip.hidden = !config.showClip || !motion.clipping;
      if (motion.animate) this.renderer.request();
    } else {
      marker.hidden = true;
      clip.hidden = true;
      this.motion.reset();
      this.slowReading = null;
    }
    const canvas = this.nodes[".history"];
    canvas.hidden = !this.model.config.showHistory || !!slow;
    canvas.style.cssText = "width:100%;height:30px;margin-top:12px";
    if (!canvas.hidden) {
      const context = canvas.getContext("2d"),
        now = performance.now();
      context.clearRect(0, 0, 320, 30);
      context.strokeStyle = config.colors.normal;
      context.beginPath();
      this.motion.history.forEach((point, index) => {
        const x =
          320 *
          (1 - (now - point.time) / (this.model.config.historySeconds * 1000));
        const y =
          30 * (1 - meterFraction(point.level, view.floor, config.ceiling));
        if (index) context.lineTo(x, y);
        else context.moveTo(x, y);
      });
      context.stroke();
    }
    this.nodes[".cover"].style.transform =
      `scale${orientation === "vertical" ? "Y" : "X"}(${1 - displayed})`;
    this.nodes[".track"].setAttribute("aria-orientation", orientation);
    const track = this.nodes[".track"];
    track.setAttribute("aria-valuemin", view.floor);
    track.setAttribute("aria-valuemax", config.ceiling);
    track.setAttribute(
      "aria-valuetext",
      view.level === null
        ? this.nodes[".status-text"].textContent
        : `${view.level.toFixed(1)} dBFS peak`,
    );
    if (view.level === null) track.removeAttribute("aria-valuenow");
    else
      track.setAttribute(
        "aria-valuenow",
        Math.max(view.floor, Math.min(config.ceiling, view.level)),
      );
    const scale = this.shadowRoot.querySelector(".scale");
    scale.hidden = !config.showScale;
    const ticks = [
      view.floor,
      ...[-60, -40, -20, -10, 0].filter(
        (value) => value > view.floor && value < config.ceiling,
      ),
      config.ceiling,
    ];
    if (scale.dataset.ticks !== ticks.join(",")) {
      scale.replaceChildren(
        ...ticks.map((value) => {
          const span = document.createElement("span");
          span.textContent = value > 0 ? `+${value}` : String(value);
          span.dataset.value = value;
          return span;
        }),
      );
      scale.dataset.ticks = ticks.join(",");
    }
    for (const span of scale.children) {
      const percent =
        meterFraction(Number(span.dataset.value), view.floor, config.ceiling) *
        100;
      span.style.cssText =
        orientation === "vertical" ? `bottom:${percent}%` : `left:${percent}%`;
    }
    if (!slow && ["signal", "silence", "unavailable"].includes(view.state))
      this.measurement.dom(this.model.session, this.model.sequence);
  }
}
if (!customElements.get("voicemeeter-channel-card"))
  customElements.define("voicemeeter-channel-card", VoicemeeterChannelCard);
