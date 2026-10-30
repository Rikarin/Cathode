import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  type ElementRef,
  Injector,
  afterNextRender,
  booleanAttribute,
  computed,
  inject,
  input,
  model,
  numberAttribute,
  output,
  signal,
  viewChild
} from '@angular/core';
import { CTH_KNOB_PREFERENCES, circularNormal } from './preferences';
import { type Taper, formatValue, fromNormal, parseValue, toNormal } from './values';

/** Pixels of vertical drag for the whole range; ⇧ makes it ten times finer. */
const DragPixels = 200;
const Sweep = 270;
const StartAngle = 135; // degrees clockwise from 3 o'clock: the 7:30 position
/** After a key or wheel step on a modulated knob, how long it shows the value being set before following again. */
const SettleMs = 800;

/**
 * A rotary control (docs/02-architecture.md §7.3: xUI has none yet). It is an ARIA slider: arrows step by 1 % of
 * the travel (⇧: 0.1 %), Page keys by 10 %, Home/End go to the ends. Drag vertically (⇧ for fine), or around it in
 * circular mode, and use the wheel (both per the app's {@link CTH_KNOB_PREFERENCES}),
 * double-click the knob or its readout to type a value with its unit, ⌥-click to reset to the default. It reads like
 * hardware: the value in a recessed readout above, a turned-metal dial (Anode's, `--dial-*`) whose pointer shows
 * the position, the travel as an arc in `color`, and the name below.
 *
 * While a modifier drives the parameter (`modulated`), the pointer, arc and readout follow that live value, gliding
 * between telemetry frames, and a dot on the track marks the value set here, which the modifier works from. Editing
 * still sets that value: while dragging or typing, and briefly after a key or wheel step, the knob shows it instead.
 */
