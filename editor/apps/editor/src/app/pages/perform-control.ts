import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { CthKnob, CthToggle, blockAccent } from '@cathode/audio-ui';
import { provideIcons } from '@ng-icons/core';
import { matCloseRound } from '@ng-icons/material-icons/round';
import { XuiButtonImports } from '@xui/button';
import { XuiIconImports } from '@xui/icon';
import { XuiSelectImports } from '@xui/select';
import { EventStream } from '../core/events';
import { CatalogStore } from '../state/catalog.store';
import { LiveStore } from '../state/live.store';
import type { ResolvedControl } from './perform';

/**
 * One control on the Perform page. Its modulated value is a computed of its own, as in ParamControl, so telemetry
 * re-checks only the controls whose modifier moved, not the whole page.
 */
@Component({
  selector: 'cth-perform-control',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CthKnob, CthToggle, XuiButtonImports, XuiIconImports, XuiSelectImports],
  providers: [provideIcons({ matCloseRound })],
  host: {
    class:
      'bg-surface-raised border-border relative flex min-h-48 flex-col items-center justify-center gap-2 rounded-xl border p-4'
  },
  template: `
    <button
      type="button"
      xuiButton
      variant="ghost"
      size="sm"
      class="absolute top-1 right-1"
      [attr.aria-label]="'Remove ' + control().label"
      (click)="remove.emit()"
    >
      <ng-icon xui name="matCloseRound" size="sm" />
    </button>
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
            (valueChange)="set($event)"
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

  readonly control = input.required<ResolvedControl>();
  readonly remove = output();

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

  protected set(value: number): void {
    const { block, param } = this.control();
    if (block && param) {
      this.live.setParam(block.id, this.live.channelOf(block.id), param.id, value);
    }
  }
}
