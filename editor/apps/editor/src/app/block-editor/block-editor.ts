import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { BuiltInTypes, BypassModes, type ChannelName, ChannelNames, type ParameterInfo } from '@cathode/api-client';
import { CthBlockTile, blockAccent } from '@cathode/audio-ui';
import { provideIcons } from '@ng-icons/core';
import {
  matContentCopyRound,
  matLinkOffRound,
  matLinkRound,
  matMoreHorizRound,
  matPublicRound,
  matRestartAltRound,
  matSaveRound,
  matSwapHorizRound,
  matTuneRound
} from '@ng-icons/material-icons/round';
import { XuiButtonImports } from '@xui/button';
import { XuiContextMenuImports } from '@xui/context-menu';
import { XuiDialogImports } from '@xui/dialog';
import { XuiFormFieldImports } from '@xui/form-field';
import { XuiIconImports } from '@xui/icon';
import { XuiInputImports } from '@xui/input';
import { XuiMenuImports } from '@xui/menu';
import { XuiNonIdealStateImports } from '@xui/non-ideal-state';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { XuiSelectImports } from '@xui/select';
import { XuiSwitchImports } from '@xui/switch';
import { XuiTagImports } from '@xui/tag';
import { XuiTextImports } from '@xui/text';
import { ModifierDialog } from '../controls/modifier-dialog';
import { BlockPresetsStore } from '../state/block-presets.store';
import { CatalogStore } from '../state/catalog.store';
import { GlobalStore } from '../state/global.store';
import { LiveStore } from '../state/live.store';
import { AmpPanel } from './panels/amp-panel';
import { isCabSlotParam } from './panels/cab';
import { CabPanel } from './panels/cab-panel';
import { isCustomTableParam } from './panels/custom-shifter';
import { IrPlayerPanel } from './panels/ir-player-panel';
import { PeqPanel } from './panels/peq-panel';
import { PitchPanel } from './panels/pitch-panel';
import { ToneMatchPanel } from './panels/tone-match-panel';
import { ParamControl } from './param-control';
import { labelledParams } from './type-labels';

const OutputParams = new Set(['level', 'mix', 'balance', 'pan']);

/**
 * The selected block's page, laid out like Axe-Edit's: a sidebar with the block's tile, engage, channel A-D (which
 * channel the block uses in the active scene, as on the Axe-Fx), Type and bypass mode; then a custom panel where the
 * block has one, and the parameters on a rack panel, the output stage set apart on the right. Knob arcs take the
 * block's category colour, as its tile does.
 */
