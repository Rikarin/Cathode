import {
  ChangeDetectionStrategy,
  Component,
  type ElementRef,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild
} from '@angular/core';
import { onAnimationFrame, themeColors } from './animation';
import { CTH_METER_PREFERENCES, frameDue } from './preferences';
import { toDecibels } from './values';

const FloorDb = -60;
const CeilingDb = 6;
const FallDbPerSecond = 24;
const HoldMs = 1_500;
/** Above this a segment lights amber; at 0 dB and over, red. */
const HotDb = -12;
/** The gap between channels, in CSS pixels. */
const ChannelGap = 2;
/** The dark gap after each LED segment along the scale, in CSS pixels. */
const SegmentGap = 1;

/** Counts the requests to put out every clip light at once (resetClipLights); each meter watches it. */
const clipResets = signal(0);

/** Puts out every meter's clip light at once, as a hardware unit's clear button does: the editor's ⌘K. */
export function resetClipLights(): void {
  clipResets.update(n => n + 1);
}

interface ChannelState {
  level: number;
  peak: number;
  peakAt: number;
}

/**
 * A peak meter for one or more channels (docs/02-architecture.md §5.9), drawn as LED segments: green, amber from
 * -12 dB, red from 0 dB, with the unlit segments faintly visible so the scale reads at rest, a peak-hold segment and a
 * 0 dB mark. `sources` are read on every animation frame, never through change detection: each returns the linear
 * peak since the previous read (the Server already keeps the maximum per telemetry frame). Instant attack, 24 dB/s
 * fall, 1.5 s peak hold, and a clip light that stays on until clicked; the click goes no further, so a meter laid over
 * something clickable (a block's tile) is reset without acting on it. Horizontal meters fill the host's width, vertical
 * ones its height; `bar` and `segment` size the LEDs for where the meter sits.
 */
@Component({
  selector: 'cth-meter',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'inline-flex gap-1.5',
    '[class.items-center]': 'orientation() === "horizontal"',
    '[class.flex-col-reverse]': 'orientation() === "vertical"',
    '[class.items-stretch]': 'orientation() === "vertical"'
  },
  template: `
    <canvas
      #canvas
      class="min-h-0 min-w-0 flex-1"
      role="meter"
      [attr.aria-label]="label()"
      [attr.aria-valuemin]="floor"
      [attr.aria-valuemax]="ceiling"
      [attr.aria-valuenow]="announced()"
      [style.height.px]="orientation() === 'horizontal' ? thickness() : null"
      [style.width.px]="orientation() === 'vertical' ? thickness() : null"
    ></canvas>
    @if (clipped() || clipLight() === 'always') {
      <button
        type="button"
        class="size-2 shrink-0 self-center rounded-full transition-colors"
        [class]="
          clipped() ? 'bg-error shadow-[0_0_6px_var(--color-error)]' : 'bg-surface-sunken ring-border ring-1 ring-inset'
        "
        [attr.aria-label]="clipped() ? label() + ' clipped; click to reset' : label() + ' clip indicator'"
        (pointerdown)="$event.stopPropagation()"
        (click)="$event.stopPropagation(); clipped.set(false)"
      ></button>
    }
  `
})
export class CthMeter {
  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private states: ChannelState[] = [];
  private last = 0;
  private readonly preferences = inject(CTH_METER_PREFERENCES, { optional: true });
  /** The zone colours, resolved once per theme change rather than on every frame. */
  private readonly colors = themeColors({
    ok: '--color-success',
    hot: '--color-warning',
    clip: '--color-error',
    mark: '--color-foreground-muted'
  });

  readonly sources = input.required<readonly (() => number)[]>();
  readonly label = input('Level');
  readonly orientation = input<'horizontal' | 'vertical'>('vertical');
  /** Each channel's bar thickness, in CSS pixels: thicker where the meter is small and must read at a glance. */
  readonly bar = input(4);
  /** Each LED segment's lit length along the scale, in CSS pixels. */
  readonly segment = input(2);
  /** Whether the clip light shows at rest (`always`) or only once it has caught a clip (`clipped`), where space is short. */
  readonly clipLight = input<'always' | 'clipped'>('always');

