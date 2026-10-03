// Setting previews, not measured audio. RBJ biquads at an illustrative 48 kHz.
// Formula reference: https://www.w3.org/TR/audio-eq-cookbook/
export const frequencyPosition = (f) =>
  Math.log10(Math.max(20, Math.min(20000, f)) / 20) / 3;
export const positionFrequency = (x) =>
  20 * 1000 ** Math.max(0, Math.min(1, x));
export function quantize(value, { min, max, step }) {
  return Number(
    Math.max(
      min,
      Math.min(max, min + Math.round((value - min) / step) * step),
    ).toFixed(6),
  );
}
export function compressorOutput(
  input,
  { threshold, ratio, knee = 0, gainin = 0, gainout = 0 },
) {
  const x = input + gainin,
    width = knee * 12,
    delta = x - threshold,
    slope = 1 / ratio - 1;
  const reduction =
    width && Math.abs(delta) < width / 2
      ? (slope * (delta + width / 2) ** 2) / (2 * width)
      : delta > 0
        ? slope * delta
        : 0;
  return x + reduction + gainout;
}
export function eqResponse(bands, frequency) {
  let total = 0;
  for (const band of bands) {
    if (
      !band.on ||
      ![band.f, band.gain, band.q, band.type].every(Number.isFinite)
    )
      continue;
    const w = (2 * Math.PI * band.f) / 48000,
      c = Math.cos(w),
      s = Math.sin(w),
      alpha = s / (2 * band.q),
      A = 10 ** (band.gain / 40);
    let b, a;
    switch (band.type) {
      case 0:
        b = [1 + alpha * A, -2 * c, 1 - alpha * A];
        a = [1 + alpha / A, -2 * c, 1 - alpha / A];
        break;
      case 1:
        b = [1, -2 * c, 1];
        a = [1 + alpha, -2 * c, 1 - alpha];
        break;
      case 2:
        b = [alpha, 0, -alpha];
        a = [1 + alpha, -2 * c, 1 - alpha];
        break;
      case 3:
        b = [(1 - c) / 2, 1 - c, (1 - c) / 2];
        a = [1 + alpha, -2 * c, 1 - alpha];
        break;
      case 4:
        b = [(1 + c) / 2, -(1 + c), (1 + c) / 2];
        a = [1 + alpha, -2 * c, 1 - alpha];
        break;
      case 5: {
        const t = 2 * Math.sqrt(A) * alpha;
        b = [
          A * (A + 1 - (A - 1) * c + t),
          2 * A * (A - 1 - (A + 1) * c),
          A * (A + 1 - (A - 1) * c - t),
        ];
        a = [
          A + 1 + (A - 1) * c + t,
          -2 * (A - 1 + (A + 1) * c),
          A + 1 + (A - 1) * c - t,
        ];
        break;
      }
      case 6: {
        const t = 2 * Math.sqrt(A) * alpha;
        b = [
          A * (A + 1 + (A - 1) * c + t),
          -2 * A * (A - 1 + (A + 1) * c),
          A * (A + 1 + (A - 1) * c - t),
        ];
        a = [
          A + 1 - (A - 1) * c + t,
          2 * (A - 1 - (A + 1) * c),
          A + 1 - (A - 1) * c - t,
        ];
        break;
      }
      default:
        continue;
    }
    const phase = (2 * Math.PI * frequency) / 48000;
    const power = (coeff) =>
      (coeff[0] +
        coeff[1] * Math.cos(phase) +
        coeff[2] * Math.cos(2 * phase)) **
        2 +
      (coeff[1] * Math.sin(phase) + coeff[2] * Math.sin(2 * phase)) ** 2;
    total +=
      10 * Math.log10(Math.max(1e-12, power(b)) / Math.max(1e-12, power(a)));
  }
  return total;
}