@Component({
  selector: 'cth-block-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AmpPanel,
    CthBlockTile,
    CabPanel,
    IrPlayerPanel,
    ModifierDialog,
    XuiContextMenuImports,
    XuiDialogImports,
    XuiFormFieldImports,
    XuiInputImports,
    ParamControl,
    PeqPanel,
    PitchPanel,
    ToneMatchPanel,
    XuiNonIdealStateImports,
    XuiSegmentedControlImports,
    XuiSwitchImports,
    XuiTextImports,
    XuiButtonImports,
    XuiIconImports,
    XuiMenuImports,
    XuiSelectImports,
    XuiTagImports
  ],
  providers: [
    provideIcons({
      matContentCopyRound,
      matLinkOffRound,
      matLinkRound,
      matMoreHorizRound,
      matPublicRound,
      matRestartAltRound,
      matSaveRound,
      matSwapHorizRound,
      matTuneRound
    })
  ],
  host: { class: 'block h-full overflow-auto p-4' },
  template: `
    @if (block(); as block) {
      <section class="flex min-h-full gap-5" [attr.aria-label]="title() + ' settings'">
        <aside class="flex w-56 shrink-0 flex-col gap-3">
          <cth-block-tile
            class="h-20 w-full"
            size="lg"
            [name]="title()"
            [category]="category()"
            [typeId]="block.type"
            [bypassed]="live.isBypassed(block.id)"
          />
          <div class="-my-1 flex items-center gap-2">
            <h2 class="sr-only">{{ title() }}</h2>
            <span class="text-foreground-muted min-w-0 flex-1 truncate text-xs">{{ block.id }}</span>
            <button xuiButton variant="ghost" size="sm" aria-label="Block actions" [xuiMenuTriggerFor]="blockMenu">
              <ng-icon xui name="matMoreHorizRound" size="sm" />
            </button>
          </div>
          @if (globalBlock(); as shared) {
            <xui-tag minimal color="primary" icon="matPublicRound" class="-mt-2 self-start">
              Global: {{ shared.name }}
            </xui-tag>
          }

          @if (!isIo()) {
            <label
              [xuiContextMenuTriggerFor]="engageMenu"
              class="bg-surface-raised border-border flex items-center justify-between gap-3 rounded-lg border px-3 py-1.5 text-sm"
            >
              <span class="flex items-center gap-2">
                <span
                  class="size-2 rounded-full"
                  [class]="
                    live.isBypassed(block.id) ? 'bg-surface-sunken' : 'bg-success shadow-[0_0_6px_var(--color-success)]'
                  "
                  aria-hidden="true"
                ></span>
                {{ live.isBypassed(block.id) ? 'Bypassed' : 'Engaged' }}
              </span>
              <xui-switch
                data-testid="engage"
                aria-label="Engaged"
                [checked]="!live.isBypassed(block.id)"
                (checkedChange)="live.setBypass(block.id, !$event)"
              />
            </label>
            <ng-template #engageMenu>
              <xui-menu>
                <button xuiMenuItem icon="matTuneRound" (click)="editingEngage.set(true)">
                  {{ engageModifier() ? 'Edit engage modifier…' : 'Engage from a modifier…' }}
                </button>
                @if (engageModifier()) {
                  <button xuiMenuItem icon="matLinkOffRound" color="error" (click)="removeEngageModifier()">
                    Remove engage modifier
                  </button>
                }
              </xui-menu>
            </ng-template>
            <cth-modifier-dialog [block]="block.id" [param]="engageParam" [(open)]="editingEngage" />
            @if (engageModifier(); as m) {
              <span class="text-foreground-muted -mt-2 text-[11px]">Engaged by {{ m.source }} (at 50 % and above)</span>
            }
          }

          @if ((info()?.channels ?? 0) > 1) {
            <div class="flex flex-col gap-1.5">
              <span class="text-foreground-muted text-[11px] font-semibold tracking-wide uppercase" aria-hidden="true">
                Channel
              </span>
              <xui-segmented-control
                size="sm"
                fill
                aria-label="Channel"
                [options]="channels"
                [value]="live.channelOf(block.id)"
                (valueChange)="$event && live.setChannel(block.id, $event)"
              />
              <label class="text-foreground-muted flex items-center justify-between gap-3 text-xs">
                Scene Ignore on {{ live.channelOf(block.id) }}
                <xui-switch
                  data-testid="scene-ignore"
                  [attr.aria-label]="'Scene Ignore on channel ' + live.channelOf(block.id)"
                  [checked]="live.ignoresScenes(block, live.channelOf(block.id))"
                  (checkedChange)="live.setSceneIgnore(block.id, live.channelOf(block.id), $event)"
                />
              </label>
            </div>
          }

          @if (typeParam(); as type) {
            <cth-param-control variant="sidebar" [block]="block" [param]="type" />
          }

          @if (libraryPresets().length > 0) {
            <div class="flex flex-col gap-1.5">
              <span
                class="text-foreground-muted text-[11px] font-semibold tracking-wide uppercase"
                id="block-library-label"
              >
                Block Library
              </span>
              <xui-select
                class="w-full"
                data-testid="block-library"
                aria-labelledby="block-library-label"
                placeholder="Load a preset…"
                [items]="libraryIds()"
                [itemText]="libraryName"
                [value]="null"
                (valueChange)="$event && blockPresets.apply(block.id, $event)"
              />
            </div>
          }

          @if (!isIo()) {
            <div class="flex flex-col gap-1.5">
              <span
                class="text-foreground-muted text-[11px] font-semibold tracking-wide uppercase"
                id="bypass-mode-label"
              >
                Bypass mode
              </span>
              <xui-select
                class="w-full"
                aria-labelledby="bypass-mode-label"
                [filterable]="false"
                [items]="bypassModes"
                [itemText]="bypassModeName"
                [value]="block.bypassMode"
                (valueChange)="$event && live.setBypassMode(block.id, $event)"
              />
            </div>
          }

          <ng-template #blockMenu>
            <xui-menu>
              <button xuiMenuItem icon="matSaveRound" (click)="openSavePreset()">
                Save channel {{ live.channelOf(block.id) }} to the Block Library…
              </button>
              <xui-menu-divider />
              @if ((info()?.channels ?? 0) > 1) {
                <xui-menu-divider [title]="'Copy channel ' + live.channelOf(block.id) + ' to'" />
                @for (channel of channels; track channel.value) {
                  @if (channel.value !== live.channelOf(block.id)) {
                    <button
                      xuiMenuItem
                      icon="matContentCopyRound"
                      (click)="live.copyChannel(block.id, live.channelOf(block.id), channel.value)"
                    >
                      Channel {{ channel.label }}
                    </button>
                  }
                }
                <xui-menu-divider [title]="'Swap channel ' + live.channelOf(block.id) + ' with'" />
                @for (channel of channels; track channel.value) {
                  @if (channel.value !== live.channelOf(block.id)) {
                    <button
                      xuiMenuItem
                      icon="matSwapHorizRound"
                      (click)="live.swapChannels(block.id, live.channelOf(block.id), channel.value)"
                    >
                      Channel {{ channel.label }}
                    </button>
                  }
                }
                <xui-menu-divider />
                <button
                  xuiMenuItem
                  icon="matRestartAltRound"
                  (click)="live.resetChannel(block.id, live.channelOf(block.id))"
                >
                  Reset channel {{ live.channelOf(block.id) }}
                </button>
              }
              <button xuiMenuItem icon="matRestartAltRound" color="error" (click)="live.resetBlock(block.id)">
                Reset block (all channels)
              </button>
              <xui-menu-divider />
              @if (isIo()) {
                <!-- The grid's Input and Output cannot be global. -->
              } @else if (block.global) {
                <button xuiMenuItem icon="matLinkOffRound" (click)="live.unlinkGlobal(block.id)">
                  Unlink from global
                </button>
              } @else {
                <button
                  xuiMenuItem
                  icon="matPublicRound"
                  (click)="live.makeGlobal(block.id, title() + ' (' + block.id + ')')"
                >
                  Make global
                </button>
                @for (shared of sameTypeGlobals(); track shared.id) {
                  <button xuiMenuItem icon="matLinkRound" (click)="live.useGlobal(block.id, shared.id)">
                    Use “{{ shared.name }}”
                  </button>
                }
              }
            </xui-menu>
          </ng-template>
        </aside>

        <xui-dialog title="Save to the Block Library" icon="matSaveRound" [(open)]="savingPreset">
          <xui-dialog-body>
            <xui-form-field label="Name">
              <input
                xuiInput
                data-testid="block-preset-name"
                [value]="presetName()"
                (input)="presetName.set($any($event.target).value)"
                (keydown.enter)="savePreset()"
              />
            </xui-form-field>
          </xui-dialog-body>
          <xui-dialog-footer>
            <button xuiButton variant="ghost" type="button" (click)="savingPreset.set(false)">Cancel</button>
            <button xuiButton type="button" [disabled]="!presetName().trim()" (click)="savePreset()">Save</button>
          </xui-dialog-footer>
        </xui-dialog>

        <div class="flex min-w-0 flex-1 flex-col gap-4">
          @switch (block.type) {
            @case ('nam') {
              <cth-amp-panel [block]="block" />
            }
            @case ('cab') {
              <cth-cab-panel [block]="block" />
            }
            @case ('peq') {
              <cth-peq-panel [block]="block" />
            }
            @case ('irplayer') {
              <cth-ir-player-panel [block]="block" />
            }
            @case ('tonematch') {
              <cth-tone-match-panel [block]="block" />
            }
            @case ('pitch') {
              <cth-pitch-panel [block]="block" />
            }
          }

          @if (mainParams().length > 0 || outputParams().length > 0) {
            <div
              class="from-surface-raised to-surface border-border flex gap-6 rounded-xl border bg-linear-to-b p-5 shadow-[inset_0_1px_0_rgb(255_255_255/0.05)]"
            >
              @if (mainParams().length > 0) {
                <div class="flex min-w-0 flex-1 flex-wrap content-start gap-x-3 gap-y-5">
                  @for (param of mainParams(); track param.id) {
                    <cth-param-control [block]="block" [param]="param" [accent]="accent()" />
                  }
                </div>
              }
              @if (outputParams().length > 0) {
                <div
                  role="group"
                  aria-labelledby="block-output-label"
                  class="flex shrink-0 flex-col gap-3"
                  [class.border-s]="mainParams().length > 0"
                  [class.border-border]="mainParams().length > 0"
                  [class.ps-6]="mainParams().length > 0"
                >
                  <span
                    id="block-output-label"
                    class="text-foreground-muted text-[11px] font-semibold tracking-wide uppercase"
                  >
                    Output
                  </span>
                  <div class="flex flex-wrap gap-x-3 gap-y-5">
                    @for (param of outputParams(); track param.id) {
                      <cth-param-control [block]="block" [param]="param" [accent]="accent()" />
                    }
                  </div>
                </div>
              }
            </div>
          }
        </div>
      </section>
    } @else {
      <xui-non-ideal-state
        class="h-full"
        title="No block selected"
        description="Select a block in the grid to edit it, or double-click an empty cell to add one."
      />
    }
  `
})
export class BlockEditor {
  protected readonly live = inject(LiveStore);
  private readonly catalog = inject(CatalogStore);

