// A conservative last-state-update budget, not evidence of the device's sample age.
export const SENSOR_FRESHNESS_MS = 15000;
export function slowSensorView(config, hass, now = Date.now()) {
  const entity = config.meters[config.tap];
  if (!config.id || !entity) return null;
  const value = hass?.states?.[entity];
  const unavailable = { level: null, state: "unavailable", fill: 0, expiresAt: null };
  if (hass?.connection?.connected === false || typeof value?.state !== "string" ||
      !/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)$/.test(value.state) || value.attributes?.unit_of_measurement !== "dBFS") return unavailable;
  const level = Number(value.state), updated = Date.parse(value.last_updated);
  if (!Number.isFinite(level) || level < -200 || level > 60 || !Number.isFinite(updated) || !Number.isFinite(now) || now < updated) return unavailable;
  const expiresAt = updated + SENSOR_FRESHNESS_MS;
  if (now >= expiresAt) return { ...unavailable, state: "stale" };
  return { level, state: level <= config.floor ? "silence" : "signal", expiresAt,
    fill: Math.max(0, Math.min(1, (level - config.floor) / -config.floor)) };
}
