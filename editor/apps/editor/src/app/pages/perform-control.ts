import {
  ChangeDetectionStrategy,
  Component,
  type ElementRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild
} from '@angular/core';
import { CthKnob, CthToggle, blockAccent } from '@cathode/audio-ui';
import { provideIcons } from '@ng-icons/core';
import {
  matArrowBackRound,
  matArrowForwardRound,
  matCloseRound,
  matDragIndicatorRound,
  matEditRound
} from '@ng-icons/material-icons/round';
import { XuiButtonImports } from '@xui/button';
import { XuiIconImports } from '@xui/icon';
import { XuiInputImports } from '@xui/input';
import { XuiSelectImports } from '@xui/select';
import { EventStream } from '../core/events';
import { CatalogStore } from '../state/catalog.store';
import { KnobGesture } from '../state/knob-gesture';
import { LiveStore } from '../state/live.store';
import type { ResolvedControl } from './perform';

/** The drag data type of a Perform control being moved by its handle. */
export const PerformDragType = 'application/x-cathode-perform';

/**
 * One control on the Perform page. Its modulated value is a computed of its own, as in ParamControl, so telemetry
 * re-checks only the controls whose modifier moved, not the whole page. Its toolbar renames it (a caption of its own,
 * blank for the parameter's name), moves it earlier or later, or removes it; the grip drags it to another place.
 */
@Component({
  selector: 'cth-perform-control',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CthKnob, CthToggle, XuiButtonImports, XuiIconImports, XuiInputImports, XuiSelectImports],
  providers: [
    provideIcons({ matArrowBackRound, matArrowForwardRound, matCloseRound, matDragIndicatorRound, matEditRound })
  ],
  host: {
    class:
      'bg-surface-raised border-border relative flex min-h-48 flex-col items-center justify-center gap-2 rounded-xl border p-4 pt-9'
  },
  template: `
    <div class="absolute inset-x-1 top-1 flex items-center" role="toolbar" [attr.aria-label]="control().label">
      <span
        class="text-foreground-muted flex cursor-grab items-center px-1"
        draggable="true"
        title="Drag to move"
        aria-hidden="true"
        data-testid="perform-drag"
        (dragstart)="dragStart($event)"
      >
        <ng-icon xui name="matDragIndicatorRound" size="sm" />
      </span>
      <button
        type="button"
        xuiButton
        variant="ghost"
        size="sm"
        [disabled]="first()"
        [attr.aria-label]="'Move ' + control().label + ' earlier'"
        (click)="move.emit(-1)"
      >
        <ng-icon xui name="matArrowBackRound" size="sm" />
      </button>
      <button
        type="button"
        xuiButton
        variant="ghost"
        size="sm"
        [disabled]="last()"
        [attr.aria-label]="'Move ' + control().label + ' later'"
        (click)="move.emit(1)"
      >
        <ng-icon xui name="matArrowForwardRound" size="sm" />
      </button>
      <span class="flex-1"></span>
      <button
        type="button"
        xuiButton
        variant="ghost"
        size="sm"
        [attr.aria-label]="'Rename ' + control().label"
        [attr.aria-pressed]="editing()"
        (mousedown)="editing() && $event.preventDefault()"
        (click)="editing() ? commit(labelField()?.nativeElement?.value ?? '') : editing.set(true)"
      >
        <ng-icon xui name="matEditRound" size="sm" />
      </button>
      <button
        type="button"
        xuiButton
        variant="ghost"
        size="sm"
        [attr.aria-label]="'Remove ' + control().label"
        (click)="remove.emit()"
      >
        <ng-icon xui name="matCloseRound" size="sm" />
      </button>
    </div>
    @if (editing()) {
      <input
        #labelField
        xuiInput
        size="sm"
        class="w-full"
        data-testid="perform-label"
        [attr.aria-label]="'Label of ' + control().label"
        [placeholder]="control().name"
        [value]="control().control.label ?? ''"
        (keydown.enter)="commit(labelField.value)"
        (keydown.escape)="editing.set(false)"
        (blur)="commit(labelField.value)"
      />
    }
    @if (control().block && control().param; as param) {
      @switch (param.kind) {
        @case ('bool') {
          <cth-toggle
            [label]="control().label"
            [color]="accent()"
            [checked]="shown() >= 0.5"
            (checkedChange)="set($event ? 1 : 0)"
          />
        }
        @case ('enum') {
          <span class="text-sm font-medium">{{ control().label }}</span>
          <xui-select
            class="w-full"
            [attr.aria-label]="control().label"
            [filterable]="false"
            [items]="param.options ?? []"
            [value]="(param.options ?? [])[shown()] ?? null"
            (valueChange)="set(Math.max(0, (param.options ?? []).indexOf($event ?? '')))"
          />
        }
        @default {
          <cth-knob
            size="lg"
            [label]="control().label"
            [unit]="param.unit"
            [min]="param.min"
            [max]="param.max"
            [defaultValue]="param.default"
            [taper]="param.taper === 'log' ? 'log' : 'linear'"
            [color]="accent()"
            [value]="value()"
            [modulated]="modulated()"
            (valueChange)="set($event, gesture.current())"
            (committed)="gesture.end()"
          />
        }
      }
    } @else {
      <span class="text-foreground-muted text-center text-sm">{{ control().label }}: not in this preset</span>
    }
  `
})
export class PerformControl {
  private readonly live = inject(LiveStore);
  private readonly catalog = inject(CatalogStore);
  private readonly events = inject(EventStream);
  /** The knob's gesture: one drag is one undo step, and the next drag another (the knob's `committed` ends it). */
  protected readonly gesture = new KnobGesture();