  private readonly globals = inject(GlobalStore);

  protected readonly channels = ChannelNames.map(c => ({ label: c, value: c as ChannelName }));
  /** The pseudo-parameter a modifier drives to engage the block: engaged at 0.5 and above (ModNames.EngageParam). */
  protected readonly engageParam: ParameterInfo = {
    id: 'engage',
    name: 'Engage',
    kind: 'float',
    min: 0,
    max: 1,
    default: 1,
    unit: '',
    taper: 'linear'
  };
  protected readonly editingEngage = signal(false);
  protected readonly blockPresets = inject(BlockPresetsStore);
  protected readonly savingPreset = signal(false);
  protected readonly presetName = signal('');
  protected readonly libraryPresets = computed(() => {
    const block = this.block();
    return block ? this.blockPresets.forType(block.type) : [];
  });
  protected readonly libraryIds = computed(() => this.libraryPresets().map(p => p.id));
  protected readonly libraryName = (id: string) => {
    const preset = this.libraryPresets().find(p => p.id === id);
    return preset ? (preset.factory ? preset.name : `${preset.name} (mine)`) : id;
  };
  protected readonly engageModifier = computed(() => {
    const block = this.block();
    return block ? this.live.modifierOf(block.id, 'engage') : undefined;
  });
  protected readonly bypassModes = [...BypassModes];
  protected readonly bypassModeName = (mode: string) =>
    ({
      thru: 'Thru',
      mute: 'Mute',
      muteIn: 'Mute In',
      muteFxIn: 'Mute FX In (spillover)',
      muteOut: 'Mute Out',
      muteFxOut: 'Mute FX Out'
    })[mode] ?? mode;
  protected readonly globalBlock = computed(() => {
    const id = this.block()?.global;
    return id ? this.globals.settings()?.blocks.find(b => b.id === id) : undefined;
  });
  protected readonly sameTypeGlobals = computed(() =>
    (this.globals.settings()?.blocks ?? []).filter(b => b.type === this.block()?.type)
  );
  protected readonly block = computed(() => {
    const block = this.live.selected();
    return block && block.type !== BuiltInTypes.shunt ? block : null;
  });
  /** The grid's Input and Output: they have parameters and channels, but no bypass and no global link. */
  protected readonly isIo = computed(
    () => this.block()?.type === BuiltInTypes.input || this.block()?.type === BuiltInTypes.output
  );
  constructor() {
    void this.blockPresets.refresh();
  }

