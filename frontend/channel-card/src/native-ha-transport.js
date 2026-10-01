import { SharedTelemetry } from "./shared-telemetry.js";

export function validateNativeTopic(topic) {
  // HA renders MQTT trigger topics as templates, so template delimiters are forbidden.
  if (typeof topic !== "string" || !topic || topic !== topic.trim() || topic.length > 512 ||
      /[+#\0]/.test(topic) || /\{[{%#]/.test(topic)) throw new Error("A literal MQTT topic is required.");
  return topic;
}

// subscribeMessage unwraps the WebSocket event; this function receives event.variables.
// Publication age is a preliminary filter, not proof of sample age or session identity.
export function decodeNativeEvent(event, topic, now = Date.now()) {
  const trigger = event?.variables?.trigger;
  if (trigger?.platform !== "mqtt" || trigger.topic !== topic ||
      typeof trigger.payload !== "string" || trigger.payload.length > 65536 || !Number.isFinite(now)) return null;
  let frame;
  try { frame = JSON.parse(trigger.payload); } catch { return null; }
  if (!frame || frame.schema !== 2 || typeof frame.session_id !== "string" ||
      !frame.session_id || frame.session_id.length > 128 || !Number.isSafeInteger(frame.seq) || frame.seq < 0 ||
      typeof frame.published_at_utc !== "string" ||
      !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(frame.published_at_utc)) return null;
  const age = now - Date.parse(frame.published_at_utc);
  if (!Number.isFinite(age) || age < 0 || age >= 750) return null;
  if (!frame.sources || typeof frame.sources !== "object" || Array.isArray(frame.sources)) return null;
  for (const [id, source] of Object.entries(frame.sources)) {
    if (!/^(strip|bus):[0-7]$/.test(id) || !source || typeof source !== "object" || Array.isArray(source) ||
        typeof source.available !== "boolean") return null;
    const taps = id.startsWith("bus:") ? ["output_dbfs"] : ["pre_dbfs", "post_mute_dbfs"];
    for (const tap of taps) {
      const value = source[tap];
      if (value !== undefined && value !== null && (!Number.isFinite(value) || value < -200 || value > 60)) return null;
    }
  }
  return frame;
}

export function createNativeTelemetry(now = () => Date.now()) {
  return new SharedTelemetry(async (connection, topic, deliver) => {
    validateNativeTopic(topic);
    if (typeof connection.subscribeMessage !== "function") throw new Error("HA subscription API is unavailable.");
    return connection.subscribeMessage(event => {
      const frame = decodeNativeEvent(event, topic, now());
      if (frame) deliver(frame);
    }, { type: "subscribe_trigger", trigger: { platform: "mqtt", topic, qos: 0 } });
  });
}

// Reuse this singleton across cards. Wiring waits for metadata/session authorization.
export const nativeTelemetry = createNativeTelemetry();
