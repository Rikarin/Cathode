import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import type {
  FootswitchOverride,
  GlobalSettings,
  ParameterInfo,
  PresetBlock,
  PresetModifier,
  SwitchAction
} from '@cathode/api-client';
import { CthKnob, CthToggle } from '@cathode/audio-ui';
import { provideIcons } from '@ng-icons/core';
import {
  matDashboardCustomizeRound,
  matDynamicFeedRound,
  matLinkOffRound,
  matSettingsInputComponentRound,
  matTuneRound
} from '@ng-icons/material-icons/round';
import { XuiContextMenuImports } from '@xui/context-menu';
import { XuiMenuImports } from '@xui/menu';
import { XuiSelectImports } from '@xui/select';
import { XuiTooltipImports } from '@xui/tooltip';
import { ModifierDialog } from '../controls/modifier-dialog';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';
import { withControl } from '../pages/perform';
import { GlobalStore } from '../state/global.store';
import { LiveStore } from '../state/live.store';
import { UiStore } from '../state/ui.store';

/**
 * One parameter of the selected block, generated from its descriptor: a knob for continuous values, a select for
 * choices, a switch for on/off. Edits go to the channel the block uses in the active scene.
 * Right-click a continuous parameter to give it a modifier or learn a MIDI controller for it; a dot marks a modified
 * parameter. While its modifier runs, the control follows the live value (a knob marks the value set on its track).
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
    XuiSelectImports,
    XuiTooltipImports
  ],
  providers: [
    provideIcons({
      matDashboardCustomizeRound,
      matDynamicFeedRound,
      matLinkOffRound,
      matSettingsInputComponentRound,
      matTuneRound
    })
  ],
  host: { class: 'block' },
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
            [modulated]="modulated()"
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
            <xui-menu-divider />
            <button xuiMenuItem icon="matDashboardCustomizeRound" (click)="addToPerform('preset')">
              Add to the preset's Perform page
            </button>
            <button xuiMenuItem icon="matDashboardCustomizeRound" (click)="addToPerform('global')">
              Add to the global Perform page
            </button>
            <button xuiMenuItem icon="matDynamicFeedRound" (click)="batchSet()">Set in other presets…</button>
          </xui-menu>
        </ng-template>
        <cth-modifier-dialog [block]="block().id" [param]="param()" [(open)]="editing" />
      }
      @case ('enum') {
        @if (variant() === 'sidebar') {
          <div class="flex flex-col gap-1.5" [xuiContextMenuTriggerFor]="settingMenu">
            <span class="text-foreground-muted text-[11px] font-semibold tracking-wide uppercase" [id]="labelId()">
              {{ param().name }}
            </span>
            <xui-select
              class="w-full"
              [aria-labelledby]="labelId()"
              [filterable]="false"
              [items]="options()"
              [value]="options()[shown()] ?? null"
              (valueChange)="set(Math.max(0, options().indexOf($event ?? '')))"
            />
          </div>
        } @else {
          <!-- One knob-sized cell, so knobs, toggles and selects make a uniform grid. -->
          <div class="flex w-20 flex-col items-center gap-1.5" [xuiContextMenuTriggerFor]="settingMenu">
            <span class="h-6" aria-hidden="true"></span>
            <div class="flex h-[52px] w-full items-center">
              <xui-select
                class="cth-compact-select w-full"
                [aria-labelledby]="labelId()"
                [matchTargetWidth]="false"
                [filterable]="false"
                [items]="options()"
                [value]="options()[shown()] ?? null"
                (valueChange)="set(Math.max(0, options().indexOf($event ?? '')))"
              />
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
          [xuiContextMenuTriggerFor]="settingMenu"
          [label]="param().name"
          [color]="accent()"
          [checked]="shown() >= 0.5"
          (checkedChange)="set($event ? 1 : 0)"
        />
      }
    }
    <!-- A switch or choice: what applies to any setting (modifiers and MIDI learn drive continuous values). -->
    <ng-template #settingMenu>
      <xui-menu>
        <button xuiMenuItem icon="matDashboardCustomizeRound" (click)="addToPerform('preset')">
          Add to the preset's Perform page
        </button>
        <button xuiMenuItem icon="matDashboardCustomizeRound" (click)="addToPerform('global')">
          Add to the global Perform page
        </button>
        <button xuiMenuItem icon="matDynamicFeedRound" (click)="batchSet()">Set in other presets…</button>
      </xui-menu>
    </ng-template>
  `
})
export class ParamControl {
  private readonly live = inject(LiveStore);
  private readonly connection = inject(ConnectionService);
  private readonly notify = inject(Notify);
  private readonly ui = inject(UiStore);
  private readonly globals = inject(GlobalStore);
  private readonly events = inject(EventStream);

  readonly block = input.required<PresetBlock>();
  readonly param = input.required<ParameterInfo>();
  /** The knobs' arc colour: the block's category colour. */
  readonly accent = input<string | null>(null);
  /** `rack` lines controls up as knob-sized cells; `sidebar` stacks a choice as a label over a full-width select. */
  readonly variant = input<'rack' | 'sidebar'>('rack');
  /** Emits each value the user sets, after it has gone to the live rig (the Cab panel unmutes an edited slot). */
  readonly edited = output<number>();

  protected readonly labelId = computed(() => `param-${this.block().id}-${this.param().id}`);
  protected readonly value = computed(() => {
    const block = this.block();
    return this.live.valueOf(block, this.live.channelOf(block.id), this.param().id) ?? this.param().default;
  });
  protected readonly options = computed(() => this.param().options ?? []);
  protected readonly Math = Math;

  protected readonly editing = signal(false);
  protected readonly modifier = computed(() => this.live.modifierOf(this.block().id, this.param().id));
  /** The live value while a modifier drives this parameter (telemetry), else undefined. */
  protected readonly modulated = computed(() => this.events.modulatedValue(this.block().id, this.param().id));
  /** What a switch or choice shows: the modulated value, rounded to a step, or the value set. */
  protected readonly shown = computed(() => {
    const modulated = this.modulated();
    return modulated === undefined ? this.value() : Math.round(modulated);
  });

  /** Puts this control on a Perform page: the preset's, or the global one shown in every preset with this block. */
  protected addToPerform(page: 'preset' | 'global'): void {
    const control = { block: this.block().id, param: this.param().id, label: null };
    if (page === 'preset') {
      void this.live.setPerform(withControl(this.live.preset()?.perform ?? [], control));
    } else {
      void this.globals.update(g => (g.perform = withControl(g.perform, control)));
    }
  }

  /** Parameter Batch Set: this value into the same block of other presets. */
  protected batchSet(): void {
    const block = this.block();
    this.ui.batchSet.set({ block, param: this.param(), channel: this.live.channelOf(block.id), value: this.value() });
  }

  protected removeModifier(): void {
    void this.live.putModifier(this.block().id, this.param().id, null);
  }

  /**
   * MIDI learn for a knob, the Axe-Fx way: drive it from a free External controller over its whole range, and bind
   * the next CC moved to that controller. Free means no global mapping or footswitch drives it and no modifier of this
   * preset uses it: mappings are global, so an External another preset learned may already follow another pedal.
   */
  protected async learn(): Promise<void> {
    const preset = this.live.preset();
    const n = freeExternal(this.globals.settings(), preset?.modifiers ?? [], preset?.footswitchOverrides ?? []);
    const p = this.param();
    if (n === null) {
      this.notify.error(
        'All eight Externals are in use. Remove an External mapping on the Global page, ' +
          'or give this knob a modifier from an External that is already mapped.'
      );
      return;
    }

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
    this.edited.emit(value);
  }
}

/**
 * The first External (1-8) nothing drives or uses yet, or null: not a global MIDI mapping or footswitch action
 * (`external n`), not a footswitch action of the preset, and not a modifier source of the preset.
 */
export function freeExternal(
  settings: Pick<GlobalSettings, 'midiMappings' | 'footswitches'> | null,
  modifiers: readonly Pick<PresetModifier, 'source'>[],
  overrides: readonly FootswitchOverride[]
): number | null {
  const actions: (SwitchAction | null | undefined)[] = [
    ...(settings?.midiMappings ?? []),
    ...(settings?.footswitches ?? []).flatMap(l => l.switches.flatMap(s => [s.tap, s.hold])),
    ...overrides.flatMap(o => [o.tap, o.hold])
  ];
  const used = new Set([
    ...modifiers.map(m => m.source),
    ...actions.filter(a => a?.action === 'external').map(a => `ext${a!.value ?? 1}`)
  ]);
  return [1, 2, 3, 4, 5, 6, 7, 8].find(i => !used.has(`ext${i}`)) ?? null;
}
