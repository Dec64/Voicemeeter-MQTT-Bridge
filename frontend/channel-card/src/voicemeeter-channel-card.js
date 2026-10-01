// Voicemeeter MQTT Bridge. See repository LICENSE and upstream attribution.
import { MeterModel } from "./meter-model.js";
import { CardFeed } from "./card-feed.js";
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
      <div class="meter"><div class="track" role="meter" aria-label="Combined peak level"><div class="color"></div><div class="cover"></div><div class="grid"></div></div>
      <div class="scale" aria-hidden="true"><span></span><span></span><span></span><span>0</span></div></div>
      <footer><span class="status"><span class="dot" aria-hidden="true"></span><span class="status-text"></span></span><span class="tap"></span></footer></article>`;
    this.nodes = Object.fromEntries(["article", "h2", ".id", ".value", ".track", ".cover", ".status-text", ".tap"]
      .map(selector => [selector, this.shadowRoot.querySelector(selector)]));
    this.feed = new CardFeed(value => this.receiveTelemetry(value));
    this.setConfig({});
  }
  static getStubConfig() { return { type: "custom:voicemeeter-channel-card", source: { id: "" } }; }
  static getConfigElement() { return document.createElement("voicemeeter-channel-card-editor"); }
  getCardSize() { return this.model.config.orientation === "vertical" ? 7 : this.model.config.variant === "compact" ? 3 : 4; }
  getGridOptions() { return { columns: 6, min_columns: 3 }; }
  setConfig(config) {
    const model = new MeterModel(config);
    this.feed.stop(); this.descriptor = null; this.streamStatus = null;
    this.clearTimer();
    this.model = model;
    this.render();
    this.updateFeed();
  }
  set hass(value) { this.ha = value; this.updateFeed(); }
  updateFeed() {
    const { topic, transport, id } = this.model.config;
    this.feed.update(this.ha?.connection, topic, this.isConnected && !!id && transport !== "entities_only");
  }
  receiveTelemetry(event) {
    if (!this.isConnected) return;
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
    this.setFrame(event.frame, Date.now() - Date.parse(event.frame.published_at_utc));
  }
  // Fixture seam. Native frames first pass metadata/session checks in receiveTelemetry.
  setFrame(frame, publicationAgeMs = 0) {
    if (!this.isConnected || !this.model.accept(frame, performance.now(), publicationAgeMs)) return false;
    this.render();
    this.clearTimer();
    const expire = () => {
      this.render();
      const remaining = this.model.expiresAt - performance.now();
      if (remaining > 0) this.expiryTimer = setTimeout(expire, Math.ceil(remaining));
      else this.expiryTimer = undefined;
    };
    this.expiryTimer = setTimeout(expire, Math.max(0, this.model.expiresAt - performance.now()));
    return true;
  }
  connectedCallback() { this.render(); this.updateFeed(); }
  disconnectedCallback() { this.feed.stop(); this.clearTimer(); this.model.reset(); this.descriptor = null; this.streamStatus = null; }
  clearTimer() { clearTimeout(this.expiryTimer); this.expiryTimer = undefined; }
  render() {
    const view = this.model.view(performance.now());
    this.nodes.article.dataset.state = view.state;
    const { orientation, variant } = this.model.config;
    this.nodes.article.dataset.orientation = orientation;
    this.nodes.article.dataset.variant = variant;
    this.nodes.h2.textContent = (view.label === view.id && this.descriptor?.label) || view.label;
    this.nodes[".id"].textContent = view.id ? `${view.id.startsWith("bus:") ? "OUTPUT" : "INPUT"} / ${view.id}` : "UNASSIGNED";
    this.nodes[".value"].textContent = view.level === null ? "—" : Math.max(view.floor, view.level).toFixed(1);
    this.nodes[".status-text"].textContent = streamLabels[this.streamStatus] ?? statusLabels[view.state];
    this.nodes[".tap"].textContent = tapLabels[view.tap];
    this.nodes[".cover"].style.transform = `scale${orientation === "vertical" ? "Y" : "X"}(${1 - view.fill})`;
    this.nodes[".track"].setAttribute("aria-orientation", orientation);
    const track = this.nodes[".track"];
    track.setAttribute("aria-valuemin", view.floor);
    track.setAttribute("aria-valuemax", "0");
    track.setAttribute("aria-valuetext", view.level === null ? this.nodes[".status-text"].textContent : `${view.level.toFixed(1)} dBFS peak`);
    if (view.level === null) track.removeAttribute("aria-valuenow");
    else track.setAttribute("aria-valuenow", Math.max(view.floor, Math.min(0, view.level)));
    this.shadowRoot.querySelectorAll(".scale span").forEach((span, i) => { span.textContent = Math.round(view.floor * (1 - i / 3)); });
  }
}
if (!customElements.get("voicemeeter-channel-card")) customElements.define("voicemeeter-channel-card", VoicemeeterChannelCard);
