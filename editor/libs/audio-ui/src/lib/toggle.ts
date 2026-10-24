import { ChangeDetectionStrategy, Component, booleanAttribute, computed, input, model, output } from '@angular/core';

/**
 * A vertical toggle that sits in a row of knobs (cth-knob): a recessed slot with a machined handle that reads ON at the
 * top and OFF at the bottom, the slot glowing in `color` when on, and the name below. It is an ARIA switch: click or
 * Space/Enter flips it, ↑ turns it on and ↓ off.
 */
@Component({
  selector: 'cth-toggle',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'inline-flex flex-col items-center gap-1.5 select-none',
    '[class.opacity-50]': 'disabled()'
  },
  template: `
    <button
      type="button"
      role="switch"
      class="group/toggle focus-visible:outline-focus relative cursor-pointer rounded-lg border border-black/40 outline-offset-2 focus-visible:outline-2 disabled:cursor-default"
      [style.width.px]="dimensions().width"
      [style.height.px]="dimensions().height"
      [style.background]="slot()"
      [style.box-shadow]="slotShadow()"
      [disabled]="disabled()"
      [attr.aria-checked]="checked()"
      [attr.aria-labelledby]="labelId"
      [attr.title]="label() + ': ' + (checked() ? onLabel() : offLabel())"
      (click)="flip()"
      (keydown)="key($event)"
    >
      <span
        class="absolute inset-x-[3px] flex flex-col items-center justify-center gap-[3px] rounded-md border border-white/10 transition-[top] duration-150 ease-out motion-reduce:transition-none"
        [style.top.px]="checked() ? 3 : dimensions().height - dimensions().handle - 5"
        [style.height.px]="dimensions().handle"
        style="
          background: linear-gradient(180deg, #5b616c 0%, #2a2e35 55%, #141619 100%);
          box-shadow:
            0 3px 5px rgb(0 0 0 / 0.5),
            inset 0 1px 0 rgb(255 255 255 / 0.12);
        "
        aria-hidden="true"
      >
        @if (!checked()) {
          <span class="block h-px w-3 bg-black/50 shadow-[0_1px_0_rgb(255_255_255/0.08)]"></span>
        }
        <span
          class="font-semibold tracking-wider"
          [class]="size() === 'sm' ? 'text-[9px]' : 'text-[10px]'"
          [style.color]="checked() ? glow() : 'rgb(255 255 255 / 0.55)'"
          [style.text-shadow]="checked() ? '0 0 6px ' + glow() : 'none'"
        >
          {{ checked() ? onLabel() : offLabel() }}
        </span>
        @if (checked()) {
          <span class="block h-px w-3 bg-black/50 shadow-[0_1px_0_rgb(255_255_255/0.08)]"></span>
        }
      </span>
    </button>
    <span class="text-foreground-muted line-clamp-2 max-w-full text-center text-xs leading-tight" [id]="labelId">
      {{ label() }}
    </span>
  `
})
export class CthToggle {
  private static nextId = 0;
  protected readonly labelId = `cth-toggle-label-${CthToggle.nextId++}`;

  readonly checked = model(false);
  readonly label = input('');
  readonly onLabel = input('ON');
  readonly offLabel = input('OFF');
  /** `md` lines up with a md knob and its readout, `sm` with a sm knob. */
  readonly size = input<'sm' | 'md'>('md');
  readonly disabled = input(false, { transform: booleanAttribute });
  /** The glow when on (any CSS colour); the theme's primary when unset. */
  readonly color = input<string | null>(null);

  /** Emitted after the user flips it: a good moment for an undo point. */
  readonly committed = output<boolean>();

  protected readonly dimensions = computed(() =>
    this.size() === 'sm' ? { width: 30, height: 70, handle: 32 } : { width: 34, height: 82, handle: 38 }
  );
  protected readonly glow = computed(() => this.color() ?? 'var(--color-primary)');
  protected readonly slot = computed(() =>
    this.checked()
      ? `linear-gradient(180deg, color-mix(in oklab, ${this.glow()} 45%, #0b0c0e) 0%, #0b0c0e 70%)`
      : '#0b0c0e'
  );
  protected readonly slotShadow = computed(
    () =>
      'inset 0 2px 4px rgb(0 0 0 / 0.6), 0 1px 0 rgb(255 255 255 / 0.06)' +
      (this.checked() ? `, 0 0 8px color-mix(in oklab, ${this.glow()} 35%, transparent)` : '')
  );

  protected flip(): void {
    this.set(!this.checked());
  }

  protected key(event: KeyboardEvent): void {
    if (event.key === 'ArrowUp' || event.key === 'ArrowDown') {
      event.preventDefault();
      this.set(event.key === 'ArrowUp');
    }
  }

  private set(checked: boolean): void {
    if (this.disabled() || checked === this.checked()) {
      return;
    }

    this.checked.set(checked);
    this.committed.emit(checked);
  }
}