  protected openSavePreset(): void {
    this.presetName.set('');
    this.savingPreset.set(true);
  }

  protected async savePreset(): Promise<void> {
    const block = this.block();
    const name = this.presetName().trim();
    if (!block || !name) {
      return;
    }

    const values = block.channels[this.live.channelOf(block.id)] ?? {};
    await this.blockPresets.save(block.type, name, { ...values });
    this.savingPreset.set(false);
  }

  protected removeEngageModifier(): void {
    const block = this.block();
    if (block) {
      void this.live.putModifier(block.id, 'engage', null);
    }
  }

  protected readonly info = computed(() => (this.block() ? this.catalog.info(this.block()!.type) : undefined));
  protected readonly title = computed(() => this.info()?.name ?? this.block()?.type ?? '');
  protected readonly typeParam = computed(() => this.info()?.params.find(p => p.id === 'type') ?? null);
  /** The selected type (model) of the block, which names and hides per-model controls. */
  private readonly typeValue = computed(() => {
    const block = this.block();
    const type = this.typeParam();
    return block && type ? (this.live.valueOf(block, this.live.channelOf(block.id), 'type') ?? type.default) : 0;
  });
  private readonly otherParams = computed(() =>
    labelledParams(
      this.info()?.params.filter(
        p =>
          p.id !== 'type' &&
          !(this.block()?.type === 'pitch' && isCustomTableParam(p.id)) &&
          !(this.block()?.type === 'cab' && isCabSlotParam(p.id))
      ) ?? [],
      this.typeValue()
    )
  );
  /** The block's own controls, and its output stage (level, mix, balance, pan) set apart on the right. */
  protected readonly mainParams = computed(() => this.otherParams().filter(p => !OutputParams.has(p.id)));
  protected readonly outputParams = computed(() => this.otherParams().filter(p => OutputParams.has(p.id)));
  protected readonly category = computed(() => this.info()?.category ?? '');
  protected readonly accent = computed(() => blockAccent(this.category()));
}
