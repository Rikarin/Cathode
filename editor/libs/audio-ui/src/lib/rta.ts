import { ChangeDetectionStrategy, Component, type ElementRef, input, viewChild } from '@angular/core';
import { onAnimationFrame, themeColors } from './animation';

/** Band levels in dB at their centre frequencies (RtaFrame in Cathode.Contracts). */
export interface RtaSpectrum {
  frequencies: ArrayLike<number>;
  levels: ArrayLike<number>;
}

/** Lowest level drawn; the scale runs from here to 0 dB. */
export const RtaFloorDb = -96;

/** The theme's colours: the well's lines for the grid, the accent for the bars and a light mark for the peaks. */
const colors = themeColors({ grid: '--well-line', bar: '--color-primary', peak: '--well-mark' });

/**
 * A real-time analyser: one bar per band on a log frequency axis, with a slowly falling peak hold. `source` is read
 * on animation frames, like the scope. Grid lines every 12 dB and at 100 Hz, 1 kHz and 10 kHz. It is drawn for a
 * sunken well: faint grid lines, translucent bars with a lit top edge, and the peaks as thin light marks.
 */
@Component({
  selector: 'cth-rta',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `<canvas #canvas class="size-full" role="img" [attr.aria-label]="label()"></canvas>`
})
export class CthRta {
  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private peaks: number[] = [];
  private lastTime = 0;

  readonly source = input.required<() => RtaSpectrum | null | undefined>();
  readonly label = input('Output spectrum');

  /** How fast the peak markers fall, in dB per second. */
  readonly peakFall = input(12);

  constructor() {
    onAnimationFrame(time => this.draw(time));
  }

  private draw(time: number): void {
    const canvas = this.canvas().nativeElement;
    const ratio = window.devicePixelRatio || 1;
    const width = canvas.clientWidth;
    const height = canvas.clientHeight;
    if (canvas.width !== width * ratio || canvas.height !== height * ratio) {
      canvas.width = width * ratio;
      canvas.height = height * ratio;
    }

    const context = canvas.getContext('2d');
    if (!context || width === 0 || height === 0) {
      return;
    }

    context.setTransform(ratio, 0, 0, ratio, 0, 0);
    context.clearRect(0, 0, width, height);
    const theme = colors(canvas);
    context.strokeStyle = theme.grid;
    context.lineWidth = 1;
    for (let db = 0; db > RtaFloorDb; db -= 12) {
      const y = Math.round(levelToY(db, height)) + 0.5;
      line(context, 0, y, width, y);
    }

    for (const hz of [100, 1_000, 10_000]) {
      const x = Math.round(frequencyToX(hz, width)) + 0.5;
      line(context, x, 0, x, height);
    }

    const spectrum = this.source()();
    if (!spectrum || spectrum.levels.length === 0) {
      this.peaks = [];
      return;
    }

    const count = spectrum.levels.length;
    const elapsed = this.lastTime ? (time - this.lastTime) / 1000 : 0;
    this.lastTime = time;
    if (this.peaks.length !== count) {
      this.peaks = Array.from({ length: count }, () => RtaFloorDb);
    }

    const barWidth = Math.max(1, width / count - 1);
    context.fillStyle = theme.bar;
    const peaks: { x: number; peak: number }[] = [];
    for (let i = 0; i < count; i++) {
      const frequency = spectrum.frequencies[i];
      const level = spectrum.levels[i] ?? RtaFloorDb;
      if (frequency === undefined) {
        continue; // a frame with fewer frequencies than levels: those bands have no place on the axis
      }

      const x = frequencyToX(frequency, width) - barWidth / 2;
      const y = levelToY(level, height);
      context.globalAlpha = 0.45;
      context.fillRect(x, y, barWidth, height - y);
      context.globalAlpha = 1;
      context.fillRect(x, y, barWidth, Math.min(2, height - y));
      const peak = Math.max(level, (this.peaks[i] ?? RtaFloorDb) - this.peakFall() * elapsed);
      this.peaks[i] = peak;
      peaks.push({ x, peak });
    }

    context.fillStyle = theme.peak;
    for (const { x, peak } of peaks) {
      context.fillRect(x, levelToY(peak, height) - 1, barWidth, 1.5);
    }
  }
}

/** x position of `hz` on a 20 Hz - 20 kHz log axis. */
export function frequencyToX(hz: number, width: number): number {
  return (Math.log10(Math.max(20, Math.min(20_000, hz)) / 20) / 3) * width;
}

/** y position of `db` between 0 dB (top) and {@link RtaFloorDb} (bottom). */
export function levelToY(db: number, height: number): number {
  return (Math.min(0, Math.max(RtaFloorDb, db)) / RtaFloorDb) * height;
}

function line(context: CanvasRenderingContext2D, x0: number, y0: number, x1: number, y1: number): void {
  context.beginPath();
  context.moveTo(x0, y0);
  context.lineTo(x1, y1);
  context.stroke();
}