@Component({
  selector: 'cth-knob',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'inline-flex flex-col items-center gap-1.5 select-none',
    '[class.opacity-50]': 'disabled()',
    '[class.cth-knob-live]': 'live() !== null'
  },
  styles: `
    /* About one telemetry frame (33 ms) and a little more, so the pointer glides from one value to the next. */
    :host(.cth-knob-live) .cth-knob-moving {
      transition:
        transform 50ms linear,
        stroke-dasharray 50ms linear,
        stroke-dashoffset 50ms linear;
    }

    @media (prefers-reduced-motion: reduce) {
      :host(.cth-knob-live) .cth-knob-moving {
        transition: none;
      }
    }
  `,
  template: `
    @if (typing()) {
      <input
        #editor
        class="border-border bg-surface-inset text-foreground focus-visible:outline-focus h-6 w-20 rounded-md border px-1 text-center text-xs tabular-nums"
        [value]="display()"
        [attr.aria-label]="label() + ' value'"
        (keydown.enter)="commitTyping(editor.value)"
        (keydown.escape)="typing.set(false)"
        (blur)="commitTyping(editor.value)"
      />
    } @else {
      <span
        class="bg-surface-inset border-border-muted text-foreground flex h-6 min-w-16 items-center justify-center rounded-md border px-1.5 text-xs font-medium tabular-nums shadow-[inset_0_1px_2px_rgb(0_0_0/0.35)]"
        [attr.data-modulated]="live() !== null || null"
        (dblclick)="startTyping()"
      >
        {{ shownDisplay() }}
      </span>
    }
    <div
      #dial
      role="slider"
      class="focus-visible:outline-focus relative cursor-ns-resize touch-none rounded-full outline-offset-2 focus-visible:outline-2"
      [style.width.px]="pixels()"
      [style.height.px]="pixels()"
      [attr.tabindex]="disabled() ? -1 : 0"
      [attr.aria-labelledby]="labelId"
      [attr.aria-valuemin]="min()"
      [attr.aria-valuemax]="max()"
      [attr.aria-valuenow]="value()"
      [attr.aria-valuetext]="display()"
      [attr.aria-disabled]="disabled() || null"
      [attr.title]="tooltips() ? tooltip() : null"
      (pointerdown)="grab($event)"
      (pointermove)="drag($event)"
      (pointerup)="release($event)"
      (pointercancel)="release($event)"
      (lostpointercapture)="release($event)"
      (dblclick)="startTyping()"
      (keydown)="key($event)"
      (wheel)="wheel($event)"
    >
      <svg viewBox="0 0 100 100" class="size-full overflow-visible" aria-hidden="true">
        <defs>
          <linearGradient [attr.id]="capId" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" style="stop-color: var(--dial-top)" />
            <stop offset="100%" style="stop-color: var(--dial-bottom)" />
          </linearGradient>
          <radialGradient [attr.id]="shineId" cx="50%" cy="18%" r="55%">
            <stop offset="0%" style="stop-color: var(--dial-shine)" />
            <stop offset="100%" style="stop-color: var(--dial-shine); stop-opacity: 0" />
          </radialGradient>
        </defs>
        <path
          [attr.d]="track"
          class="fill-none"
          stroke-width="7"
          stroke-linecap="round"
          style="stroke: var(--dial-track)"
        />
        <!-- The value's stretch of the track, as a dash, so it can glide while modulated. -->
        <path
          [attr.d]="track"
          [attr.pathLength]="Sweep"
          class="cth-knob-moving fill-none"
          stroke-width="7"
          stroke-linecap="round"
          [style.visibility]="fill().empty ? 'hidden' : null"
          [style.stroke-dasharray]="fill().length + ' ' + 2 * Sweep"
          [style.stroke-dashoffset]="-fill().offset"
          [style.stroke]="arcColor()"
        />
        @if (live() !== null) {
          <circle
            data-base-marker
            [attr.cx]="baseMarker().x"
            [attr.cy]="baseMarker().y"
            r="3.6"
            fill="rgb(255 255 255 / 0.85)"
            stroke="rgb(0 0 0 / 0.55)"
            stroke-width="1.2"
          />
        }
        <!-- Anode's turned-metal body: graded, a shine on its upper edge, a dark rim and a short shadow. -->
        <circle cx="50" cy="52" r="31" fill="rgb(0 0 0 / 0.4)" style="filter: blur(1.5px)" />
        <circle cx="50" cy="50" r="30.5" [attr.fill]="'url(#' + capId + ')'" />
        <circle cx="50" cy="50" r="30.5" [attr.fill]="'url(#' + shineId + ')'" />
        <circle cx="50" cy="50" r="31" fill="none" stroke-width="2" style="stroke: var(--dial-rim)" />
        <path d="M 27 41 A 24.5 24.5 0 0 1 73 41" fill="none" stroke-width="1" style="stroke: var(--dial-shine)" />
        <line
          class="cth-knob-moving"
          [attr.x1]="pointerFrom.x"
          [attr.y1]="pointerFrom.y"
          [attr.x2]="pointerTo.x"
          [attr.y2]="pointerTo.y"
          stroke-width="4"
          stroke-linecap="round"
          style="stroke: var(--dial-pointer); transform-origin: 50px 50px"
          [style.transform]="'rotate(' + shownNormal() * Sweep + 'deg)'"
        />
      </svg>
    </div>
    <span class="text-foreground-muted line-clamp-2 max-w-full text-center text-xs leading-tight" [id]="labelId">
      {{ label() }}
    </span>
  `
})
export class CthKnob {
  private static nextId = 0;
  private readonly instance = CthKnob.nextId++;
  protected readonly labelId = `cth-knob-label-${this.instance}`;
  protected readonly capId = `cth-knob-cap-${this.instance}`;
  protected readonly shineId = `cth-knob-shine-${this.instance}`;
  private readonly dial = viewChild.required<ElementRef<HTMLElement>>('dial');
  private readonly editor = viewChild<ElementRef<HTMLInputElement>>('editor');
  private dragStart: { y: number; normal: number } | null = null;
  private settle: ReturnType<typeof setTimeout> | undefined;
  /** Wheel steps not yet committed: they are, once the wheel rests or another interaction starts. */
  private wheeling = false;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.settle));
  }
  private readonly preferences = inject(CTH_KNOB_PREFERENCES, { optional: true });
  private readonly injector = inject(Injector);

  readonly value = model(0);
  readonly min = input(0, { transform: numberAttribute });
  readonly max = input(1, { transform: numberAttribute });
  readonly defaultValue = input<number | undefined, unknown>(undefined, {
    transform: (v: unknown) => (v === undefined || v === null || v === '' ? undefined : numberAttribute(v))
  });
  readonly taper = input<Taper>('linear');
  readonly unit = input('');
  readonly label = input('');
  readonly size = input<'sm' | 'md' | 'lg'>('md');
  readonly disabled = input(false, { transform: booleanAttribute });
  /** The value arc's colour (any CSS colour); the theme's primary when unset. */
  readonly color = input<string | null>(null);
  /**
   * The parameter's live value while a modifier drives it (from telemetry), or null/undefined when none does. The
   * knob then shows it, with a mark at `value`, the value the modifier works from.
   */
  readonly modulated = input<number | null | undefined>(undefined);

  /**
   * Emitted when an interaction ends (drag release, typed value, key, reset, the wheel coming to rest): a good moment
   * for an undo point. Every change of `value` the user makes is followed by one before the next interaction starts.
   */
  readonly committed = output<number>();

  /** Linear or circular dragging; the app's preference unless set here. */
  readonly mode = input<'linear' | 'circular' | null>(null);

  protected readonly typing = signal(false);
  /** True while the user sets the value (dragging, or just after a key or wheel step): the knob shows `value` then. */
  protected readonly editing = signal(false);
  protected readonly Sweep = Sweep;
  protected readonly dragMode = computed(() => this.mode() ?? this.preferences?.mode() ?? 'linear');
  protected readonly tooltips = computed(() => this.preferences?.tooltips() ?? true);
  protected readonly pixels = computed(() => ({ sm: 40, md: 52, lg: 68 })[this.size()]);
  protected readonly normal = computed(() => toNormal(this.value(), this.range()));
  protected readonly display = computed(() => formatValue(this.value(), this.unit()));
  /** The modulated value the knob shows, or null while it shows `value` (no modifier, or the user is editing). */
  readonly live = computed(() => {
    const value = this.modulated();
    if (value === null || value === undefined || !Number.isFinite(value) || this.editing() || this.typing()) {
      return null;
    }

    return Math.min(this.max(), Math.max(this.min(), value));
  });
  /** The value the pointer, arc and readout show: the modulated one, or else the value set. */
  readonly shown = computed(() => this.live() ?? this.value());
  protected readonly shownNormal = computed(() => toNormal(this.shown(), this.range()));
  protected readonly shownDisplay = computed(() => formatValue(this.shown(), this.unit()));
  protected readonly tooltip = computed(() =>
    this.live() === null
      ? `${this.label()}: ${this.display()} (double-click to type, ⌥-click to reset)`
      : `${this.label()}: ${this.shownDisplay()}, modulated from ${this.display()} (double-click to type, ⌥-click to reset)`
  );
  protected readonly track = arc(0, 1);
  /** The shown value's stretch of the track, in degrees along it (bipolar ranges fill from the centre). */
  protected readonly fill = computed(() => {
    const n = this.shownNormal();
    const origin = this.min() < 0 && this.max() > 0 ? toNormal(0, this.range()) : 0;
    return {
      offset: Math.min(origin, n) * Sweep,
      length: Math.abs(n - origin) * Sweep,
      empty: Math.abs(n - origin) < 0.002
    };
  });
  /** The pointer at the start of travel; it is turned to the shown value, so it can glide. */
  protected readonly pointerTo = point(0, 25);
  protected readonly pointerFrom = point(0, 9);
  /** Where the value set sits on the track, marked while the knob shows a modulated value. */
  protected readonly baseMarker = computed(() => point(this.normal(), 42));
  protected readonly arcColor = computed(() => this.color() ?? 'var(--color-primary)');
  private readonly range = computed(() => ({ min: this.min(), max: this.max(), taper: this.taper() }));

  protected grab(event: PointerEvent): void {
    if (this.disabled() || event.button !== 0) {
      return;
    }

    if (event.altKey) {
      this.reset();
      return;
    }

    this.commitWheel();
    this.dial().nativeElement.setPointerCapture(event.pointerId);
    this.dial().nativeElement.focus();
    this.dragStart = { y: event.clientY, normal: this.normal() };
    clearTimeout(this.settle);
    this.editing.set(true);
    if (this.dragMode() === 'circular') {
      this.turnTo(event);
    }
  }

  protected drag(event: PointerEvent): void {
    if (!this.dragStart) {
      return;
    }

    // No button held: the release went somewhere else (the desktop app's title bar swallows it), so end here.
    if (event.buttons === 0) {
      this.release(event);
      return;
    }

    if (this.dragMode() === 'circular') {
      this.turnTo(event);
      return;
    }

    const scale = event.shiftKey ? DragPixels * 10 : DragPixels;
    // Re-anchor on every move so pressing or releasing ⇧ mid-drag does not jump.
    const normal = this.dragStart.normal + (this.dragStart.y - event.clientY) / scale;
    this.dragStart = { y: event.clientY, normal: Math.min(1, Math.max(0, normal)) };
    this.setNormal(this.dragStart.normal);
  }

  protected release(event: PointerEvent): void {
    if (this.dragStart) {
      this.dragStart = null;
      this.dial().nativeElement.releasePointerCapture(event.pointerId);
      this.editing.set(false);
      this.committed.emit(this.value());
    }
  }

  protected key(event: KeyboardEvent): void {
    this.commitWheel();
    const fine = event.shiftKey ? 0.001 : 0.01;
    const steps: Record<string, number> = {
      ArrowUp: fine,
      ArrowRight: fine,
      ArrowDown: -fine,
      ArrowLeft: -fine,
      PageUp: 0.1,
      PageDown: -0.1
    };
    const step = steps[event.key];
    if (step !== undefined) {
      this.setNormal(this.normal() + step);
    } else if (event.key === 'Home' || event.key === 'End') {
      this.setNormal(event.key === 'Home' ? 0 : 1);
    } else if (event.key === 'Enter') {
      this.startTyping();
      return;
    } else {
      return;
    }

    event.preventDefault();
    this.stepped();
    this.committed.emit(this.value());
  }

  protected wheel(event: WheelEvent): void {
    const wheel = this.preferences?.wheel() ?? 'normal';
    if (this.disabled() || event.deltaY === 0 || wheel === 'off') {
      return; // off: the wheel scrolls the page, as over anything else
    }

    event.preventDefault();
    const direction = wheel === 'reverse' ? -1 : 1;
    this.setNormal(this.normal() - direction * Math.sign(event.deltaY) * (event.shiftKey ? 0.002 : 0.01));
    this.wheeling = true;
    this.stepped();
  }

  protected startTyping(): void {
    if (this.disabled()) {
      return;
    }

    this.commitWheel();
    this.typing.set(true);
    // After the input renders: a microtask runs before change detection, while it is not there yet.
    afterNextRender(
      () => {
        const field = this.editor()?.nativeElement;
        field?.focus();
        field?.select();
      },
      { injector: this.injector }
    );
  }

  protected commitTyping(text: string): void {
    if (!this.typing()) {
      return;
    }

    this.typing.set(false);
    const parsed = parseValue(text, this.unit());
    if (parsed !== null) {
      this.value.set(Math.min(this.max(), Math.max(this.min(), parsed)));
      this.committed.emit(this.value());
    }

    this.dial().nativeElement.focus();
  }

  /** After a key or wheel step: show the value being set for a moment, then follow the modifier again. */
  private stepped(): void {
    if (this.dragStart) {
      return;
    }

    clearTimeout(this.settle);
    this.editing.set(true);
    this.settle = setTimeout(() => {
      this.editing.set(false);
      this.commitWheel();
    }, SettleMs);
  }

  private commitWheel(): void {
    if (this.wheeling) {
      this.wheeling = false;
      this.committed.emit(this.value());
    }
  }

  /** Circular mode: the knob points where the pointer is, seen from its centre. */
  private turnTo(event: PointerEvent): void {
    const rect = this.dial().nativeElement.getBoundingClientRect();
    const normal = circularNormal(
      event.clientX - (rect.left + rect.width / 2),
      event.clientY - (rect.top + rect.height / 2)
    );
    if (normal !== null) {
      this.setNormal(normal);
    }
  }

  private reset(): void {
    const value = this.defaultValue();
    if (value !== undefined) {
      this.value.set(value);
      this.committed.emit(value);
    }
  }

  private setNormal(normal: number): void {
    if (!this.disabled()) {
      this.value.set(fromNormal(normal, this.range()));
    }
  }
}

function point(normal: number, radius: number): { x: number; y: number } {
  const angle = ((StartAngle + normal * Sweep) * Math.PI) / 180;
  return { x: 50 + radius * Math.cos(angle), y: 50 + radius * Math.sin(angle) };
}

function arc(from: number, to: number): string {
  const start = point(from, 42);
  const end = point(to, 42);
  const large = (to - from) * Sweep > 180 ? 1 : 0;
  return `M ${start.x} ${start.y} A 42 42 0 ${large} 1 ${end.x} ${end.y}`;
}
