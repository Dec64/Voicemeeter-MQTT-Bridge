// Voicemeeter MQTT Bridge. See repository LICENSE and upstream attribution.
const meterColors = {
  normal: "#78ff8e",
  warning: "#ffd466",
  clip: "#ff5656",
  track: "#07130c",
  peak: "#edffdf",
};
const controlColors = { accent: "#78ff8e", mute: "#ff756b", solo: "#ffd466" };
function palette(value, defaults) {
  if (
    value !== undefined &&
    (!value || typeof value !== "object" || Array.isArray(value))
  )
    throw new Error("Colors must be an object of hex colors.");
  const result = { ...defaults };
  for (const [key, color] of Object.entries(value ?? {})) {
    if (
      !Object.hasOwn(defaults, key) ||
      typeof color !== "string" ||
      !/^#(?:[\da-f]{3}|[\da-f]{6})$/i.test(color)
    )
      throw new Error(
        "Use supported color names and hex colors such as #78ff8e.",
      );
    result[key] = color;
  }
  return Object.freeze(result);
}
function flag(section, key, fallback) {
  if (section?.[key] !== undefined && typeof section[key] !== "boolean")
    throw new Error(`${key} must be true or false.`);
  return section?.[key] ?? fallback;
}
export function meterFraction(level, floor, ceiling = 12) {
  return Math.max(0, Math.min(1, (level - floor) / (ceiling - floor)));
}
export function normalizeConfig(config) {
  if (
    config?.diagnostics !== undefined &&
    typeof config.diagnostics !== "boolean"
  )
    throw new Error("Diagnostics must be true or false.");
  const topic = config?.bridge?.base_topic ?? "";
  if (
    typeof topic !== "string" ||
    /[+#\0]/.test(topic) ||
    /\{[{%#]/.test(topic) ||
    topic.length > 480 ||
    topic.endsWith("/") ||
    topic !== topic.trim()
  )
    throw new Error(
      "Use a literal MQTT base topic without wildcards or surrounding spaces.",
    );
  const transport = config?.bridge?.transport ?? "auto";
  if (!["auto", "native_ws", "entities_only"].includes(transport))
    throw new Error("Choose auto, native_ws or entities_only transport.");
  const id = config?.source?.id ?? "";
  if (typeof id !== "string" || (id !== "" && !/^(strip|bus):[0-7]$/.test(id)))
    throw new Error("Choose a canonical source: strip:0–7 or bus:0–7.");
  const tap = config?.meter?.mute_display_mode ?? "incoming";
  if (!["incoming", "post_mute"].includes(tap))
    throw new Error("Choose incoming or post_mute.");
  if (id.startsWith("bus:") && config?.meter?.mute_display_mode !== undefined)
    throw new Error("Bus meters support output only; omit mute_display_mode.");
  const floor = config?.meter?.floor_dbfs ?? -90;
  if (!Number.isFinite(floor) || floor < -120 || floor > -20)
    throw new Error("Meter floor must be between -120 and -20 dBFS.");
  const ceiling = config?.meter?.ceiling_dbfs ?? 12;
  if (!Number.isFinite(ceiling) || ceiling < 0 || ceiling > 24)
    throw new Error("Meter ceiling must be between 0 and +24 dBFS.");
  const historySeconds = config?.meter?.history_seconds ?? 5,
    holdMs = config?.meter?.peak_hold_ms ?? 1500;
  const showHistory = config?.meter?.show_history ?? false;
  if (
    !Number.isFinite(historySeconds) ||
    historySeconds < 3 ||
    historySeconds > 5 ||
    !Number.isFinite(holdMs) ||
    holdMs < 0 ||
    holdMs > 5000 ||
    typeof showHistory !== "boolean"
  )
    throw new Error(
      "Use 3-5 seconds of history, a 0-5000 ms hold and a boolean show_history.",
    );
  const label = config?.source?.display_name ?? id;
  const orientation = config?.meter?.orientation ?? "horizontal";
  const variant = config?.appearance?.variant ?? "standard";
  if (!["horizontal", "vertical"].includes(orientation))
    throw new Error("Choose horizontal or vertical orientation.");
  if (!["compact", "standard", "expanded"].includes(variant))
    throw new Error("Choose compact, standard or expanded layout.");
  if (typeof label !== "string" || label.length > 511)
    throw new Error("Display name must be text up to 511 characters.");
  const meters = config?.entities?.meters ?? {};
  if (
    !meters ||
    typeof meters !== "object" ||
    Array.isArray(meters) ||
    Object.entries(meters).some(
      ([key, value]) =>
        !["pre", "post_mute", "output"].includes(key) ||
        typeof value !== "string" ||
        (value !== "" && !/^sensor\.[a-z0-9_]+$/.test(value)),
    )
  )
    throw new Error("Map meter taps to sensor entity IDs.");
  return Object.freeze({
    id,
    label,
    topic,
    transport,
    floor,
    ceiling,
    orientation,
    variant,
    historySeconds,
    holdMs,
    showHistory,
    showPeakValue: flag(config?.meter, "show_peak_value", true),
    showScale: flag(config?.meter, "show_scale", true),
    showStatus: flag(config?.meter, "show_status", true),
    showPeakHold: flag(config?.meter, "show_peak_hold", true),
    showClip: flag(config?.meter, "show_clip", true),
    showSourceId: flag(config?.appearance, "show_source_id", false),
    showTap: flag(config?.appearance, "show_tap", false),
    colors: palette(config?.meter?.colors, meterColors),
    controlColors: palette(config?.appearance?.colors, controlColors),
    diagnostics: config?.diagnostics === true,
    meters: Object.freeze({ ...meters }),
    tap: id.startsWith("bus:")
      ? "output"
      : tap === "incoming"
        ? "pre"
        : "post_mute",
  });
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
    this.expiresAt = null;
    this.level = null;
  }
  accept(frame, now, publicationAgeMs = 0) {
    if (
      !Number.isFinite(publicationAgeMs) ||
      publicationAgeMs < 0 ||
      publicationAgeMs >= 750
    )
      return false;
    if (
      !Number.isFinite(now) ||
      now < 0 ||
      (this.receivedAt !== null && now < this.receivedAt)
    )
      return false;
    if (
      frame?.schema !== 2 ||
      typeof frame.session_id !== "string" ||
      !frame.session_id ||
      frame.session_id.length > 128 ||
      !Number.isSafeInteger(frame.seq) ||
      frame.seq < 0 ||
      !frame.sources ||
      typeof frame.sources !== "object" ||
      Array.isArray(frame.sources)
    )
      return false;
    if (
      this.session !== null &&
      (frame.session_id !== this.session || frame.seq <= this.sequence)
    )
      return false;
    this.session = frame.session_id;
    this.sequence = frame.seq;
    this.receivedAt = now;
    this.expiresAt = now + 750 - publicationAgeMs;
    const source = Object.hasOwn(frame.sources, this.config.id)
      ? frame.sources[this.config.id]
      : null;
    const level = source?.[`${this.config.tap}_dbfs`];
    this.level =
      source?.available === true &&
      Number.isFinite(level) &&
      level >= -200 &&
      level <= 60
        ? level
        : null;
    return true;
  }
  view(now) {
    const { id, label, floor, ceiling, tap } = this.config;
    let state = !id
      ? "unconfigured"
      : this.receivedAt === null
        ? "waiting"
        : !Number.isFinite(now) ||
            now < this.receivedAt ||
            now >= this.expiresAt
          ? "stale"
          : this.level === null
            ? "unavailable"
            : this.level <= floor
              ? "silence"
              : "signal";
    const level = state === "signal" || state === "silence" ? this.level : null;
    return {
      id,
      label: label || "Choose a source",
      floor,
      ceiling,
      tap,
      state,
      level,
      fill: level === null ? 0 : meterFraction(level, floor, ceiling),
    };
  }
}