  protected readonly floor = FloorDb;
  protected readonly ceiling = CeilingDb;
  protected readonly clipped = signal(false);
  /** Whole-dB level for assistive technology, updated only when it changes. */
  protected readonly announced = signal(FloorDb);
  protected readonly thickness = computed(() => this.sources().length * (this.bar() + ChannelGap) - ChannelGap);

  constructor() {
    // At the user's refresh rate: a lower one costs less on a busy machine. The levels between frames are peaks
    // the sources keep, so a slower meter misses nothing.
    onAnimationFrame(time => {
      if (frameDue(time, this.last, this.preferences?.rate() ?? 60)) {
        this.draw(time);
      }
    });
    effect(() => {
      clipResets();
      untracked(() => this.clipped.set(false));
    });
  }

  private draw(time: number): void {
    const canvas = this.canvas().nativeElement;
    const sources = this.sources();
    const elapsed = this.last ? (time - this.last) / 1000 : 0;
    this.last = time;
    if (this.states.length !== sources.length) {
      this.states = sources.map(() => ({ level: FloorDb, peak: FloorDb, peakAt: 0 }));
    }

    sources.forEach((source, i) => {
      const db = toDecibels(source(), FloorDb);
      const state = this.states[i];
      if (!state) {
        return; // one state per source, made above
      }

      state.level = Math.max(db, state.level - FallDbPerSecond * elapsed);
      if (db >= state.peak || time - state.peakAt > HoldMs) {
        state.peak = db;
        state.peakAt = time;
      }

      if (db >= 0) {
        this.clipped.set(true);
      }
    });

    const loudest = Math.round(Math.max(...this.states.map(s => s.level)));
    if (loudest !== this.announced()) {
      this.announced.set(loudest);
    }

    const ratio = window.devicePixelRatio || 1;
    const width = canvas.clientWidth;
    const height = canvas.clientHeight;
    if (canvas.width !== width * ratio || canvas.height !== height * ratio) {
      canvas.width = width * ratio;
      canvas.height = height * ratio;
    }

    const context = canvas.getContext('2d');
    const horizontal = this.orientation() === 'horizontal';
    const length = horizontal ? width : height;
    if (!context || length === 0) {
      return;
    }

    context.setTransform(ratio, 0, 0, ratio, 0, 0);
    context.clearRect(0, 0, width, height);
    const colors = this.colors(canvas);
    const bar = this.bar();
    const segment = this.segment();
    const zone = (db: number) => (db >= 0 ? colors.clip : db >= HotDb ? colors.hot : colors.ok);
    const dbAt = (position: number) => FloorDb + (position / length) * (CeilingDb - FloorDb);
    /** Paints a stretch of the scale (`at` from the floor end) across one channel's bar. */
    const paint = (channel: number, at: number, size: number) => {
      const across = channel * (bar + ChannelGap);
      if (horizontal) {
        context.fillRect(at, across, size, bar);
      } else {
        context.fillRect(across, height - at - size, bar, size);
      }
    };

    const pitch = segment + SegmentGap;
    this.states.forEach((state, channel) => {
      let peakSegment = -1;
      for (let at = 0; at + segment <= length; at += pitch) {
        const db = dbAt(at + segment / 2);
        if (state.peak > FloorDb && db <= state.peak) {
          peakSegment = at;
        }

        context.fillStyle = zone(db);
        context.globalAlpha = db <= state.level ? 1 : 0.2;
        paint(channel, at, segment);
      }

      if (peakSegment >= 0) {
        context.globalAlpha = 1;
        context.fillStyle = zone(dbAt(peakSegment + segment / 2));
        paint(channel, peakSegment, segment);
      }
    });

    // The 0 dB mark, in the gap before the first segment at or above 0 dB, so it never covers a segment.
    context.globalAlpha = 0.7;
    context.fillStyle = colors.mark;
    const zero = Math.ceil(((-FloorDb / (CeilingDb - FloorDb)) * length - segment / 2) / pitch) * pitch - SegmentGap;
    const span = this.thickness();
    if (horizontal) {
      context.fillRect(zero, 0, 1, span);
    } else {
      context.fillRect(0, height - zero, span, 1);
    }

    context.globalAlpha = 1;
  }
}
