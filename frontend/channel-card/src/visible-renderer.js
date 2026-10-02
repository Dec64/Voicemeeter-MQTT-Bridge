// At most one pending paint. There is no animation loop while the model is unchanged.
export class VisibleRenderer {
  constructor(paint, request = callback => requestAnimationFrame(callback), cancel = id => cancelAnimationFrame(id)) {
    this.paint = paint; this.schedule = request; this.cancel = cancel;
    this.active = false; this.pending = null; this.generation = 0; this.lastPaint = -Infinity;
  }
  setActive(active) {
    if (active === this.active) return;
    this.active = active; this.generation++;
    if (this.pending !== null) this.cancel(this.pending);
    this.pending = null; this.lastPaint = -Infinity;
    if (active) this.request();
  }
  request() {
    if (!this.active || this.pending !== null) return;
    const generation = this.generation;
    this.pending = this.schedule(time => {
      if (!this.active || generation !== this.generation) return;
      this.pending = null;
      if (time - this.lastPaint < 1000 / 30) { this.request(); return; }
      this.lastPaint = time; this.paint();
    });
  }
}
