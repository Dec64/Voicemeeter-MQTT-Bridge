import { nativeSessionTelemetry } from "./native-session.js";

export class CardFeed {
  constructor(deliver, hub = nativeSessionTelemetry) { this.deliver = deliver; this.hub = hub; this.generation = 0; }
  update(connection, topic, enabled) {
    if (!enabled || !connection || !topic) { this.stop(); return; }
    if (this.connection === connection && this.topic === topic) return;
    this.stop(); this.connection = connection; this.topic = topic;
    const generation = this.generation;
    const deliver = value => { if (this.generation === generation) this.deliver(value); };
    deliver({ state: "waiting_metadata", metadata: null, frame: null });
    try { this.lease = this.hub.acquire(connection, topic, deliver, () => deliver({ state: "error", metadata: null, frame: null })); }
    catch { deliver({ state: "error", metadata: null, frame: null }); }
  }
  stop() {
    this.generation++;
    this.lease?.release(); this.lease = null; this.connection = null; this.topic = null;
  }
}
