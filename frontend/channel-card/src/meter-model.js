// Voicemeeter MQTT Bridge. See repository LICENSE and upstream attribution.
export function normalizeConfig(config) {
  const id = config?.source?.id ?? "";
  if (typeof id !== "string" || (id !== "" && !/^(strip|bus):[0-7]$/.test(id)))
    throw new Error("Choose a canonical source: strip:0–7 or bus:0–7.");
  const tap = config?.meter?.mute_display_mode ?? "incoming";
  if (!["incoming", "post_mute"].includes(tap)) throw new Error("Choose incoming or post_mute.");
  if (id.startsWith("bus:") && config?.meter?.mute_display_mode !== undefined)
    throw new Error("Bus meters support output only; omit mute_display_mode.");
  const floor = config?.meter?.floor_dbfs ?? -90;
  if (!Number.isFinite(floor) || floor < -120 || floor > -20)
    throw new Error("Meter floor must be between -120 and -20 dBFS.");
  const label = config?.source?.display_name ?? id;
  if (typeof label !== "string" || label.length > 511) throw new Error("Display name must be text up to 511 characters.");
  return Object.freeze({ id, label, floor, tap: id.startsWith("bus:") ? "output" : tap === "incoming" ? "pre" : "post_mute" });
}

// Receives already-decoded aggregate fixtures. Real transport and session authorization
// are deliberately separate: a new session requires an explicit reset, never seq alone.
export class MeterModel {
  constructor(config) {
    this.config = normalizeConfig(config);
    this.reset();
  }
  reset() {
    this.session = null;
    this.sequence = -1;
    this.receivedAt = null;
    this.level = null;
  }
  accept(frame, now) {
    if (!Number.isFinite(now) || now < 0 || (this.receivedAt !== null && now < this.receivedAt)) return false;
    if (frame?.schema !== 2 || typeof frame.session_id !== "string" || !frame.session_id ||
        frame.session_id.length > 128 || !Number.isSafeInteger(frame.seq) || frame.seq < 0 ||
        !frame.sources || typeof frame.sources !== "object" || Array.isArray(frame.sources)) return false;
    if (this.session !== null && (frame.session_id !== this.session || frame.seq <= this.sequence)) return false;
    this.session = frame.session_id;
    this.sequence = frame.seq;
    this.receivedAt = now;
    const source = Object.hasOwn(frame.sources, this.config.id) ? frame.sources[this.config.id] : null;
    const level = source?.[`${this.config.tap}_dbfs`];
    this.level = source?.available === true && Number.isFinite(level) && level >= -200 && level <= 60 ? level : null;
    return true;
  }
  view(now) {
    const { id, label, floor, tap } = this.config;
    let state = !id ? "unconfigured" : this.receivedAt === null ? "waiting"
      : !Number.isFinite(now) || now < this.receivedAt || now - this.receivedAt >= 750 ? "stale"
      : this.level === null ? "unavailable" : this.level <= floor ? "silence" : "signal";
    const level = state === "signal" || state === "silence" ? this.level : null;
    return { id, label: label || "Choose a source", floor, tap, state, level,
      fill: level === null ? 0 : Math.max(0, Math.min(1, (level - floor) / -floor)) };
  }
}
