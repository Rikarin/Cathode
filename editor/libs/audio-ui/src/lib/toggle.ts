import { ChangeDetectionStrategy, Component, booleanAttribute, computed, input, model, output } from '@angular/core';

/**
 * A vertical toggle that sits in a row of knobs (cth-knob): a sunken well (the theme's `--well`) with a brushed-metal
 * cap (`--knob-top/bottom`, the switches' metal) that reads ON at the top and OFF at the bottom, an LED on the cap lit
 * in `color` when on with a faint wash of it in the well, and the name below. It is an ARIA switch: click or
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
      class="group/toggle focus-visible:outline-focus border-hairline-strong relative cursor-pointer rounded-[7px] border shadow-[inset_0_1px_3px_rgb(0_0_0/0.65),0_1px_0_var(--highlight)] outline-offset-2 focus-visible:outline-2 disabled:cursor-default"
      [style.width.px]="dimensions().width"
      [style.height.px]="dimensions().height"
      [style.background]="slot()"
      [disabled]="disabled()"
      [attr.aria-checked]="checked()"
      [attr.aria-labelledby]="labelId"
      [attr.title]="label() + ': ' + (checked() ? onLabel() : offLabel())"
      (click)="flip()"
      (keydown)="key($event)"
    >
      <span
        class="absolute inset-x-[3px] flex flex-col items-center justify-center gap-[5px] rounded-[5px] transition-[top] duration-150 ease-out motion-reduce:transition-none"
        [style.top.px]="checked() ? 2 : dimensions().height - dimensions().handle - 4"
        [style.height.px]="dimensions().handle"
        style="
          background: linear-gradient(180deg, var(--knob-top) 0%, var(--knob-bottom) 100%);
          box-shadow:
            0 0 0 0.5px rgb(0 0 0 / 0.6),
            0 2px 4px rgb(0 0 0 / 0.5),
            inset 0 1px 0 rgb(255 255 255 / 0.7),
            inset 0 -1px 0 rgb(0 0 0 / 0.15);
        "
        aria-hidden="true"
      >
        <span
          class="block size-[5px] rounded-full transition-[background,box-shadow] duration-150"
          [style.background]="checked() ? glow() : 'rgb(0 0 0 / 0.35)'"
          [style.box-shadow]="
            checked() ? '0 0 4px ' + glow() + ', 0 0 1px ' + glow() : 'inset 0 1px 1px rgb(0 0 0 / 0.4)'
          "
        ></span>
        <span
          class="font-semibold tracking-wider text-[#2a2a2e]/80"
          [class]="size() === 'sm' ? 'text-[9px]' : 'text-[10px]'"
        >
          {{ checked() ? onLabel() : offLabel() }}
        </span>
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
      ? `linear-gradient(180deg, color-mix(in oklab, ${this.glow()} 22%, var(--well)) 0%, var(--well) 60%)`
      : 'var(--well)'
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
