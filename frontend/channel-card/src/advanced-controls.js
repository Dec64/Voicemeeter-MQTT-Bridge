// Vendor ranges mirror AdvancedControlRegistry; backend remains command authority.
const processing = {
  comp: ["compressor", "Compression", 0, 10, 0.1],
  comp_gainin: ["compressor", "Input gain", -24, 24, 0.1, "dB"],
  comp_ratio: ["compressor", "Ratio", 1, 8, 0.1],
  comp_threshold: ["compressor", "Threshold", -40, -3, 0.1, "dB"],
  comp_attack: ["compressor", "Attack", 0, 200, 1, "ms"],
  comp_release: ["compressor", "Release", 0, 5000, 1, "ms"],
  comp_knee: ["compressor", "Knee", 0, 1, 0.01],
  comp_gainout: ["compressor", "Output gain", -24, 24, 0.1, "dB"],
  comp_makeup: ["compressor", "Automatic makeup", 0, 1, 1, null, true],
  gate: ["gate", "Gate", 0, 10, 0.1],
  gate_threshold: ["gate", "Threshold", -60, -10, 0.1, "dB"],
  gate_damping: ["gate", "Maximum damping", -60, -10, 0.1, "dB"],
  gate_bpsidechain: ["gate", "Sidechain band pass", 100, 4000, 1, "Hz"],
  gate_attack: ["gate", "Attack", 0, 1000, 1, "ms"],
  gate_hold: ["gate", "Hold", 0, 5000, 1, "ms"],
  gate_release: ["gate", "Release", 0, 5000, 1, "ms"],
  denoiser: ["denoiser", "Native denoiser", 0, 10, 0.1],
  denoiser_threshold: ["denoiser", "Noise floor threshold", 0, 10, 0.1],
};
export const ADVANCED_GROUPS = Object.freeze([
  "mono",
  "compressor",
  "gate",
  "denoiser",
  "eq",
  "eq_cells",
]);
// Voicemeeter SDK VoicemeeterRemote.h VMRT_EQTYPE_* constants.
const filterTypes = Object.freeze(
  [
    "Bell / parametric",
    "Notch",
    "Band pass",
    "Low pass",
    "High pass",
    "Low shelf",
    "High shelf",
  ].map((label, value) => Object.freeze({ label, value })),
);
export function describeAdvanced(id, source) {
  const owner = /^(strip|bus):([0-7])$/.exec(source);
  if (!owner || typeof id !== "string") return null;
  const prefix = `${owner[1]}_${owner[2]}_`;
  if (!id.startsWith(prefix)) return null;
  const suffix = id.slice(prefix.length),
    physical = owner[1] === "strip" && Number(owner[2]) < 5,
    bus = owner[1] === "bus";
  let spec, channel, cellIndex;
  if (suffix === "mono") spec = ["mono", "Mono", 0, 1, 1, null, true];
  else if (physical && processing[suffix]) spec = processing[suffix];
  else if (!physical && !bus && /^eqgain[1-3]$/.test(suffix))
    spec = ["eq", `EQ band ${suffix.at(-1)}`, -12, 12, 0.1, "dB"];
  else if ((physical || bus) && ["eq_on", "eq_ab"].includes(suffix))
    spec = [
      "eq",
      suffix === "eq_on" ? "EQ enabled" : "EQ memory B",
      0,
      1,
      1,
      null,
      true,
    ];
  else if (physical || bus) {
    const cell = /^eq_channel_([0-7])_cell_([0-5])_(on|type|f|gain|q)$/.exec(
      suffix,
    );
    if (cell) {
      channel = Number(cell[1]);
      cellIndex = Number(cell[2]);
      const definitions = {
        on: ["Enabled", 0, 1, 1, null, true],
        type: ["Filter", 0, 6, 1],
        f: ["Frequency", 20, 20000, 1, "Hz"],
        gain: ["Gain", -12, 12, 0.1, "dB"],
        q: ["Q", 1, 100, 0.1],
      };
      const d = definitions[cell[3]];
      spec = ["eq_cells", d[0], ...d.slice(1)];
    }
  }
  if (!spec) return null;
  const [group, label, min, max, step, unit, toggle] = spec;
  const input = toggle
    ? "button"
    : suffix.endsWith("_type")
      ? "select"
      : unit === "Hz"
        ? "frequency"
        : unit === "ms" || suffix.endsWith("_q") || suffix === "comp_ratio"
          ? "number"
          : "fader";
  return Object.freeze({
    id,
    group,
    label,
    min,
    max,
    step,
    unit: unit ?? null,
    domain: toggle ? "switch" : "number",
    input,
    ...(channel === undefined ? {} : { channel, cell: cellIndex }),
    ...(input === "select" ? { choices: filterTypes } : {}),
    advanced: true,
    key: `advanced:${id}`,
  });
}
export function resolveRegistryEntities(descriptors, registry) {
  const result = {};
  if (!Array.isArray(registry)) return result;
  for (const descriptor of descriptors ?? []) {
    if (typeof descriptor?.discovery_unique_id !== "string") continue;
    const matches = registry.filter(
      (e) =>
        e.platform === "mqtt" &&
        e.unique_id === descriptor.discovery_unique_id &&
        !e.disabled_by &&
        /^(number|switch)\.[a-z0-9_]+$/.test(e.entity_id),
    );
    if (matches.length === 1) result[descriptor.id] = matches[0].entity_id;
  }
  return result;
}
