/** How a control maps its position (0..1) to a value. Log needs min > 0. */
export type Taper = 'linear' | 'log';

export interface ValueRange {
  min: number;
  max: number;
  taper?: Taper;
}

const clamp = (value: number, min: number, max: number) => Math.min(max, Math.max(min, value));

/** Position (0..1) of a value. */
export function toNormal(value: number, { min, max, taper = 'linear' }: ValueRange): number {
  if (max <= min) {
    return 0;
  }

  const v = clamp(value, min, max);
  return taper === 'log' && min > 0 ? Math.log(v / min) / Math.log(max / min) : (v - min) / (max - min);
}

/** Value at a position (0..1). */
export function fromNormal(normal: number, { min, max, taper = 'linear' }: ValueRange): number {
  const n = clamp(normal, 0, 1);
  return taper === 'log' && min > 0 ? min * Math.pow(max / min, n) : min + n * (max - min);
}

/** A readable value with its unit: 1.25 kHz, -6.0 dB, 350 ms, 42 %. */
export function formatValue(value: number, unit: string): string {
  if (!Number.isFinite(value)) {
    return '–';
  }

  switch (unit) {
    case 'Hz':
      return value >= 1000
        ? `${trim(value / 1000, value >= 10_000 ? 1 : 2)} kHz`
        : `${trim(value, value < 100 ? 1 : 0)} Hz`;
    case 'dB':
      return value <= -119.5 ? '-∞ dB' : `${value > 0.05 ? '+' : ''}${value.toFixed(1)} dB`;
    case 'ms':
      return value >= 1000 ? `${trim(value / 1000, 2)} s` : `${trim(value, value < 10 ? 1 : 0)} ms`;
    case 's':
      return `${trim(value, value < 10 ? 2 : 1)} s`;
    case '%':
      return `${Math.round(value)} %`;
    case '':
      return trim(value, Math.abs(value) < 10 ? 2 : 1);
    default:
      return `${trim(value, Math.abs(value) < 10 ? 2 : 1)} ${unit}`;
  }
}

/**
 * Parses what a user typed into a value control, or returns null when there is no number in it: "1.2k", "1.2 kHz",
 * "-6", "350ms", "2 s" (for unit ms), "8 :1" (for unit :1), "2,5" (a decimal comma). Everything {@link formatValue}
 * writes reads back, so "-∞" (or "-inf") is negative infinity: the control's minimum once clamped. Text after the
 * number that is not a unit this knows is ignored, unless it has digits of its own ("1.2.3", "4 5").
 */
export function parseValue(text: string, unit: string): number | null {
  const typed = text.trim().replace(/\u2212/g, '-');
  if (/^-\s*(∞|inf(inity)?)\s*\D*$/i.test(typed)) {
    return Number.NEGATIVE_INFINITY;
  }

  const match = /^([-+]?(?:\d+(?:[.,]\d*)?|[.,]\d+))(.*)$/.exec(typed);
  if (!match) {
    return null;
  }

  const [, digits = '', rest = ''] = match;
  const number = Number(digits.replace(',', '.'));
  const suffix = rest.replace(/\s+/g, '').toLowerCase();
  const own = unit.replace(/\s+/g, '').toLowerCase();
  // The control's own unit first: ":1" has a digit, and "s" must not read as a scale of itself.
  if (suffix === '' || suffix === own) {
    return number;
  }

  if (suffix === 'k' || suffix === 'khz') {
    return number * 1000;
  }

  if (own === 'ms' && suffix === 's') {
    return number * 1000;
  }

  if (own === 's' && suffix === 'ms') {
    return number / 1000;
  }

  return /\d/.test(suffix) ? null : number;
}

/** dBFS of a linear peak, floored. */
export function toDecibels(gain: number, floor = -120): number {
  return gain > 0 ? Math.max(floor, 20 * Math.log10(gain)) : floor;
}

function trim(value: number, digits: number): string {
  return Number(value.toFixed(digits)).toString();
}
