import { describeAdvanced } from "./advanced-controls.js";
// Metadata is trusted only within the configured authenticated MQTT namespace.
// Matching session IDs establish consistency, not cryptographic publisher identity.
export function validateMetadata(value) {
  if (value?.schema !== 2 || value.engine !== "potato" || typeof value.session_id !== "string" ||
      !value.session_id || value.session_id.length > 128 || !Array.isArray(value.sources) || value.sources.length > 16) return null;
  const ids = new Set(), sources = [];
  for (const source of value.sources) {
    const match = typeof source?.id === "string" && /^(strip|bus):([0-7])$/.exec(source.id);
    if (!match || ids.has(source.id) || source.kind !== match[1] || source.index !== Number(match[2]) ||
        typeof source.enabled !== "boolean" || typeof source.label !== "string" || source.label.length > 511 ||
        !Array.isArray(source.taps) || !source.taps.length || new Set(source.taps).size !== source.taps.length ||
        source.taps.some(tap => !(source.kind === "bus" ? ["output"] : ["pre", "post_mute"]).includes(tap))) return null;
    const controls = [], unique = new Set();
    if (source.controls !== undefined) {
      if (!Array.isArray(source.controls) || source.controls.length > 264) return null;
      for (const control of source.controls) {
        const descriptor = describeAdvanced(control?.id, source.id);
        if (!descriptor || unique.has(control.id) || control.kind !== descriptor.domain || control.group !== descriptor.group ||
            control.min !== descriptor.min || control.max !== descriptor.max || !Number.isFinite(control.step) || Math.abs(control.step - descriptor.step) > .00001 ||
            typeof control.discovery_unique_id !== "string" || control.discovery_unique_id.length > 256) return null;
        unique.add(control.id); controls.push(Object.freeze({ ...control }));
      }
    }
    const meterUnique = source.meter_discovery_unique_ids?.peak;
    const coreUnique = {};
    for (const [key, value] of Object.entries(source.core_discovery_unique_ids ?? {})) {
      if (!["gain", "mute", "solo", "a1", "a2", "a3", "a4", "a5", "b1", "b2", "b3"].includes(key) || typeof value !== "string" || value.length > 256) return null;
      coreUnique[key] = value;
    }
    ids.add(source.id);
    sources.push(Object.freeze({ id: source.id, kind: source.kind, index: source.index,
      enabled: source.enabled, label: source.label, controls: Object.freeze(controls), coreUnique: Object.freeze(coreUnique),
      meterUnique: typeof meterUnique === "string" && meterUnique.length <= 256 ? meterUnique : null,
      taps: Object.freeze([...source.taps]) }));
  }
  return Object.freeze({ schema: 2, session_id: value.session_id, sources: Object.freeze(sources) });
}

export class SessionGate {
  constructor() { this.metadata = null; this.session = null; this.sequence = -1; this.retired = new Set(); this.exhausted = false; }
  setMetadata(value) {
    const metadata = validateMetadata(value);
    if (!metadata || this.exhausted) { this.metadata = null; return null; }
    if (this.retired.has(metadata.session_id)) return null;
    if (this.session !== metadata.session_id) {
      if (this.session !== null) {
        if (this.retired.size >= 128) { this.exhausted = true; this.metadata = null; return null; }
        this.retired.add(this.session);
      }
      this.session = metadata.session_id; this.sequence = -1;
    }
    this.metadata = metadata;
    return metadata;
  }
  accept(frame) {
    if (!this.metadata || frame?.schema !== 2 || frame.session_id !== this.session ||
        !Number.isSafeInteger(frame.seq) || frame.seq <= this.sequence || !frame.sources ||
        typeof frame.sources !== "object" || Array.isArray(frame.sources)) return null;
    for (const [id, source] of Object.entries(frame.sources)) {
      const descriptor = this.metadata.sources.find(item => item.id === id);
      if (!descriptor?.enabled || !source || typeof source !== "object" || Array.isArray(source)) return null;
      for (const tap of ["pre", "post_mute", "output"])
        if (Object.hasOwn(source, `${tap}_dbfs`) && !descriptor.taps.includes(tap)) return null;
    }
    this.sequence = frame.seq;
    return { metadata: this.metadata, frame };
  }
}
