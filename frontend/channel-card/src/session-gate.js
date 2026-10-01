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
    ids.add(source.id);
    sources.push(Object.freeze({ id: source.id, kind: source.kind, index: source.index,
      enabled: source.enabled, label: source.label, taps: Object.freeze([...source.taps]) }));
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
