import { ChangeDetectionStrategy, Component, booleanAttribute, computed, input, output, signal } from '@angular/core';

/** Arrow keys step by this share of the range; ⇧ makes it ten times finer, Page keys ten times coarser. */
const KeyStep = 0.01;

/** A change to one bar. */
export interface StepBarChange {
  index: number;
  value: number;
}

/**
 * A row of bars for a step sequencer's stages, each from 0 at the bottom to 1 at the top, drawn like the rest of the
 * hardware: a recessed panel with faint quarter lines, each bar a channel lit in `color` up to its value, the stage
 * number below and the value above while it is hovered, focused or drawn.
 *
 * `active` marks the stage playing, as a step sequencer's LEDs do: its channel takes a faint wash of `color` behind the
 * fill and a lit strip glows in the panel's margin below it. Neither moves anything, and neither touches the hover,
 * focus and drawing states, which show the value and the outline as before.
 *
 * Press and drag across the bars to draw the pattern: every bar the pointer passes, the skipped ones included, takes
 * the height under it. Each bar is also an ARIA slider: ↑/↓ step by 1 % (⇧: 0.1 %), Page keys by 10 %, Home/End go
 * to the ends, ←/→ move to the neighbouring bar.
 */
@Component({
  selector: 'cth-step-bars',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'block select-none',
    '[class.opacity-50]': 'disabled()'
  },
  template: `
    <div
      class="relative grid touch-none gap-1 rounded-lg border border-black/40 p-1.5"
      [class.cursor-crosshair]="!disabled()"
      [style.height.px]="height()"
      [style.grid-template-columns]="'repeat(' + values().length + ', minmax(0, 1fr))'"
      style="
        background: #0b0c0e;
        box-shadow:
          inset 0 2px 4px rgb(0 0 0 / 0.6),
          0 1px 0 rgb(255 255 255 / 0.06);
      "
      role="group"
      [attr.aria-label]="label()"
      (pointerdown)="press($event)"
      (pointermove)="drag($event)"
      (pointerup)="release($event)"
      (pointercancel)="release($event)"
      (lostpointercapture)="drawing.set(null)"
    >
      <!-- Quarter lines, behind the bars. -->
      @for (line of [0.25, 0.5, 0.75]; track line) {
        <span
          class="pointer-events-none absolute inset-x-1.5 border-t border-dashed border-white/[0.06]"
          [style.bottom]="'calc(6px + ' + line + ' * (100% - 12px))'"
          aria-hidden="true"
        ></span>
      }

      @for (value of values(); track $index; let i = $index) {
        <div
          class="group/bar focus-visible:outline-focus relative rounded-[5px] bg-white/[0.035] outline-offset-1 focus-visible:outline-2"
          role="slider"
          tabindex="0"
          aria-orientation="vertical"
          aria-valuemin="0"
          aria-valuemax="100"
          [attr.aria-valuenow]="percent(value)"
          [attr.aria-valuetext]="percent(value) + ' %'"
          [attr.aria-label]="'Stage ' + (i + 1)"
          [attr.aria-disabled]="disabled() || null"
          [attr.data-stage]="i + 1"
          [attr.data-active]="active() === i || null"
          (keydown)="key($event, i)"
          (focus)="focused.set(i)"
          (blur)="focused.set(null)"
        >
          <!-- The playing stage: a wash behind the fill and an LED strip in the margin below; both fade, neither moves. -->
          <span
            class="pointer-events-none absolute inset-0 rounded-[5px] transition-opacity duration-100 motion-reduce:transition-none"
            [style.background]="wash()"
            [style.opacity]="active() === i ? 1 : 0"
            aria-hidden="true"
          ></span>
          <span
            class="pointer-events-none absolute inset-x-1 -bottom-[5px] h-[2px] rounded-full transition-opacity duration-100 motion-reduce:transition-none"
            [style.background]="color()"
            [style.box-shadow]="glow()"
            [style.opacity]="active() === i ? 1 : 0"
            aria-hidden="true"
          ></span>
          <span
            class="absolute inset-x-0 bottom-0 rounded-[5px] transition-[height] duration-75 ease-out motion-reduce:transition-none"
            [style.height.%]="value * 100"
            [style.background]="fill()"
            [style.box-shadow]="value > 0 ? glow() : 'none'"
            aria-hidden="true"
          >
            <!-- The cap: a bright edge at the bar's value, so even a low bar reads. -->
            <span
              class="absolute inset-x-0 top-0 h-[3px] rounded-[5px]"
              [style.background]="color()"
              [style.opacity]="value > 0 ? 1 : 0.35"
            ></span>
          </span>
          <span
            class="text-foreground pointer-events-none absolute inset-x-0 -top-0.5 text-center text-[10px] leading-none font-medium tabular-nums opacity-0 transition-opacity group-hover/bar:opacity-100"
            [class.opacity-100]="focused() === i || drawing() === i"
            [style.transform]="'translateY(' + (value > 0.85 ? '4px' : '-120%') + ')'"
            [style.top]="(1 - value) * 100 + '%'"
            aria-hidden="true"
          >
            {{ percent(value) }}
          </span>
        </div>
      }
    </div>

    <div
      class="mt-1 grid gap-1 px-1.5"
      [style.grid-template-columns]="'repeat(' + values().length + ', minmax(0, 1fr))'"
      aria-hidden="true"
    >
      @for (value of values(); track $index; let i = $index) {
        <span
          class="text-center text-[10px] tabular-nums"
          [class]="focused() === i || drawing() === i ? 'text-foreground' : 'text-foreground-muted'"
        >
          {{ i + 1 }}
        </span>
      }
    </div>
  `
})
export class CthStepBars {
  /** The stages' values, 0 to 1. */
  readonly values = input<readonly number[]>([]);
  /** The group's accessible name. */
  readonly label = input('Stages');
  /** The panel's height in pixels, the stage numbers below it not counted. */
  readonly height = input(144);
  readonly disabled = input(false, { transform: booleanAttribute });
  /** The bars' colour (any CSS colour); the theme's primary when unset. */
  readonly color = input('var(--color-primary)');
  /** The stage playing (from 0), marked live; null for none (as is a stage beyond the bars). */
  readonly active = input<number | null>(null);

