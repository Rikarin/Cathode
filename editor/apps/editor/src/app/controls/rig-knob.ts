import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { CthKnob, CthToggle } from '@cathode/audio-ui';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { XuiSelectImports } from '@xui/select';
import { CatalogStore } from '../state/catalog.store';

/** One rig parameter (from GET /catalog/rig) as a knob, a choice or a switch. */
@Component({
  selector: 'cth-rig-control',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CthKnob, XuiSegmentedControlImports, XuiSelectImports, CthToggle],
  host: { class: 'contents' },
  template: `
    @if (param(); as p) {
      @switch (p.kind) {
        @case ('enum') {
          <div class="flex min-w-28 flex-col gap-1">
            <span class="text-foreground-muted text-xs" [id]="'rig-' + id()">{{ label() ?? p.name }}</span>
            <xui-select
              class="w-32"
              [attr.aria-labelledby]="'rig-' + id()"
              [filterable]="false"
              [items]="options()"
              [value]="options()[value()] ?? null"
              (valueChange)="changed.emit(Math.max(0, options().indexOf($event ?? '')))"
            />
          </div>
        }
        @case ('bool') {
          <cth-toggle
            size="sm"
            [label]="label() ?? p.name"
            [checked]="value() >= 0.5"
            (checkedChange)="changed.emit($event ? 1 : 0)"
          />
        }
        @default {
          <cth-knob
            size="sm"
            [label]="label() ?? p.name"
            [unit]="p.unit"
            [min]="p.min"
            [max]="p.max"
            [defaultValue]="p.default"
            [taper]="p.taper === 'log' ? 'log' : 'linear'"
            [value]="value()"
            (valueChange)="changed.emit($event)"
          />
        }
      }
    }
  `
})
export class RigControl {
  private readonly catalog = inject(CatalogStore);

  readonly id = input.required<string>();
  readonly label = input<string>();
  /** The stored value, or undefined for the parameter's default. */
  readonly stored = input<number | undefined>();
  readonly changed = output<number>();

  protected readonly param = computed(() => this.catalog.rigParams().get(this.id()));
  protected readonly value = computed(() => this.stored() ?? this.param()?.default ?? 0);
  // Labels rather than indices as items: xui-select treats a falsy value (index 0) as empty.
  protected readonly options = computed(() => this.param()?.options ?? []);
  protected readonly Math = Math;
}
