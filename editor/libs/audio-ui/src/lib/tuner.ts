import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  type ElementRef,
  computed,
  inject,
  input,
  signal,
  viewChild
} from '@angular/core';
import { onAnimationFrame } from './animation';

/** What the tuner shows: the engine's TunerInfo (Cathode.Contracts), or a tuning's target from `tuneTo`. */
export interface TunerReading {
  frequency: number;
  note: string;
  octave: number;
  cents: number;
  /** The string of a tuning that is targeted, 1 for the highest; absent in chromatic. */
  string?: number;
}

/** How the tuner shows the error: a needle, or a strobe whose marks drift at a speed proportional to it. */
export type TunerView = 'needle' | 'strobe';

let nextId = 0;

/** Within this many cents a string reads as in tune. */
const InTuneCents = 2;
/** How long a note (and whether it is in tune) must hold before a screen reader is told it: about 30 readings. */
export const AnnounceAfterMs = 1_000;
/** Strobe speed in view-box units a second per cent of error: 1 cent drifts a coarse mark across in about 5 s. */
export const StrobeSpeed = 3;
/** The strobe's speed follows a new reading over about this long, so 10-30 readings a second still glide. */
const StrobeSmoothingMs = 80;
/** Spacing of the strobe's coarse marks; the fine row has marks twice as close, moving twice as fast. */
const StrobeSpacing = 16;

/**
 * Advances a strobe band: the new phase (0..`spacing`) after `dt` seconds at `cents` of error. Sharp moves the marks
 * right, flat left; in tune they stand still. The error is clamped to ±50 cents so a string far off does not blur.
 */
export function strobeStep(phase: number, cents: number, dt: number, spacing = StrobeSpacing): number {
  const next = phase + Math.max(-50, Math.min(50, cents)) * StrobeSpeed * dt;
  return ((next % spacing) + spacing) % spacing;
}

/**
 * A tuner: a needle spanning ±50 cents, or a strobe. `source` is polled on animation frames (telemetry bypasses
 * change detection); the text only updates when the rounded reading changes. The strobe moves at display rate from
 * the latest cents (its speed eased toward each new reading), however few readings arrive; with reduced motion
 * asked for, the needle shows instead. Either turns green within ±2 cents. A screen reader is not told every reading
 * (up to 30 a second): a polite live region says the note, and whether it is in tune, once that has held for a
 * second.
 */
@Component({
  selector: 'cth-tuner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col items-center gap-3' },
  template: `
    <div class="flex flex-col items-center" aria-hidden="true">
      <div class="flex items-baseline gap-1">
        <span class="text-6xl font-semibold tabular-nums" [class]="inTune() ? 'text-success' : 'text-foreground'">
          {{ reading()?.note ?? '–' }}
        </span>
        <span class="text-foreground-muted text-xl">{{ reading()?.octave ?? '' }}</span>
      </div>
      @if (reading()?.string; as n) {
        <span class="text-foreground-muted text-xs" data-testid="tuner-string">String {{ n }}</span>
      }
    </div>
    @if (strobe()) {
      <svg
        viewBox="0 0 200 60"
        class="w-[22rem] max-w-full"
        role="img"
        data-testid="tuner-strobe"
        [attr.aria-label]="description()"
      >
        <defs>
          <clipPath [attr.id]="clipId"><rect x="4" y="8" width="192" height="44" rx="4" /></clipPath>
        </defs>
        <rect x="4" y="8" width="192" height="44" rx="4" class="fill-surface-sunken stroke-border" />
        <g [attr.clip-path]="'url(#' + clipId + ')'" [class]="markClass()">
          <g #coarse>
            @for (x of coarseMarks; track x) {
              <rect [attr.x]="x" y="11" width="7" height="18" rx="1" />
            }
          </g>
          <g #fine>
            @for (x of fineMarks; track x) {
              <rect [attr.x]="x" y="32" width="3.5" height="17" rx="1" />
            }
          </g>
        </g>
        <line x1="100" x2="100" y1="2" y2="58" class="stroke-foreground-muted" stroke-width="1" />
      </svg>
    } @else {
      <svg viewBox="0 0 200 60" class="w-[22rem] max-w-full" role="img" [attr.aria-label]="description()">
        @for (tick of ticks; track tick) {
          <line
            [attr.x1]="100 + tick * 1.8"
            [attr.x2]="100 + tick * 1.8"
            y1="40"
            [attr.y2]="tick % 25 === 0 ? 20 : 30"
            class="stroke-border"
            stroke-width="1.5"
          />
        }
        @if (reading(); as r) {
          <line
            [attr.x1]="100 + clamp(r.cents) * 1.8"
            [attr.x2]="100 + clamp(r.cents) * 1.8"
            y1="8"
            y2="52"
            [class]="inTune() ? 'stroke-success' : 'stroke-warning'"
            stroke-width="3"
            stroke-linecap="round"
          />
        }
      </svg>
    }
    <span class="text-foreground-muted text-sm tabular-nums">
      @if (reading(); as r) {
        {{ r.frequency.toFixed(1) }} Hz · {{ r.cents > 0 ? '+' : '' }}{{ r.cents.toFixed(1) }} cents
      } @else {
        Play a string
      }
    </span>
    @if (view() === 'strobe' && reducedMotion()) {
      <span class="text-foreground-muted text-xs">Reduced motion is on: the needle shows instead of the strobe.</span>
    }
    <span class="sr-only" aria-live="polite" data-testid="tuner-announcement">{{ announcement() }}</span>
  `
})
export class CthTuner {
  readonly source = input.required<() => TunerReading | null | undefined>();
  readonly view = input<TunerView>('needle');

