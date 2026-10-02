import { readControl, controlRequest } from "./control-model.js";

export class ControlCommands {
  constructor(changed, schedule = (callback, delay) => setTimeout(callback, delay), cancel = id => clearTimeout(id)) {
    this.changed = changed; this.schedule = schedule; this.cancel = cancel; this.requests = new Map();
  }
  configure(config) { this.dispose(); this.config = config; }
  update(hass) {
    this.hass = hass;
    for (const [key, request] of this.requests) {
      const binding = this.config.bindings.find(item => item.key === key);
      if (request.phase === "pending" && request.settled && binding && readControl(binding, hass).value === request.expected) {
        this.cancel(request.timer); this.requests.delete(key);
      }
    }
  }
  status(key) {
    const request = this.requests.get(key);
    return request ? { phase: request.phase, busy: !request.settled || request.phase === "pending" } : { phase: "idle", busy: false };
  }
  send(key, value) {
    if (this.status(key).busy) return false;
    const binding = this.config?.bindings.find(item => item.key === key);
    if (!binding) return false;
    const view = readControl(binding, this.hass), command = controlRequest(view, value);
    if (!command || view.value === value) return false;
    const request = { expected: value, phase: "pending", settled: false, timer: null };
    this.requests.set(key, request);
    const current = () => this.requests.get(key) === request;
    request.timer = this.schedule(() => {
      if (current()) { request.phase = "timeout"; this.changed(); }
    }, 3000);
    this.changed();
    const finish = success => {
      if (!current()) return;
      request.settled = true;
      if (!success) { request.phase = "error"; this.cancel(request.timer); }
      else this.update(this.hass);
      this.changed();
    };
    try {
      Promise.resolve(this.hass.callService(command.domain, command.service, command.data)).then(() => finish(true), () => finish(false));
    } catch { finish(false); }
    return true;
  }
  dispose() { for (const request of this.requests.values()) this.cancel(request.timer); this.requests.clear(); }
}
