import { describeAdvanced, ADVANCED_GROUPS } from "./advanced-controls.js";
export const ROUTES = Object.freeze(["A1", "A2", "A3", "A4", "A5", "B1", "B2", "B3"]);

// Explicit entity overrides assert the source association; labels never resolve targets.
export function normalizeControls(config) {
  const id = config.source?.id ?? "", flags = config.controls ?? {}, entities = config.entities ?? {};
  for (const key of ["gain", "mute", "solo", "routing", ...ADVANCED_GROUPS])
    if (flags[key] !== undefined && typeof flags[key] !== "boolean") throw new Error("Control visibility must be true or false.");
  const routes = entities.routes ?? {};
  if (!routes || typeof routes !== "object" || Array.isArray(routes) || Object.keys(routes).some(key => !ROUTES.includes(key)))
    throw new Error("Routing mappings must use A1–A5 or B1–B3.");
  const candidates = [
    { key: "gain", label: "Gain", entity: entities.gain, domain: "number", enabled: flags.gain === true },
    { key: "mute", label: "Mute", entity: entities.mute, domain: "switch", enabled: flags.mute === true },
    { key: "solo", label: "Solo", entity: entities.solo, domain: "switch", enabled: flags.solo === true && id.startsWith("strip:") },
    ...ROUTES.map(route => ({ key: `route:${route}`, label: route, entity: routes[route], domain: "switch", enabled: flags.routing === true && id.startsWith("strip:") }))
  ];
  const advanced = entities.advanced ?? {};
  if (!advanced || typeof advanced !== "object" || Array.isArray(advanced) || Object.keys(advanced).length > 264) throw new Error("Advanced mappings must be an object with at most 264 controls.");
  for (const [key, entity] of Object.entries(advanced)) {
    const descriptor = describeAdvanced(key, id);
    if (!descriptor) throw new Error("Advanced control does not belong to this source or capability.");
    candidates.push({ ...descriptor, entity, enabled: flags[descriptor.group] === true });
  }
  const bindings = [], used = new Set(), warnings = [];
  for (const group of ADVANCED_GROUPS) if (flags[group] === true && !candidates.some(c => c.group === group && c.enabled)) warnings.push(`Enable ${group === 'eq_cells' ? 'parametric EQ cells' : group} discovery in Windows bridge Settings, then suggest entities in the card editor. Only supported processing is shown.`);
  const unfinished = Object.entries(flags).filter(([key, enabled]) => enabled === true && !["gain", "mute", "solo", "routing", ...ADVANCED_GROUPS].includes(key)).map(([key]) => key);
  if (unfinished.length) warnings.push(`Not implemented yet: ${unfinished.join(", ")}.`);
  if (flags.solo && id.startsWith("bus:")) warnings.push("Bus cards do not support strip solo.");
  if (flags.routing && id.startsWith("bus:")) warnings.push("Bus cards do not support strip routing.");
  if (flags.routing && id.startsWith("strip:") && !Object.values(routes).some(Boolean)) warnings.push("Select verified routing entities.");
  for (const item of candidates) {
    if (item.entity !== undefined && item.entity !== "" && (typeof item.entity !== "string" || !new RegExp(`^${item.domain}\\.[a-z0-9_]+$`).test(item.entity)))
      throw new Error(`${item.label} requires a ${item.domain} entity ID.`);
    if (!item.enabled || !/^(strip|bus):[0-7]$/.test(id)) continue;
    if (!item.entity) { if (!item.key.startsWith("route:")) warnings.push(`Select a verified ${item.label.toLowerCase()} entity.`); continue; }
    if (used.has(item.entity)) throw new Error("Each control must use a distinct entity.");
    used.add(item.entity); bindings.push(Object.freeze(item));
  }
  return Object.freeze({ id, bindings: Object.freeze(bindings), warnings: Object.freeze(warnings) });
}

export function readControl(binding, hass) {
  const unavailable = { ...binding, available: false, value: null };
  if (!hass || hass.connection?.connected === false || typeof hass.callService !== "function") return unavailable;
  const state = hass.states?.[binding.entity], service = hass.services?.[binding.domain];
  if (binding.domain === "switch") {
    if (!["on", "off"].includes(state?.state) || !service?.turn_on || !service?.turn_off) return unavailable;
    return { ...binding, available: true, value: state.state === "on" };
  }
  const { min, max, step, unit_of_measurement: unit } = state?.attributes ?? {};
  if (typeof state?.state !== "string" || !/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)$/.test(state.state) || !service?.set_value ||
      (unit !== undefined && unit !== (binding.advanced ? binding.unit ?? undefined : "dB")) || ![min, max, step].every(Number.isFinite) || min < (binding.advanced ? binding.min : -60) || max > (binding.advanced ? binding.max : 12) || min >= max || step <= 0 || step > max - min) return unavailable;
  const value = Number(state.state);
  if (!Number.isFinite(value) || value < min || value > max) return unavailable;
  return { ...binding, available: true, value, min, max, step };
}

export function controlRequest(view, value) {
  if (!view.available) return null;
  if (view.domain === "switch") return typeof value === "boolean"
    ? { domain: "switch", service: value ? "turn_on" : "turn_off", data: { entity_id: view.entity } } : null;
  if (!Number.isFinite(value) || value < view.min || value > view.max ||
      Math.abs((value - view.min) / view.step - Math.round((value - view.min) / view.step)) > 1e-6) return null;
  return { domain: "number", service: "set_value", data: { entity_id: view.entity, value } };
}
