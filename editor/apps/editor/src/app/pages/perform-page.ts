import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CthKnob, CthToggle, blockAccent } from '@cathode/audio-ui';
import { provideIcons } from '@ng-icons/core';
import { matCloseRound } from '@ng-icons/material-icons/round';
import { XuiButtonImports } from '@xui/button';
import { XuiIconImports } from '@xui/icon';
import { XuiNonIdealStateImports } from '@xui/non-ideal-state';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { XuiSelectImports } from '@xui/select';
import { EventStream } from '../core/events';
import { CatalogStore } from '../state/catalog.store';
import { GlobalStore } from '../state/global.store';
import { LiveStore } from '../state/live.store';
import { type ResolvedControl, resolveControls, withoutControl } from './perform';

/**
 * Perform: big controls for playing live. The preset's page travels with the preset; the global page shows in every
 * preset that has its blocks. Add a control from any knob's context menu. A control a modifier drives follows its
 * live value.
 */
@Component({
  selector: 'cth-perform-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CthKnob,
    CthToggle,
    XuiButtonImports,
    XuiIconImports,
    XuiNonIdealStateImports,
    XuiSegmentedControlImports,
    XuiSelectImports
  ],
  providers: [provideIcons({ matCloseRound })],
  host: { class: 'flex h-full flex-col gap-6 overflow-auto p-6' },
  template: `
    <header class="flex items-center gap-4">
      <h1 class="text-lg font-semibold">Perform</h1>
      <xui-segmented-control
        size="sm"
        aria-label="Page"
        [options]="pages"
        [value]="page()"
        (valueChange)="$event && page.set($event)"
      />
    </header>
    @if (controls().length === 0) {
      <xui-non-ideal-state
        class="flex-1"
        title="Nothing on this page yet"
        description="Right-click a knob on a block's page and choose Add to the Perform page."
      />
    } @else {
      <div class="grid grid-cols-[repeat(auto-fill,minmax(10rem,1fr))] gap-6" data-testid="perform-grid">
        @for (c of controls(); track c.control.block + '/' + c.control.param) {
          <div
            class="bg-surface-raised border-border relative flex min-h-48 flex-col items-center justify-center gap-2 rounded-xl border p-4"
          >
            <button
              xuiButton
              variant="ghost"
              size="sm"
              class="absolute top-1 right-1"
              [attr.aria-label]="'Remove ' + c.label"
              (click)="remove(c)"
            >
              <ng-icon xui name="matCloseRound" size="sm" />
            </button>
            @if (c.block && c.param; as param) {
              @switch (param.kind) {
                @case ('bool') {
                  <cth-toggle
                    [label]="c.label"
                    [color]="accent(c)"
                    [checked]="shown(c) >= 0.5"
                    (checkedChange)="set(c, $event ? 1 : 0)"
                  />
                }
                @case ('enum') {
                  <span class="text-sm font-medium">{{ c.label }}</span>
                  <xui-select
                    class="w-full"
                    [attr.aria-label]="c.label"
                    [filterable]="false"
                    [items]="param.options ?? []"
                    [value]="(param.options ?? [])[shown(c)] ?? null"
                    (valueChange)="set(c, Math.max(0, (param.options ?? []).indexOf($event ?? '')))"
                  />
                }
                @default {
                  <cth-knob
                    size="lg"
                    [label]="c.label"
                    [unit]="param.unit"
                    [min]="param.min"
                    [max]="param.max"
                    [defaultValue]="param.default"
                    [taper]="param.taper === 'log' ? 'log' : 'linear'"
                    [color]="accent(c)"
                    [value]="value(c)"
                    [modulated]="modulated(c)"
                    (valueChange)="set(c, $event)"
                  />
                }
              }
            } @else {
              <span class="text-foreground-muted text-center text-sm">{{ c.label }}: not in this preset</span>
            }
          </div>
        }
      </div>
    }
  `
})
export class PerformPage {
  private readonly live = inject(LiveStore);
  private readonly globals = inject(GlobalStore);
  private readonly catalog = inject(CatalogStore);
  private readonly events = inject(EventStream);

  protected readonly Math = Math;
  protected readonly pages = [
    { label: 'This preset', value: 'preset' as const },
    { label: 'Global', value: 'global' as const }
  ];
  protected readonly page = signal<'preset' | 'global'>('preset');
  protected readonly controls = computed(() =>
    resolveControls(
      this.page() === 'preset' ? (this.live.preset()?.perform ?? []) : (this.globals.settings()?.perform ?? []),
      this.live.preset(),
      type => this.catalog.info(type)
    )
  );

  protected value(c: ResolvedControl): number {
    return c.block && c.param
      ? (this.live.valueOf(c.block, this.live.channelOf(c.block.id), c.param.id) ?? c.param.default)
      : 0;
  }

  /** The live value while a modifier drives the control's parameter, else undefined. */
  protected modulated(c: ResolvedControl): number | undefined {
    return c.block && c.param ? this.events.modulatedValue(c.block.id, c.param.id) : undefined;
  }

  /** What a switch or choice shows: the modulated value, rounded to a step, or the value set. */
  protected shown(c: ResolvedControl): number {
    const modulated = this.modulated(c);
    return modulated === undefined ? this.value(c) : Math.round(modulated);
  }

  protected set(c: ResolvedControl, value: number): void {
    if (c.block && c.param) {
      this.live.setParam(c.block.id, this.live.channelOf(c.block.id), c.param.id, value);
    }
  }

  protected accent(c: ResolvedControl): string | null {
    return c.block ? blockAccent(this.catalog.info(c.block.type)?.category ?? '') : null;
  }

  protected remove(c: ResolvedControl): void {
    if (this.page() === 'preset') {
      void this.live.setPerform(withoutControl(this.live.preset()?.perform ?? [], c.control));
    } else {
      void this.globals.update(g => (g.perform = withoutControl(g.perform, c.control)));
    }
  }
}