  protected readonly clipId = `cth-strobe-${nextId++}`;
  protected readonly ticks = [-50, -40, -30, -20, -10, 0, 10, 20, 30, 40, 50];
  /** The marks' resting x, one spacing beyond each edge so the band stays full as it moves either way. */
  protected readonly coarseMarks = marks(StrobeSpacing);
  protected readonly fineMarks = marks(StrobeSpacing / 2);
  protected readonly reading = signal<TunerReading | null>(null);
  protected readonly inTune = computed(() => {
    const r = this.reading();
    return r !== null && Math.abs(r.cents) <= InTuneCents;
  });
  protected readonly reducedMotion = signal(false);
  protected readonly strobe = computed(() => this.view() === 'strobe' && !this.reducedMotion());
  protected readonly markClass = computed(() => {
    if (!this.reading()) {
      return 'fill-border';
    }

    return this.inTune() ? 'fill-success' : 'fill-warning';
  });
  /** What the live region says: the note and whether it is in tune, once held for {@link AnnounceAfterMs}. */
  protected readonly announcement = signal('');
  private heard = { text: '', since: 0 };

  protected readonly description = computed(() => {
    const r = this.reading();
    if (!r) {
      return 'No pitch detected';
    }

    return `${r.string ? `String ${r.string}, ` : ''}${r.note}${r.octave}, ${r.cents.toFixed(1)} cents`;
  });

  private readonly coarse = viewChild<ElementRef<SVGGElement>>('coarse');
  private readonly fine = viewChild<ElementRef<SVGGElement>>('fine');
  private readonly motion = { coarse: 0, fine: 0, cents: 0, last: 0 };

  constructor() {
    this.watchReducedMotion();
    onAnimationFrame(time => {
      const next = this.source()() ?? null;
      const current = this.reading();
      if (
        next?.note !== current?.note ||
        next?.octave !== current?.octave ||
        next?.string !== current?.string ||
        Math.round((next?.cents ?? 0) * 10) !== Math.round((current?.cents ?? 0) * 10)
      ) {
        this.reading.set(next ? { ...next } : null);
      }

      this.moveStrobe(time, next?.cents ?? 0);
      this.announce(time);
    });
  }

  /** Updates the live region once the note (and whether it is in tune) has held long enough, and only on a change. */
  protected announce(time: number): void {
    const r = this.reading();
    const note = r ? `${r.string ? `String ${r.string}, ` : ''}${r.note}${r.octave}` : '';
    const text = r ? `${note}${this.inTune() ? ', in tune' : ''}` : '';
    if (text !== this.heard.text) {
      this.heard = { text, since: time };
    } else if (text && text !== this.announcement() && time - this.heard.since >= AnnounceAfterMs) {
      this.announcement.set(text);
    }
  }

  protected clamp(cents: number): number {
    return Math.max(-50, Math.min(50, cents));
  }

  /**
   * Moves the strobe's marks straight on the elements (no change detection per frame), by the time since the last
   * frame and the error eased toward the latest reading's. Without a reading it stops.
   */
  private moveStrobe(time: number, cents: number): void {
    const m = this.motion;
    const dt = m.last ? Math.min(0.1, (time - m.last) / 1000) : 0;
    m.last = time;
    m.cents += (cents - m.cents) * (1 - Math.exp((-dt * 1000) / StrobeSmoothingMs));
    const coarse = this.coarse()?.nativeElement;
    const fine = this.fine()?.nativeElement;
    if (!coarse || !fine) {
      return;
    }

    m.coarse = strobeStep(m.coarse, m.cents, dt);
    m.fine = strobeStep(m.fine, 2 * m.cents, dt, StrobeSpacing / 2);
    coarse.setAttribute('transform', `translate(${m.coarse.toFixed(2)} 0)`);
    fine.setAttribute('transform', `translate(${m.fine.toFixed(2)} 0)`);
  }

  private watchReducedMotion(): void {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
      return;
    }

    const query = window.matchMedia('(prefers-reduced-motion: reduce)');
    const update = () => this.reducedMotion.set(query.matches);
    update();
    query.addEventListener('change', update);
    inject(DestroyRef).onDestroy(() => query.removeEventListener('change', update));
  }
}

function marks(spacing: number): number[] {
  const result: number[] = [];
  for (let x = -spacing; x < 200 + spacing; x += spacing) {
    result.push(x);
  }

  return result;
}
