import { SharedTelemetry } from "./shared-telemetry.js";
import { decodeNativeEvent, validateNativeTopic } from "./native-ha-transport.js";
import { SessionGate } from "./session-gate.js";

function metadataPayload(event, topic) {
  const trigger = event?.variables?.trigger;
  if (trigger?.platform !== "mqtt" || trigger.topic !== topic || typeof trigger.payload !== "string" || trigger.payload.length > 1048576) return null;
  try { return JSON.parse(trigger.payload); } catch { return null; }
}

export function createNativeSessionTelemetry(now = () => Date.now(), monotonic = () => performance.now()) {
  return new SharedTelemetry(async (connection, baseTopic, deliver) => {
    const metadataTopic = validateNativeTopic(`${baseTopic}/v2/metadata`);
    const fastTopic = validateNativeTopic(`${baseTopic}/v2/meters/fast`);
    if (typeof connection.subscribeMessage !== "function" || typeof connection.addEventListener !== "function" ||
        typeof connection.removeEventListener !== "function") throw new Error("HA connection API is unavailable.");
    const gate = new SessionGate(), subscriptions = [];
    const transport = { fast_events: 0, accepted: 0, decoder_rejected: 0, session_rejected: 0 };
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
        // Capture once before parsing/fan-out. All cards measure from the same
        // callback entry rather than each inventing a later receipt timestamp.
        const receivedMonoMs = monotonic(), receivedUtcMs = now();
        transport.fast_events++;
        const frame = decodeNativeEvent(event, fastTopic, receivedUtcMs);
        if (!frame) { transport.decoder_rejected++; return; }
        const accepted = gate.accept(frame);
        if (!accepted) { transport.session_rejected++; return; }
        transport.accepted++;
        deliver({ state: "frame", ...accepted, transport: { ...transport }, timing: {
          session: frame.session_id, sequence: frame.seq, receivedMonoMs, receivedUtcMs,
          publishedUtcMs: Date.parse(frame.published_at_utc) } });
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
  }, 8192); // Full capability metadata exceeds the ordinary small-frame object budget.
}

export const nativeSessionTelemetry = createNativeSessionTelemetry();
