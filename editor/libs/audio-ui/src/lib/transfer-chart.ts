import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  type ElementRef,
  computed,
  inject,
  input,
  viewChild
} from '@angular/core';
import { onAnimationFrame } from './animation';
import { CTH_METER_PREFERENCES, frameDue } from './preferences';

/** Where a signal sits on a transfer curve: the level going in and the level coming out, dB. */
export interface TransferPoint {
  input: number;
  output: number;
}

/** The live dot as drawn: where it is (dB) and whether it shows. */
export interface TransferDot {
  input: number;
  output: number;
  visible: boolean;
}

/** How quickly the dot glides to a new reading: the time constant of its easing, ms. */
const GlideMs = 45;
/** The curve is drawn through a point every this many dB of input. */
const CurveStepDb = 0.5;

/** Where `db` sits on a scale from `min` to `max` dB: 0 at `min`, 1 at `max`, not clamped. */
export function transferFraction(db: number, min: number, max: number): number {
  return max > min ? (db - min) / (max - min) : 0;
}

/** The grid lines of a scale from `min` to `max` dB: every `step` dB, counted down from `max`, both ends included. */
export function transferTicks(min: number, max: number, step: number): number[] {
  const ticks: number[] = [];
  if (step <= 0 || max <= min) {
    return ticks;
  }

  for (let db = max; db >= min - 1e-9; db -= step) {
    ticks.push(db);
  }

  return ticks;
}

/**
 * The SVG path of `curve` (output dB for input dB) across a `size` px square spanning `min` to `max` dB on both axes,
 * input to the right and output up. Outputs far off the chart are held just past its edge, so the line leaves the
 * plot at the right angle and the well clips it.
 */
export function transferPath(curve: (inputDb: number) => number, min: number, max: number, size: number): string {
  if (max <= min || size <= 0) {
    return '';
  }

  const span = max - min;
  const steps = Math.ceil(span / CurveStepDb);
  const parts: string[] = [];
  for (let i = 0; i <= steps; i++) {
    const input = min + (span * i) / steps;
    const raw = curve(input);
    const output = Number.isFinite(raw) ? Math.max(min - span, Math.min(max + span, raw)) : min - span;
    const x = transferFraction(input, min, max) * size;
    const y = (1 - transferFraction(output, min, max)) * size;
    parts.push(`${i === 0 ? 'M' : 'L'}${x.toFixed(2)} ${y.toFixed(2)}`);
  }

  return parts.join(' ');
}

/**
 * The dot's next state for a reading `target` (null: nothing to show) after `dt` ms. It eases toward the reading with
 * a {@link GlideMs} time constant, so it moves as smoothly as a meter between telemetry frames; with `reduced` motion,
 * or when it was hidden, it jumps there. A reading off the chart's input range (`min` to `max`) hides it.
 */
export function stepTransferDot(
  state: TransferDot,
  target: TransferPoint | null,
  dt: number,
  reduced: boolean,
  min: number,
  max: number
): TransferDot {
  if (
    !target ||
    !Number.isFinite(target.input) ||
    !Number.isFinite(target.output) ||
    target.input < min ||
    target.input > max
  ) {
    return { ...state, visible: false };
  }

  if (reduced || !state.visible) {
    return { input: target.input, output: target.output, visible: true };
  }

  const k = 1 - Math.exp(-Math.max(0, dt) / GlideMs);
  return {
    input: state.input + (target.input - state.input) * k,
    output: state.output + (target.output - state.output) * k,
    visible: true
  };
}

/**
 * A transfer chart: output level against input level in dB, the same scale on both axes (−60 to 0 dB by default,
 * a grid line every 12 dB), as a compressor's page shows how it maps levels. A faint diagonal marks unity; `curve`
 * (output dB for input dB) is drawn in `accent` and redrawn whenever it changes, so it follows a knob while it turns.
 * `source` is read on animation frames, never through change detection: where the signal is now, or null when there is
 * none. The dot eases toward each reading (it jumps under prefers-reduced-motion) and hides when the input is off the
 * chart. `size` is the plot's side in px; the scale sits outside the well, to its left and below.
 */
