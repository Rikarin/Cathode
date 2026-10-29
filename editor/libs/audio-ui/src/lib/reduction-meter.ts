import {
  ChangeDetectionStrategy,
  Component,
  type ElementRef,
  computed,
  inject,
  input,
  signal,
  viewChild
} from '@angular/core';
import { onAnimationFrame } from './animation';
import { CTH_METER_PREFERENCES, frameDue } from './preferences';

/** How long the deepest reduction stays marked before the mark follows the meter again. */
const HoldMs = 1_000;
/** How often the numeric readout may change, so it can be read while the bar moves. */
const ReadoutMs = 120;

/** The reduction a meter shows and the deepest one it holds, dB (0 or more). */
export interface ReductionState {
  level: number;
  peak: number;
  peakAt: number;
}

/**
 * Where `db` of reduction sits on a meter spanning 0 to -`range` dB: 0 at the top (no reduction), 1 at the bottom,
 * clamped. A negative or non-finite value reads as no reduction.
 */
export function reductionFraction(db: number, range: number): number {
  if (!Number.isFinite(db) || db <= 0 || range <= 0) {
    return 0;
  }

  return Math.min(1, db / range);
}

/** The scale marks for a meter spanning 0 to -`range` dB: every 6 dB, or every 12 dB past 24 dB, reduction positive. */
export function reductionTicks(range: number): number[] {
  const step = range > 24 ? 12 : 6;
  const ticks: number[] = [];
  for (let db = 0; db <= range + 1e-9; db += step) {
    ticks.push(db);
  }

  return ticks;
}

/**
 * The meter's next state for a reading of `db` at `time` (ms): the bar follows the reading (the block's own release is
 * the ballistics), and the peak mark holds the deepest reduction for {@link HoldMs} before it falls to the reading.
 */
export function stepReduction(state: ReductionState, db: number, time: number): ReductionState {
  const level = Number.isFinite(db) ? Math.max(0, db) : 0;
  return level >= state.peak || time - state.peakAt > HoldMs
    ? { level, peak: level, peakAt: time }
    : { level, peak: state.peak, peakAt: state.peakAt };
}

/** A reduction for people: "0.0 dB" at rest, "-4.2 dB" while the block pulls the level down. */
export function formatReduction(db: number): string {
  const rounded = Math.round(Math.max(0, db) * 10) / 10;
  return rounded === 0 ? '0.0 dB' : `-${rounded.toFixed(1)} dB`;
}

/**
 * A gain-reduction meter: a bar that grows down from 0 dB as a compressor or gate pulls the level down, on a scale of
 * 0 to -`range` dB (24 by default), a mark that holds the deepest reduction for a second, and a readout of it
 * underneath. `source` is read on every animation frame, never through change detection, and returns the reduction in
 * dB as a positive number (the Server keeps the deepest per telemetry frame). The meter fills its host's height.
 */
@Component({
  selector: 'cth-reduction-meter',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'inline-flex flex-col items-center gap-1' },
  template: `
    <div class="flex min-h-0 flex-1 gap-1">
      <div class="text-foreground-muted relative w-5 text-[9px] leading-none tabular-nums" aria-hidden="true">
        @for (tick of ticks(); track tick) {
          <span class="absolute right-0 -translate-y-1/2" [style.top.%]="fraction(tick) * 100">
            {{ tick === 0 ? '0' : '-' + tick }}
          </span>
        }
      </div>
      <div
        class="bg-surface-sunken ring-border relative w-2.5 overflow-hidden rounded-sm ring-1 ring-inset"
        role="meter"
        [attr.aria-label]="label()"
        aria-valuemin="0"
        [attr.aria-valuemax]="range()"
        [attr.aria-valuenow]="readout()"
        [attr.aria-valuetext]="text()"
      >
        <div #bar class="bg-warning absolute inset-x-0 top-0" style="height: 0"></div>
        <div #peak class="bg-warning absolute inset-x-0 h-0.5 opacity-0" style="top: 0"></div>
      </div>
    </div>
    <span class="text-foreground-muted text-[10px] tabular-nums" data-testid="reduction-readout">{{ text() }}</span>
  `
})
export class CthReductionMeter {
  private readonly bar = viewChild.required<ElementRef<HTMLElement>>('bar');
  private readonly peakMark = viewChild.required<ElementRef<HTMLElement>>('peak');
  private readonly preferences = inject(CTH_METER_PREFERENCES, { optional: true });
  private state: ReductionState = { level: 0, peak: 0, peakAt: 0 };
  private last = 0;
  private readoutAt = 0;

  readonly source = input.required<() => number>();
  readonly label = input('Gain reduction');
  /** The deepest reduction on the scale, dB. */
  readonly range = input(24);

  protected readonly ticks = computed(() => reductionTicks(this.range()));
  /** The held reduction, to a tenth of a dB, as the readout shows it. */
  protected readonly readout = signal(0);
  protected readonly text = computed(() => formatReduction(this.readout()));

  constructor() {
    onAnimationFrame(time => {
      if (frameDue(time, this.last, this.preferences?.rate() ?? 60)) {
        this.last = time;
        this.draw(time);
      }
    });
  }

  protected fraction(db: number): number {
    return reductionFraction(db, this.range());
  }

  private draw(time: number): void {
    this.state = stepReduction(this.state, this.source()(), time);
    const range = this.range();
    this.bar().nativeElement.style.height = `${reductionFraction(this.state.level, range) * 100}%`;
    const mark = this.peakMark().nativeElement;
    const peak = reductionFraction(this.state.peak, range);
    mark.style.top = `calc(${peak * 100}% - 2px)`;
    mark.style.opacity = peak > 0 ? '1' : '0';
    if (time - this.readoutAt >= ReadoutMs) {
      const held = Math.round(this.state.peak * 10) / 10;
      if (held !== this.readout()) {
        this.readout.set(held);
        this.readoutAt = time;
      }
    }
  }
}
