import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import type { ParameterInfo, PresetBlock } from '@cathode/api-client';
import { CthKnob, CthToggle } from '@cathode/audio-ui';
import { provideIcons } from '@ng-icons/core';
import { matLinkOffRound, matSettingsInputComponentRound, matTuneRound } from '@ng-icons/material-icons/round';
import { XuiContextMenuImports } from '@xui/context-menu';
import { XuiMenuImports } from '@xui/menu';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { XuiSelectImports } from '@xui/select';
import { XuiTooltipImports } from '@xui/tooltip';
import { ModifierDialog } from '../controls/modifier-dialog';
import { ConnectionService } from '../core/connection';
import { Notify } from '../core/notify';
import { LiveStore } from '../state/live.store';

/**
 * One parameter of the selected block, generated from its descriptor: a knob for continuous values, a segmented
 * control or select for choices, a switch for on/off. Edits go to the channel the block uses in the active scene.
 * Right-click a continuous parameter to give it a modifier or learn a MIDI controller for it; a dot marks a modified
 * parameter, whose value then follows its controller.
 */
@Component({
  selector: 'cth-param-control',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CthKnob,
    CthToggle,
    ModifierDialog,
    XuiContextMenuImports,
    XuiMenuImports,
    XuiSegmentedControlImports,
    XuiSelectImports,
    XuiTooltipImports
  ],
  providers: [provideIcons({ matLinkOffRound, matSettingsInputComponentRound, matTuneRound })],
  host: { class: 'contents' },
  template: `
    @switch (param().kind) {
      @case ('float') {
        <div class="relative flex w-20 justify-center" [xuiContextMenuTriggerFor]="menu">
          @if (modifier(); as m) {
            <span
              class="bg-primary absolute top-8 right-2 size-2 rounded-full"
              [xuiTooltip]="'Modified by ' + m.source"
              [attr.aria-label]="'Modified by ' + m.source"
            ></span>
          }
          <cth-knob
            [attr.data-testid]="'param-' + param().id"
            [label]="param().name"
            [unit]="param().unit"
            [min]="param().min"
            [max]="param().max"
            [defaultValue]="param().default"
            [taper]="param().taper === 'log' ? 'log' : 'linear'"
            [color]="accent()"
            [value]="value()"
            (valueChange)="set($event)"
          />
        </div>
        <ng-template #menu>
          <xui-menu>
            <button xuiMenuItem icon="matTuneRound" (click)="editing.set(true)">
              {{ modifier() ? 'Edit modifier…' : 'Add modifier…' }}
            </button>
            <button xuiMenuItem icon="matSettingsInputComponentRound" (click)="learn()">Learn MIDI controller</button>
            @if (modifier()) {
              <button xuiMenuItem icon="matLinkOffRound" color="error" (click)="removeModifier()">
                Remove modifier
              </button>
            }
          </xui-menu>
        </ng-template>
        <cth-modifier-dialog [block]="block().id" [param]="param()" [(open)]="editing" />
      }
      @case ('enum') {
        @if (variant() === 'sidebar') {
          <div class="flex flex-col gap-1.5">
            <span class="text-foreground-muted text-[11px] font-semibold tracking-wide uppercase" [id]="labelId()">
              {{ param().name }}
            </span>
            <xui-select
              class="w-full"
              [aria-labelledby]="labelId()"
              [filterable]="false"
              [items]="options()"
              [value]="options()[value()] ?? null"
              (valueChange)="set(Math.max(0, options().indexOf($event ?? '')))"
            />
          </div>
        } @else {
          <div class="flex w-40 flex-col items-center gap-1.5">
            <span class="h-6" aria-hidden="true"></span>
            <div class="flex h-[52px] w-full items-center">
              @if (segmented()) {
                <xui-segmented-control
                  size="sm"
                  fill
                  [aria-label]="param().name"
                  [options]="choices()"
                  [value]="value()"
                  (valueChange)="set($event ?? 0)"
                />
              } @else {
                <xui-select
                  class="w-full"
                  [aria-labelledby]="labelId()"
                  [filterable]="false"
                  [items]="options()"
                  [value]="options()[value()] ?? null"
                  (valueChange)="set(Math.max(0, options().indexOf($event ?? '')))"
                />
              }
            </div>
            <span class="text-foreground-muted text-center text-xs leading-tight" [id]="labelId()">
              {{ param().name }}
            </span>
          </div>
        }
      }
      @case ('bool') {
        <cth-toggle
          class="w-20"
          [label]="param().name"
          [color]="accent()"
          [checked]="value() >= 0.5"
          (checkedChange)="set($event ? 1 : 0)"
        />
      }
    }
  `
})
export class ParamControl {
  private readonly live = inject(LiveStore);
  private readonly connection = inject(ConnectionService);
  private readonly notify = inject(Notify);

  readonly block = input.required<PresetBlock>();
  readonly param = input.required<ParameterInfo>();
  /** The knobs' arc colour: the block's category colour. */
  readonly accent = input<string | null>(null);
  /** `rack` lines controls up as knob-sized cells; `sidebar` stacks a choice as a label over a full-width select. */
  readonly variant = input<'rack' | 'sidebar'>('rack');

  protected readonly labelId = computed(() => `param-${this.block().id}-${this.param().id}`);
  protected readonly value = computed(() => {
    const block = this.block();
    return this.live.valueOf(block, this.live.channelOf(block.id), this.param().id) ?? this.param().default;
  });
  protected readonly options = computed(() => this.param().options ?? []);
  protected readonly choices = computed(() => this.options().map((label, value) => ({ label, value })));
  protected readonly segmented = computed(() => this.options().length <= 3);
  protected readonly Math = Math;

  protected readonly editing = signal(false);
  protected readonly modifier = computed(() => this.live.modifierOf(this.block().id, this.param().id));

  protected removeModifier(): void {
    void this.live.putModifier(this.block().id, this.param().id, null);
  }

  /**
   * MIDI learn for a knob, the Axe-Fx way: drive it from a free External controller over its whole range, and bind
   * the next CC moved to that controller.
   */
  protected async learn(): Promise<void> {
    const used = new Set((this.live.preset()?.modifiers ?? []).map(m => m.source));
    const n = [1, 2, 3, 4, 5, 6, 7, 8].find(i => !used.has(`ext${i}`)) ?? 1;
    const p = this.param();
    const added = await this.live.putModifier(this.block().id, p.id, {
      block: this.block().id,
      param: p.id,
      source: `ext${n}`,
      min: p.min,
      max: p.max,
      curve: 'linear',
      attackMs: 0,
      releaseMs: 0
    });
    if (added) {
      await this.notify.run(async () => {
        await this.connection.client.PUT('/api/v1/midi/learn', {
          body: { action: 'external', target: null, value: n }
        });
        this.notify.success(`Move a pedal or knob on your MIDI controller to control ${p.name} (External ${n}).`);
      });
    }
  }

  protected set(value: number): void {
    const block = this.block();
    this.live.setParam(block.id, this.live.channelOf(block.id), this.param().id, value);
  }
}
