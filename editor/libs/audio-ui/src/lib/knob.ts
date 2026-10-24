import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
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

/**
 * A rotary control (docs/02-architecture.md §7.3: xUI has none yet). It is an ARIA slider: arrows step by 1 % of
 * the travel (⇧: 0.1 %), Page keys by 10 %, Home/End go to the ends. Drag vertically (⇧ for fine), or around it in
 * circular mode, and use the wheel (both per the app's {@link CTH_KNOB_PREFERENCES}),
 * double-click the knob or its readout to type a value with its unit, ⌥-click to reset to the default. It reads like
 * hardware: the value in a recessed readout above, a machined cap whose pointer shows the position, the travel as an
 * arc in `color`, and the name below.
 */
@Component({
  selector: 'cth-knob',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'inline-flex flex-col items-center gap-1.5 select-none',
    '[class.opacity-50]': 'disabled()'
  },
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
        (dblclick)="startTyping()"
      >
        {{ display() }}
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
      [attr.title]="tooltips() ? label() + ': ' + display() + ' (double-click to type, ⌥-click to reset)' : null"
      (pointerdown)="grab($event)"
      (pointermove)="drag($event)"
      (pointerup)="release($event)"
      (pointercancel)="release($event)"
      (dblclick)="startTyping()"
      (keydown)="key($event)"
      (wheel)="wheel($event)"
    >
      <svg viewBox="0 0 100 100" class="size-full overflow-visible" aria-hidden="true">
        <defs>
          <radialGradient [attr.id]="capId" cx="38%" cy="30%" r="75%">
            <stop offset="0%" stop-color="#5b616c" />
            <stop offset="55%" stop-color="#2a2e35" />
            <stop offset="100%" stop-color="#141619" />
          </radialGradient>
        </defs>
        <path [attr.d]="track" class="stroke-border fill-none" stroke-width="7" stroke-linecap="round" />
        @if (fill(); as d) {
          <path
            [attr.d]="d"
            class="fill-none"
            stroke-width="7"
            stroke-linecap="round"
            [style.stroke]="arcColor()"
            [style.filter]="'drop-shadow(0 0 3px ' + arcColor() + ')'"
          />
        }
        <circle cx="50" cy="53" r="33" fill="rgb(0 0 0 / 0.45)" style="filter: blur(3px)" />
        <circle
          cx="50"
          cy="50"
          r="32"
          [attr.fill]="'url(#' + capId + ')'"
          stroke="rgb(255 255 255 / 0.1)"
          stroke-width="1"
        />
        <circle cx="50" cy="50" r="26" fill="none" stroke="rgb(0 0 0 / 0.35)" stroke-width="1" />
        <line
          [attr.x1]="pointerFrom().x"
          [attr.y1]="pointerFrom().y"
          [attr.x2]="pointer().x"
          [attr.y2]="pointer().y"
          stroke="white"
          stroke-width="4.5"
          stroke-linecap="round"
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
  private readonly dial = viewChild.required<ElementRef<HTMLElement>>('dial');
  private readonly editor = viewChild<ElementRef<HTMLInputElement>>('editor');
  private dragStart: { y: number; normal: number } | null = null;
  private readonly preferences = inject(CTH_KNOB_PREFERENCES, { optional: true });

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

  /** Emitted when an interaction ends (drag release, typed value, key, reset): a good moment for an undo point. */
  readonly committed = output<number>();

  /** Linear or circular dragging; the app's preference unless set here. */
  readonly mode = input<'linear' | 'circular' | null>(null);

  protected readonly typing = signal(false);
  protected readonly dragMode = computed(() => this.mode() ?? this.preferences?.mode() ?? 'linear');
  protected readonly tooltips = computed(() => this.preferences?.tooltips() ?? true);
  protected readonly pixels = computed(() => ({ sm: 40, md: 52, lg: 68 })[this.size()]);
  protected readonly normal = computed(() => toNormal(this.value(), this.range()));
  protected readonly display = computed(() => formatValue(this.value(), this.unit()));
  protected readonly track = arc(0, 1);
  protected readonly fill = computed(() => {
    const n = this.normal();
    // Bipolar ranges (-12..+12 dB) fill from the centre, the rest from the start.
    const origin = this.min() < 0 && this.max() > 0 ? toNormal(0, this.range()) : 0;
    return Math.abs(n - origin) < 0.002 ? null : arc(Math.min(origin, n), Math.max(origin, n));
  });
  protected readonly pointer = computed(() => point(this.normal(), 27));
  protected readonly pointerFrom = computed(() => point(this.normal(), 13));
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

    this.dial().nativeElement.setPointerCapture(event.pointerId);
    this.dial().nativeElement.focus();
    this.dragStart = { y: event.clientY, normal: this.normal() };
    if (this.dragMode() === 'circular') {
      this.turnTo(event);
    }
  }

  protected drag(event: PointerEvent): void {
    if (!this.dragStart) {
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
      this.committed.emit(this.value());
    }
  }

  protected key(event: KeyboardEvent): void {
    const fine = event.shiftKey ? 0.001 : 0.01;
    const steps: Record<string, number> = {
      ArrowUp: fine,
      ArrowRight: fine,
      ArrowDown: -fine,
      ArrowLeft: -fine,
      PageUp: 0.1,
      PageDown: -0.1
    };
    if (event.key in steps) {
      this.setNormal(this.normal() + steps[event.key]);
    } else if (event.key === 'Home' || event.key === 'End') {
      this.setNormal(event.key === 'Home' ? 0 : 1);
    } else if (event.key === 'Enter') {
      this.startTyping();
      return;
    } else {
      return;
    }

    event.preventDefault();
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
  }

  protected startTyping(): void {
    if (this.disabled()) {
      return;
    }

    this.typing.set(true);
    queueMicrotask(() => this.editor()?.nativeElement.select());
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