  readonly control = input.required<ResolvedControl>();
  /** First or last on the page: it cannot move earlier, or later. */
  readonly first = input(false);
  readonly last = input(false);
  readonly remove = output();
  /** A caption of its own, or null for the parameter's name. */
  readonly rename = output<string | null>();
  /** One place earlier (-1) or later (1). */
  readonly move = output<-1 | 1>();

  protected readonly editing = signal(false);
  protected readonly labelField = viewChild<ElementRef<HTMLInputElement>>('labelField');

  protected readonly Math = Math;

  protected readonly value = computed(() => {
    const { block, param } = this.control();
    return block && param ? (this.live.valueOf(block, this.live.channelOf(block.id), param.id) ?? param.default) : 0;
  });

  /** The live value while a modifier drives the control's parameter, else undefined. */
  protected readonly modulated = computed(() => {
    const { block, param } = this.control();
    return block && param ? this.events.modulatedValue(block.id, param.id) : undefined;
  });

  /** What a switch or choice shows: the modulated value, rounded to a step, or the value set. */
  protected readonly shown = computed(() => {
    const modulated = this.modulated();
    return modulated === undefined ? this.value() : Math.round(modulated);
  });

  protected readonly accent = computed(() => {
    const block = this.control().block;
    return block ? blockAccent(this.catalog.info(block.type)?.category ?? '') : null;
  });

  constructor() {
    // The label field takes the focus as it opens, so it can be typed into at once.
    effect(() => this.labelField()?.nativeElement.focus());
  }

  /** Ends renaming with `text` as the caption (blank: the parameter's name); a blur after Enter or Escape is ignored. */
  protected commit(text: string): void {
    if (!this.editing()) {
      return;
    }

    this.editing.set(false);
    const label = text.trim() || null;
    if (label !== (this.control().control.label ?? null)) {
      this.rename.emit(label);
    }
  }

  protected dragStart(event: DragEvent): void {
    const transfer = event.dataTransfer;
    if (transfer) {
      transfer.effectAllowed = 'move';
      transfer.setData(PerformDragType, this.control().label);
      const card = (event.currentTarget as HTMLElement | null)?.closest('cth-perform-control');
      if (card) {
        transfer.setDragImage(card, 16, 16);
      }
    }
  }

  protected set(value: number, gesture?: string): void {
    const { block, param } = this.control();
    if (block && param) {
      this.live.setParam(block.id, this.live.channelOf(block.id), param.id, value, gesture);
    }
  }
}