@Component({
  selector: 'cth-transfer-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'inline-flex flex-col gap-1' },
  template: `
    <div class="flex gap-1">
      <div
        class="text-foreground-muted relative w-5 text-[9px] leading-none tabular-nums"
        [style.height.px]="size()"
        aria-hidden="true"
      >
        @for (tick of ticks(); track tick) {
          <span class="absolute right-0 -translate-y-1/2" [style.top.%]="(1 - fraction(tick)) * 100">{{ tick }}</span>
        }
      </div>
      <div class="well relative overflow-hidden rounded-md" [style.width.px]="size()" [style.height.px]="size()">
        <svg
          class="absolute inset-0 size-full"
          role="img"
          [attr.viewBox]="'0 0 ' + size() + ' ' + size()"
          [attr.aria-label]="label()"
        >
          @for (line of grid(); track $index) {
            <line
              [attr.x1]="line[0]"
              [attr.y1]="line[1]"
              [attr.x2]="line[2]"
              [attr.y2]="line[3]"
              style="stroke: var(--well-line)"
              stroke-width="1"
              shape-rendering="crispEdges"
            />
          }
          <line
            x1="0"
            [attr.y1]="size()"
            [attr.x2]="size()"
            y2="0"
            style="stroke: var(--well-line-strong)"
            stroke-width="1"
            stroke-dasharray="2 3"
            data-testid="transfer-unity"
          />
          <path
            [attr.d]="path()"
            fill="none"
            [style.stroke]="accent()"
            stroke-width="1.75"
            stroke-linejoin="round"
            stroke-linecap="round"
            data-testid="transfer-curve"
          />
          <g #dot opacity="0" data-testid="transfer-dot">
            <circle r="7" [style.fill]="accent()" opacity="0.25" />
            <circle r="3.5" [style.fill]="accent()" style="stroke: var(--well)" stroke-width="1" />
          </g>
        </svg>
      </div>
    </div>
    <div
      class="text-foreground-muted relative ms-6 h-2.5 text-[9px] leading-none tabular-nums"
      [style.width.px]="size()"
      aria-hidden="true"
    >
      @for (tick of ticks(); track tick) {
        <span class="absolute top-0 -translate-x-1/2" [style.left.%]="fraction(tick) * 100">{{ tick }}</span>
      }
    </div>
  `
})
export class CthTransferChart {
  private readonly dot = viewChild.required<ElementRef<SVGGElement>>('dot');
  private readonly preferences = inject(CTH_METER_PREFERENCES, { optional: true });
  private state: TransferDot = { input: 0, output: 0, visible: false };
  private last = 0;
  private reducedMotion = false;

  /** The curve: the output level for an input level, dB. */
  readonly curve = input.required<(inputDb: number) => number>();
  /** Where the signal is now, read on every frame; null (or a level off the chart) when there is none. */
  readonly source = input<() => TransferPoint | null>(() => null);
  readonly label = input('Transfer curve');
  /** The scale's ends and grid spacing, dB, the same on both axes. */
  readonly min = input(-60);
  readonly max = input(0);
  readonly step = input(12);
  /** The plot's side, px. */
  readonly size = input(160);
  /** The curve's and the dot's colour: any CSS colour, the block's accent on a block page. */
  readonly accent = input('var(--color-primary)');

  protected readonly ticks = computed(() => transferTicks(this.min(), this.max(), this.step()));
  protected readonly path = computed(() => transferPath(this.curve(), this.min(), this.max(), this.size()));
  /** The grid's lines as x1, y1, x2, y2, both ways, without the edges the well already draws. */
  protected readonly grid = computed(() => {
    const size = this.size();
    return this.ticks()
      .map(tick => Math.round(this.fraction(tick) * size) + 0.5)
      .filter(at => at > 1 && at < size - 1)
      .flatMap(at => [
        [at, 0, at, size],
        [0, size - at, size, size - at]
      ]);
  });

  constructor() {
    this.watchReducedMotion();
    onAnimationFrame(time => {
      if (frameDue(time, this.last, this.preferences?.rate() ?? 60)) {
        const dt = this.last ? Math.min(250, time - this.last) : 0;
        this.last = time;
        this.draw(dt);
      }
    });
  }

  protected fraction(db: number): number {
    return transferFraction(db, this.min(), this.max());
  }

  /** Moves the dot straight on its element: 30 to 60 times a second, never through change detection. */
  private draw(dt: number): void {
    const min = this.min();
    const max = this.max();
    this.state = stepTransferDot(this.state, this.source()(), dt, this.reducedMotion, min, max);
    const dot = this.dot().nativeElement;
    if (!this.state.visible) {
      dot.setAttribute('opacity', '0');
      return;
    }

    const size = this.size();
    const x = transferFraction(this.state.input, min, max) * size;
    const y = (1 - Math.max(-0.1, Math.min(1.1, transferFraction(this.state.output, min, max)))) * size;
    dot.setAttribute('transform', `translate(${x.toFixed(2)} ${y.toFixed(2)})`);
    dot.setAttribute('opacity', '1');
  }

  private watchReducedMotion(): void {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
      return;
    }

    const query = window.matchMedia('(prefers-reduced-motion: reduce)');
    const update = () => (this.reducedMotion = query.matches);
    update();
    query.addEventListener('change', update);
    inject(DestroyRef).onDestroy(() => query.removeEventListener('change', update));
  }
}