  /** Every change, while drawing too. */
  readonly changed = output<StepBarChange>();
  /** After a drawing stroke or a key: a good moment for an undo point. */
  readonly committed = output();

  /** The bar a stroke last set, null when not drawing. */
  protected readonly drawing = signal<number | null>(null);
  protected readonly focused = signal<number | null>(null);

  protected readonly fill = computed(
    () =>
      `linear-gradient(180deg, color-mix(in oklab, ${this.color()} 88%, white 4%) 0%, ` +
      `color-mix(in oklab, ${this.color()} 55%, transparent) 45%, color-mix(in oklab, ${this.color()} 20%, transparent) 100%)`
  );
  protected readonly glow = computed(() => `0 0 10px color-mix(in oklab, ${this.color()} 30%, transparent)`);
  protected readonly wash = computed(() => `color-mix(in oklab, ${this.color()} 14%, transparent)`);

  /** The values the current stroke has drawn, so a fast stroke fills the bars between two pointer events. */
  private last: { index: number; value: number } | null = null;

  protected percent(value: number): number {
    return Math.round(value * 100);
  }

  protected press(event: PointerEvent): void {
    if (this.disabled() || event.button !== 0) {
      return;
    }

    const panel = event.currentTarget as HTMLElement;
    panel.setPointerCapture(event.pointerId);
    this.last = null;
    this.drawAt(panel, event);
    event.preventDefault();
  }

  protected drag(event: PointerEvent): void {
    if (this.last !== null) {
      this.drawAt(event.currentTarget as HTMLElement, event);
    }
  }

  protected release(event: PointerEvent): void {
    if (this.last === null) {
      return;
    }

    (event.currentTarget as HTMLElement).releasePointerCapture(event.pointerId);
    this.last = null;
    this.drawing.set(null);
    this.committed.emit();
  }

  protected key(event: KeyboardEvent, index: number): void {
    if (this.disabled()) {
      return;
    }

    if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
      event.preventDefault();
      const next = index + (event.key === 'ArrowLeft' ? -1 : 1);
      const bar = (event.currentTarget as HTMLElement).parentElement?.querySelector<HTMLElement>(
        `[data-stage="${next + 1}"]`
      );
      bar?.focus();
      return;
    }

    const value = this.values()[index] ?? 0;
    const step = KeyStep * (event.shiftKey ? 0.1 : 1);
    const target =
      event.key === 'ArrowUp'
        ? value + step
        : event.key === 'ArrowDown'
          ? value - step
          : event.key === 'PageUp'
            ? value + KeyStep * 10
            : event.key === 'PageDown'
              ? value - KeyStep * 10
              : event.key === 'Home'
                ? 0
                : event.key === 'End'
                  ? 1
                  : null;
    if (target === null) {
      return;
    }

    event.preventDefault();
    this.set(index, target);
    this.committed.emit();
  }

  /** Sets the bar under the pointer, and every bar between it and the one the stroke last set. */
  private drawAt(panel: HTMLElement, event: PointerEvent): void {
    const bars = Array.from(panel.querySelectorAll<HTMLElement>('[data-stage]'));
    const [firstBar, lastBar] = [bars[0], bars.at(-1)];
    if (!firstBar || !lastBar) {
      return;
    }

    const first = firstBar.getBoundingClientRect();
    const last = lastBar.getBoundingClientRect();
    const width = (last.right - first.left) / bars.length;
    const index = Math.min(bars.length - 1, Math.max(0, Math.floor((event.clientX - first.left) / width)));
    const value = 1 - (event.clientY - first.top) / first.height;

    const from = this.last ?? { index, value };
    const span = Math.abs(index - from.index);
    for (let i = 0; i <= span; i++) {
      const at = from.index + Math.sign(index - from.index) * i;
      this.set(at, span === 0 ? value : from.value + ((value - from.value) * i) / span);
    }

    this.last = { index, value };
    this.drawing.set(index);
  }

  private set(index: number, value: number): void {
    const clamped = Math.round(Math.min(1, Math.max(0, value)) * 1000) / 1000;
    if (clamped !== this.values()[index]) {
      this.changed.emit({ index, value: clamped });
    }
  }
}
