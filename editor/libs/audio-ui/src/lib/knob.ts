import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  booleanAttribute,
  computed,
  input,
  model,
  numberAttribute,
  output,
  signal,
  viewChild
} from '@angular/core';
import { type Taper, formatValue, fromNormal, parseValue, toNormal } from './values';

/** Pixels of vertical drag for the whole range; ⇧ makes it ten times finer. */
const DragPixels = 200;
const Sweep = 270;
const StartAngle = 135; // degrees clockwise from 3 o'clock: the 7:30 position

/**
 * A rotary control (docs/02-architecture.md §7.3: xUI has none yet). It is an ARIA slider: arrows step by 1 % of
 * the travel (⇧: 0.1 %), Page keys by 10 %, Home/End go to the ends. Drag vertically (⇧ for fine), use the wheel,
 * double-click to type a value with its unit, ⌥-click to reset to the default.
 */
@Component({
  selector: 'cth-knob',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'inline-flex flex-col items-center gap-1 select-none',
    '[class.opacity-50]': 'disabled()'
  },
  template: `
    <span class="text-foreground-muted max-w-full truncate text-xs" [id]="labelId">{{ label() }}</span>
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
      [attr.title]="label() + ': ' + display() + ' (double-click to type, ⌥-click to reset)'"
      (pointerdown)="grab($event)"
      (pointermove)="drag($event)"
      (pointerup)="release($event)"
      (pointercancel)="release($event)"
      (dblclick)="startTyping()"
      (keydown)="key($event)"
      (wheel)="wheel($event)"
    >
      <svg viewBox="0 0 100 100" class="size-full overflow-visible">
        <path [attr.d]="track" class="stroke-border fill-none" stroke-width="8" stroke-linecap="round" />
        @if (fill(); as d) {
          <path [attr.d]="d" class="stroke-primary fill-none" stroke-width="8" stroke-linecap="round" />
        }
        <circle cx="50" cy="50" r="31" class="fill-surface-raised stroke-border-muted" stroke-width="1" />
        <line
          x1="50"
          y1="50"
          [attr.x2]="pointer().x"
          [attr.y2]="pointer().y"
          class="stroke-foreground"
          stroke-width="5"
          stroke-linecap="round"
        />
      </svg>
    </div>
    @if (typing()) {
      <input
        #editor
        class="border-border bg-surface-inset text-foreground focus-visible:outline-focus h-(--control-height-sm) w-20 rounded border px-1 text-center text-xs"
        [value]="display()"
        [attr.aria-label]="label() + ' value'"
        (keydown.enter)="commitTyping(editor.value)"
        (keydown.escape)="typing.set(false)"
        (blur)="commitTyping(editor.value)"
      />
    } @else {
      <span class="text-foreground text-xs tabular-nums">{{ display() }}</span>
    }
  `
})
export class CthKnob {
  private static nextId = 0;
  protected readonly labelId = `cth-knob-label-${CthKnob.nextId++}`;
  private readonly dial = viewChild.required<ElementRef<HTMLElement>>('dial');
  private readonly editor = viewChild<ElementRef<HTMLInputElement>>('editor');
  private dragStart: { y: number; normal: number } | null = null;

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

  /** Emitted when an interaction ends (drag release, typed value, key, reset): a good moment for an undo point. */
  readonly committed = output<number>();

  protected readonly typing = signal(false);
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
  }

  protected drag(event: PointerEvent): void {
    if (!this.dragStart) {
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
    if (this.disabled() || event.deltaY === 0) {
      return;
    }

    event.preventDefault();
    this.setNormal(this.normal() - Math.sign(event.deltaY) * (event.shiftKey ? 0.002 : 0.01));
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
