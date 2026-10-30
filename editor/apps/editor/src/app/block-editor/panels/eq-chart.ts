import { DestroyRef, type ElementRef, type WritableSignal, afterNextRender, inject } from '@angular/core';

/** The sunken well an EQ chart is drawn in, laid over its tile's grid area. */
export const EqWellClass =
  'border-hairline-strong absolute inset-0 overflow-hidden rounded-lg border bg-(--well) shadow-[inset_0_1px_3px_rgb(0_0_0/0.6),0_1px_0_var(--highlight)]';

export interface EqGeometry {
  width: number;
  height: number;
  xOf: (frequency: number) => number;
  frequencyOf: (x: number) => number;
  yOf: (db: number) => number;
  dbOf: (y: number) => number;
}

export interface EqChartColors {
  curve: string;
  handle: string;
  grid: string;
  unity: string;
}

/** A level clamped to an EQ chart's ±`maxDb` range. */
export const clampDb = (db: number, maxDb: number) => Math.max(-maxDb, Math.min(maxDb, db));

/**
 * An EQ chart's mapping for a stage `width` by `height` px: frequency across (log, 20 Hz to 20 kHz), gain up (±`maxDb`
 * dB, 0 dB in the middle).
 */
export function eqGeometry(width: number, height: number, maxDb: number): EqGeometry {
  return {
    width,
    height,
    xOf: (frequency: number) => (Math.log(frequency / 20) / Math.log(1000)) * width,
    frequencyOf: (x: number) => 20 * Math.pow(1000, Math.min(width, Math.max(0, x)) / width),
    yOf: (db: number) => height / 2 - (db / maxDb) * (height / 2),
    dbOf: (y: number) => clampDb(((height / 2 - y) / (height / 2)) * maxDb, maxDb)
  };
}

/**
 * The theme's colours as they stand (a canvas cannot resolve CSS variables): the accent for the curve, a light rim for
 * handles, and the well's lines for the grid, a stronger one at 0 dB.
 */
export function eqChartColors(element: HTMLElement): EqChartColors {
  const style = getComputedStyle(element.ownerDocument.documentElement);
  const token = (name: string) => style.getPropertyValue(name).trim() || '#888';
  return {
    curve: token('--color-primary'),
    handle: 'rgb(255 255 255 / 0.85)',
    grid: token('--well-line'),
    unity: token('--well-line-strong')
  };
}

/** The grid: lines at 100 Hz, 1 kHz and 10 kHz, and at -12, 0 and +12 dB, the 0 dB one stronger. */
export function eqGrid(geo: EqGeometry, colors: EqChartColors) {
  const { xOf, yOf, width, height } = geo;
  const vertical = [100, 1_000, 10_000].map(f => ({
    points: [xOf(f), 0, xOf(f), height],
    stroke: colors.grid,
    strokeWidth: 1,
    listening: false
  }));
  const horizontal = [-12, 0, 12].map(db => ({
    points: [0, yOf(db), width, yOf(db)],
    stroke: db === 0 ? colors.unity : colors.grid,
    strokeWidth: 1,
    listening: false
  }));
  return [...vertical, ...horizontal];
}

/** The curve through `points` (x, y pairs) in the accent. */
export function eqCurve(points: number[], colors: EqChartColors) {
  return { points, stroke: colors.curve, strokeWidth: 2, lineJoin: 'round' as const, listening: false };
}

/** A faint fill between the curve through `points` and 0 dB, so boosts and cuts read at a glance. */
export function eqArea(points: number[], geo: EqGeometry, colors: EqChartColors) {
  const first = points[0] ?? 0;
  const last = points.at(-2) ?? geo.width;
  return {
    points: [first, geo.yOf(0), ...points, last, geo.yOf(0)],
    closed: true,
    fill: colors.curve,
    opacity: 0.14,
    listening: false
  };
}

/**
 * Keeps `size` at the stage size the tile's grid area gives (less its 1 px border). The chart is laid over the area
 * rather than in it, so it never sets the rows' height itself. Call in an injection context.
 */
export function measureEqTile(host: ElementRef<HTMLElement>, size: WritableSignal<{ width: number; height: number }>) {
  const observer = new ResizeObserver(([entry]) => {
    if (entry) {
      const width = Math.max(120, Math.floor(entry.contentRect.width) - 2);
      const height = Math.max(80, Math.floor(entry.contentRect.height) - 2);
      size.set({ width, height });
    }
  });
  afterNextRender(() => observer.observe(host.nativeElement));
  inject(DestroyRef).onDestroy(() => observer.disconnect());
}
