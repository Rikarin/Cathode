import { InjectionToken, type Signal } from '@angular/core';

/** How every knob responds, from the app's user preferences; knobs fall back to linear, normal wheel, tooltips. */
export interface KnobPreferences {
  /** Linear: drag up or down. Circular: drag around the knob's centre, as if turning it. */
  mode: Signal<'linear' | 'circular'>;
  wheel: Signal<'normal' | 'reverse' | 'off'>;
  tooltips: Signal<boolean>;
}

export const CTH_KNOB_PREFERENCES = new InjectionToken<KnobPreferences>('CTH_KNOB_PREFERENCES');

/** How often meters redraw: frames per second, 0 to stop (they then show nothing). */
export interface MeterPreferences {
  rate: Signal<number>;
}

export const CTH_METER_PREFERENCES = new InjectionToken<MeterPreferences>('CTH_METER_PREFERENCES');

/**
 * The knob position for a pointer in circular mode: the angle from the knob's centre, mapped onto its 270° travel
 * (7:30 to 4:30, clockwise). In the dead zone at the bottom it keeps the nearer end. Null at the very centre.
 */
export function circularNormal(dx: number, dy: number): number | null {
  if (Math.hypot(dx, dy) < 2) {
    return null;
  }

  // Degrees clockwise from straight down, 0..360.
  const fromBottom = ((Math.atan2(-dx, dy) * 180) / Math.PI + 360) % 360;
  const travel = fromBottom - 45; // the travel starts 45° clockwise of straight down
  return Math.min(1, Math.max(0, travel / 270));
}

/** Whether a frame at `time` (ms) should draw at `rate` per second, given when the last one drew. */
export function frameDue(time: number, last: number, rate: number): boolean {
  return rate >= 60 || (rate > 0 && time - last >= 1000 / rate - 1);
}
