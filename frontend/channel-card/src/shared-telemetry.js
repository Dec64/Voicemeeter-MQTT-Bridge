// One registry instance per browser module; connection keys do not keep HA sessions alive.
export class SharedTelemetry {
  constructor(open, maximumObjects = 1024) {
    if (!Number.isSafeInteger(maximumObjects) || maximumObjects < 1 || maximumObjects > 8192) throw new Error("Invalid telemetry complexity limit.");
    this.open = open; this.maximumObjects = maximumObjects; this.connections = new WeakMap();
  }
  acquire(connection, topic, onFrame, onError = () => {}) {
    if (!connection || (typeof connection !== "object" && typeof connection !== "function")) throw new Error("A connection is required.");
    if (typeof topic !== "string" || !topic.trim() || /[+#\0]/.test(topic)) throw new Error("A literal MQTT topic is required.");
    if (typeof onFrame !== "function" || typeof onError !== "function") throw new Error("Callbacks are required.");
    let topics = this.connections.get(connection);
    if (!topics) { topics = new Map(); this.connections.set(connection, topics); }
    let entry = topics.get(topic);
    if (!entry) {
      entry = { connection, topic, topics, listeners: new Set(), unsubscribe: null, task: null, state: "idle", error: null, generation: 0 };
      topics.set(topic, entry);
    }
    const listener = { onFrame, onError };
    entry.listeners.add(listener);
    if (entry.error) this.report(listener, entry.error);
    const ready = this.reconcile(entry);
    let released = false;
    return { ready, release: () => {
      if (!released) { released = true; entry.listeners.delete(listener); }
      return this.reconcile(entry);
    } };
  }
  report(listener, error) {
    try { listener.onError(error); } catch { /* A consumer cannot disrupt sibling cleanup. */ }
  }
  reconcile(entry) {
    if (entry.task) return entry.task;
    // Defer execution so task ownership is established even if open throws synchronously.
    entry.task = Promise.resolve().then(async () => {
      while (!entry.error) {
        if (entry.listeners.size && !entry.unsubscribe) {
          entry.state = "opening";
          const generation = ++entry.generation;
          const unsubscribe = await this.open(entry.connection, entry.topic, frame => {
            if (generation !== entry.generation) return;
            if (entry.state !== "opening" && entry.state !== "open") return;
            let immutable;
            try {
              immutable = structuredClone(frame);
              const pending = [immutable], seen = new Set();
              while (pending.length) {
                const value = pending.pop();
                if (!value || typeof value !== "object" || seen.has(value)) continue;
                if (seen.size >= this.maximumObjects) throw new Error("Telemetry frame is too complex.");
                seen.add(value); Object.freeze(value); pending.push(...Object.values(value));
              }
            } catch (error) {
              for (const listener of entry.listeners) this.report(listener, error);
              return;
            }
            for (const listener of [...entry.listeners]) {
              if (!entry.listeners.has(listener)) continue;
              try { listener.onFrame(immutable); } catch (error) { this.report(listener, error); }
            }
          });
          if (typeof unsubscribe !== "function") throw new Error("Transport did not return an unsubscribe function.");
          entry.unsubscribe = unsubscribe; entry.state = "open";
        } else if (!entry.listeners.size && entry.unsubscribe) {
          entry.state = "closing";
          entry.generation++;
          await entry.unsubscribe();
          entry.unsubscribe = null; entry.state = "idle";
        } else break;
      }
    }).catch(error => {
      entry.error = error; entry.state = "failed";
      for (const listener of entry.listeners) this.report(listener, error);
    }).finally(() => {
      entry.task = null;
      // A failed close keeps the entry blocked: reopening could duplicate a real subscription.
      if (!entry.listeners.size && !entry.unsubscribe && entry.topics.get(entry.topic) === entry)
        entry.topics.delete(entry.topic);
    });
    return entry.task;
  }
}
