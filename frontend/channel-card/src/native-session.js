import { SharedTelemetry } from "./shared-telemetry.js";
import { decodeNativeEvent, validateNativeTopic } from "./native-ha-transport.js";
import { SessionGate } from "./session-gate.js";

function metadataPayload(event, topic) {
  const trigger = event?.variables?.trigger;
  if (trigger?.platform !== "mqtt" || trigger.topic !== topic || typeof trigger.payload !== "string" || trigger.payload.length > 65536) return null;
  try { return JSON.parse(trigger.payload); } catch { return null; }
}

export function createNativeSessionTelemetry(now = () => Date.now()) {
  return new SharedTelemetry(async (connection, baseTopic, deliver) => {
    const metadataTopic = validateNativeTopic(`${baseTopic}/v2/metadata`);
    const fastTopic = validateNativeTopic(`${baseTopic}/v2/meters/fast`);
    if (typeof connection.subscribeMessage !== "function" || typeof connection.addEventListener !== "function" ||
        typeof connection.removeEventListener !== "function") throw new Error("HA connection API is unavailable.");
    const gate = new SessionGate(), subscriptions = [];
    let closed = false, failed = false;
    const active = () => !closed && !failed && connection.connected !== false;
    const disconnected = () => {
      gate.setMetadata(null);
      if (!closed) deliver({ state: "disconnected", metadata: null, frame: null });
    };
    connection.addEventListener("disconnected", disconnected);
    try {
      subscriptions.push(await connection.subscribeMessage(event => {
        if (!active()) return;
        const previous = gate.metadata;
        const metadata = gate.setMetadata(metadataPayload(event, metadataTopic));
        // Ignore a retired-session replay, but actively clear readings on revocation.
        if (!metadata && gate.metadata === previous && previous !== null) return;
        deliver({ state: metadata ? "metadata" : "invalid_metadata", metadata: gate.metadata, frame: null });
      }, { type: "subscribe_trigger", trigger: { platform: "mqtt", topic: metadataTopic, qos: 1 } }));
      subscriptions.push(await connection.subscribeMessage(event => {
        if (!active()) return;
        const frame = decodeNativeEvent(event, fastTopic, now());
        const accepted = frame && gate.accept(frame);
        if (accepted) deliver({ state: "frame", ...accepted });
      }, { type: "subscribe_trigger", trigger: { platform: "mqtt", topic: fastTopic, qos: 0 } }));
      if (subscriptions.some(unsubscribe => typeof unsubscribe !== "function")) throw new Error("HA unsubscribe API is unavailable.");
    } catch {
      // Retain cleanup ownership after partial setup. Throwing here would discard it.
      failed = true; gate.setMetadata(null);
      deliver({ state: "error", metadata: null, frame: null });
    }
    return async () => {
      closed = true;
      connection.removeEventListener("disconnected", disconnected);
      const results = await Promise.allSettled(subscriptions.map(unsubscribe => Promise.resolve().then(() => unsubscribe())));
      if (results.some(result => result.status === "rejected")) throw new Error("HA subscription cleanup failed.");
    };
  });
}

export const nativeSessionTelemetry = createNativeSessionTelemetry();
