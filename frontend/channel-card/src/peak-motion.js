export class PeakMotion {
  constructor(floor = -90, seconds = 5, holdMs = 1500) {
    this.floor = floor; this.seconds = seconds; this.holdMs = holdMs; this.reset();
  }
  reset() {
    this.measured = null; this.display = null; this.last = null;
    this.peak = null; this.peakUntil = 0; this.clipUntil = 0; this.history = [];
  }
  observe(level, time) {
    if (!Number.isFinite(level) || !Number.isFinite(time) || time < 0 || this.last !== null && time < this.last) return;
    this.advance(time); this.measured = Math.max(this.floor, level);
    if (this.display === null || level >= this.display) this.display = this.measured;
    if (this.peak === null || level >= this.peak) { this.peak = this.measured; this.peakUntil = time + this.holdMs; }
    if (level >= -.1) this.clipUntil = time + 2000;
    this.history.push({ time, level: this.measured });
    this.history = this.history.filter(p => p.time >= time - this.seconds * 1000).slice(-256);
    this.last = time;
  }
  advance(time) {
    if (this.last !== null && time >= this.last && this.display !== null) {
      this.display = Math.max(this.measured, this.display - 35 * (time - this.last) / 1000);
      if (time > this.peakUntil) this.peak = Math.max(this.measured, this.peak - 35 * Math.max(0, time - Math.max(this.last, this.peakUntil)) / 1000);
    }
    this.last = time;
  }
  view(time, reduced = false) {
    this.advance(time);
    return { level: reduced ? this.measured : this.display, hold: this.peak,
      clipping: this.measured !== null && time < this.clipUntil,
      animate: !reduced && this.measured !== null && (this.display > this.measured || this.peak > this.measured || time < this.clipUntil) };
  }
}
