import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { BuiltInTypes, BypassModes, ChannelNames, OutputTypes, type ParameterInfo } from '@cathode/api-client';
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
import { PreferencesStore } from '../state/preferences.store';
import { UiStore } from '../state/ui.store';
import { AmpPanel } from './panels/amp-panel';
import { isCabSlotParam } from './panels/cab';
import { CabPanel } from './panels/cab-panel';
import { CabResonanceHelper } from './panels/cab-resonance-helper';
import { isCustomTableParam } from './panels/custom-shifter';
import { IrPlayerPanel } from './panels/ir-player-panel';
import { PeqPanel } from './panels/peq-panel';
import { PitchPanel } from './panels/pitch-panel';
import { ToneMatchPanel } from './panels/tone-match-panel';
import { ParamControl } from './param-control';
import { isShown, labelledParams } from './type-labels';

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
    CabResonanceHelper,
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
  host: { class: 'flex h-full flex-col p-4' },
  template: `
    @if (block(); as block) {
      <!-- The block's own column and its controls scroll apart, so a long left column never moves the knobs. -->
      <section class="flex min-h-0 flex-1 gap-5" [attr.aria-label]="title() + ' settings'">
        <aside class="-me-2 flex min-h-0 w-58 shrink-0 flex-col gap-3 overflow-y-auto pe-2 *:shrink-0">
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
            <button
              type="button"
              xuiButton
              variant="ghost"
              size="sm"
              aria-label="Block actions"
              [xuiMenuTriggerFor]="blockMenu"
            >
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
                <button type="button" xuiMenuItem icon="matTuneRound" (click)="editingEngage.set(true)">
                  {{ engageModifier() ? 'Edit engage modifier…' : 'Engage from a modifier…' }}
                </button>
                @if (engageModifier()) {
                  <button
                    type="button"
                    xuiMenuItem
                    icon="matLinkOffRound"
                    color="error"
                    (click)="removeEngageModifier()"
                  >
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

          @if (block.type === 'circuitamp') {
            <div class="flex flex-col gap-1.5">
              <span class="text-foreground-muted text-[11px] font-semibold tracking-wide uppercase" id="amp-view-label">
                Controls
              </span>
              <xui-segmented-control
                size="sm"
                fill
                aria-labelledby="amp-view-label"
                data-testid="amp-view"
                [options]="ampViews"
                [value]="ampView()"
                (valueChange)="$event && ampViewOverride.set($event)"
              />
            </div>
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
                placeholder="Recall an entry…"
                [items]="libraryIds()"
                [itemText]="libraryName"
                [value]="null"
                (valueChange)="$event && blockPresets.apply(block.id, $event, { all: recallAll() })"
              />
              @if ((info()?.channels ?? 0) > 1) {
                <label class="text-foreground-muted flex items-center justify-between text-xs">
                  Into all channels
                  <xui-switch
                    aria-label="Recall into all channels"
                    [checked]="recallAll()"
                    (checkedChange)="recallAll.set($event)"
                  />
                </label>
              }
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
              <button type="button" xuiMenuItem icon="matSaveRound" (click)="ui.saveBlockOpen.set(true)">
                Save to the Block Library…
              </button>
              <xui-menu-divider />
              @if ((info()?.channels ?? 0) > 1) {
                <xui-menu-divider [title]="'Copy channel ' + live.channelOf(block.id) + ' to'" />
                @for (channel of channels; track channel.value) {
                  @if (channel.value !== live.channelOf(block.id)) {
                    <button
                      type="button"
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
                      type="button"
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
                  type="button"
                  xuiMenuItem
                  icon="matRestartAltRound"
                  (click)="live.resetChannel(block.id, live.channelOf(block.id))"
                >
                  Reset channel {{ live.channelOf(block.id) }}
                </button>
              }
              <button
                type="button"
                xuiMenuItem
                icon="matRestartAltRound"
                color="error"
                (click)="live.resetBlock(block.id)"
              >
                Reset block (all channels)
              </button>
              <xui-menu-divider />
              @if (isIo()) {
                <!-- The grid's Input and Output cannot be global. -->
              } @else if (block.global) {
                <button type="button" xuiMenuItem icon="matLinkOffRound" (click)="live.unlinkGlobal(block.id)">
                  Unlink from global
                </button>
              } @else {
                <button
                  type="button"
                  xuiMenuItem
                  icon="matPublicRound"
                  (click)="live.makeGlobal(block.id, title() + ' (' + block.id + ')')"
                >
                  Make global
                </button>
                @for (shared of sameTypeGlobals(); track shared.id) {
                  <button type="button" xuiMenuItem icon="matLinkRound" (click)="live.useGlobal(block.id, shared.id)">
                    Use “{{ shared.name }}”
                  </button>
                }
              }
            </xui-menu>
          </ng-template>
        </aside>

        <div class="flex min-h-0 min-w-0 flex-1 flex-col gap-4 overflow-y-auto">
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
              @if (pages().length > 1) {
                <nav
                  role="tablist"
                  aria-label="Block pages"
                  aria-orientation="vertical"
                  class="border-border -my-1 flex w-32 shrink-0 flex-col gap-0.5 border-e pe-3"
                  data-testid="block-pages"
                >
                  @for (name of pages(); track name) {
                    <button
                      type="button"
                      role="tab"
                      class="rounded-md px-2.5 py-1.5 text-start text-sm transition-colors"
                      [class.bg-active-overlay]="name === page()"
                      [class.font-medium]="name === page()"
                      [class.text-foreground-muted]="name !== page()"
                      [class.hover:bg-hover-overlay]="name !== page()"
                      [class.hover:text-foreground]="name !== page()"
                      [style.color]="name === page() ? accent() : null"
                      [attr.aria-selected]="name === page()"
                      (click)="showPage(name)"
                    >
                      {{ name }}
                    </button>
                  }
                </nav>
              }
              @if (mainParams().length > 0) {
                <div
                  role="tabpanel"
                  class="grid min-w-0 flex-1 grid-cols-[repeat(auto-fill,minmax(6.5rem,1fr))] content-start justify-items-center gap-x-3 gap-y-5"
                  [attr.aria-label]="page()"
                >
                  @for (param of pageParams(); track param.id) {
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

          <!-- Below the controls: an occasional tool, not part of the amp's panel. -->
          @if (block.type === 'circuitamp') {
            <cth-cab-resonance-helper [block]="block" />
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

  protected readonly channels = ChannelNames.map(c => ({ label: c, value: c }));
  /** The pseudo-parameter a modifier drives to engage the block: engaged at 0.5 and above (ModNames.EngageParam). */
  protected readonly engageParam: ParameterInfo = {
    id: 'engage',
    name: 'Engage',
    kind: 'float',
    min: 0,
    max: 1,
    default: 1,
    unit: '',
    taper: 'linear',
    visibleWhen: null
  };
  protected readonly editingEngage = signal(false);
  protected readonly blockPresets = inject(BlockPresetsStore);
  protected readonly ui = inject(UiStore);
  private readonly preferences = inject(PreferencesStore);
  /** Recall Library entries into every channel rather than the current one. */
  protected readonly recallAll = signal(false);
  /** The Circuit Amp's Ideal or Authentic view on this page; the preference until changed here. */
  protected readonly ampViewOverride = signal<'authentic' | 'ideal' | null>(null);
  protected readonly ampView = computed(() => this.ampViewOverride() ?? this.preferences.ampView());
  protected readonly ampViews = [
    { label: 'Authentic', value: 'authentic' as const },
    { label: 'Ideal', value: 'ideal' as const }
  ];
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
    () => this.block()?.type === BuiltInTypes.input || OutputTypes.includes(this.block()?.type ?? '')
  );
  constructor() {
    void this.blockPresets.refresh();
  }

  protected removeEngageModifier(): void {
    const block = this.block();
    if (block) {
      void this.live.putModifier(block.id, 'engage', null);
    }
  }

  protected readonly info = computed(() => {
    const block = this.block();
    return block ? this.catalog.info(block.type) : undefined;
  });
  protected readonly title = computed(() => this.info()?.name ?? this.block()?.type ?? '');
  protected readonly typeParam = computed(() => this.info()?.params.find(p => p.id === 'type') ?? null);
  /** The selected type (model) of the block, which names and hides per-model controls. */
  private readonly typeValue = computed(() => {
    const block = this.block();
    const type = this.typeParam();
    return block && type ? (this.live.valueOf(block, this.live.channelOf(block.id), 'type') ?? type.default) : 0;
  });
  /** The block's controls but the type: named and hidden by the type, and hidden by another control's setting. */
  private readonly otherParams = computed(() => {
    const block = this.block();
    const params = this.info()?.params ?? [];
    const valueOf = (id: string) => (block ? this.live.valueOf(block, this.live.channelOf(block.id), id) : undefined);
    return labelledParams(
      params.filter(
        p =>
          p.id !== 'type' &&
          !(block?.type === 'pitch' && isCustomTableParam(p.id)) &&
          !(block?.type === 'cab' && isCabSlotParam(p.id)) &&
          isShown(params, p, valueOf)
      ),
      this.typeValue(),
      block?.type === 'circuitamp' && this.ampView() === 'ideal'
    );
  });
  /** The block's own controls, and its output stage (level, mix, balance, pan) set apart on the right. */
  protected readonly mainParams = computed(() => this.otherParams().filter(p => !OutputParams.has(p.id)));
  protected readonly outputParams = computed(() => this.otherParams().filter(p => OutputParams.has(p.id)));
  /** The block's pages that have a control to show (a page may hold only output or hidden controls). */
  protected readonly pages = computed(() => {
    const declared = this.info()?.pages ?? [];
    return declared.filter(name => this.mainParams().some(p => (p.page ?? declared[0]) === name));
  });
  /** The page showing: the one last chosen for this block type, else the first. */
  protected readonly page = computed(() => {
    const pages = this.pages();
    const chosen = this.ui.blockPage()[this.block()?.type ?? ''];
    return chosen && pages.includes(chosen) ? chosen : (pages[0] ?? null);
  });
  /** The controls on the page showing; all of them for a block with one page. */
  protected readonly pageParams = computed(() => {
    const page = this.page();
    const first = this.info()?.pages?.[0];
    return this.pages().length > 1 ? this.mainParams().filter(p => (p.page ?? first) === page) : this.mainParams();
  });
  protected readonly category = computed(() => this.info()?.category ?? '');
  protected readonly accent = computed(() => blockAccent(this.category()));

  protected showPage(name: string): void {
    const type = this.block()?.type;
    if (type) {
      this.ui.blockPage.update(pages => ({ ...pages, [type]: name }));
    }
  }
}
