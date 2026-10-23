import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { BuiltInTypes, BypassModes, type ChannelName, ChannelNames } from '@cathode/api-client';
import { CthBlockTile, blockAccent } from '@cathode/audio-ui';
import { provideIcons } from '@ng-icons/core';
import {
  matContentCopyRound,
  matLinkOffRound,
  matLinkRound,
  matMoreHorizRound,
  matPublicRound
} from '@ng-icons/material-icons/round';
import { XuiButtonImports } from '@xui/button';
import { XuiIconImports } from '@xui/icon';
import { XuiMenuImports } from '@xui/menu';
import { XuiNonIdealStateImports } from '@xui/non-ideal-state';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { XuiSelectImports } from '@xui/select';
import { XuiSwitchImports } from '@xui/switch';
import { XuiTagImports } from '@xui/tag';
import { XuiTextImports } from '@xui/text';
import { CatalogStore } from '../state/catalog.store';
import { GlobalStore } from '../state/global.store';
import { LiveStore } from '../state/live.store';
import { AmpPanel } from './panels/amp-panel';
import { CabPanel } from './panels/cab-panel';
import { isCustomTableParam } from './panels/custom-shifter';
import { isDynaCabSlotParam } from './panels/dynacab';
import { DynaCabPanel } from './panels/dynacab-panel';
import { IrPlayerPanel } from './panels/ir-player-panel';
import { PeqPanel } from './panels/peq-panel';
import { PitchPanel } from './panels/pitch-panel';
import { ToneMatchPanel } from './panels/tone-match-panel';
import { ParamControl } from './param-control';

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
    DynaCabPanel,
    IrPlayerPanel,
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
  providers: [provideIcons({ matContentCopyRound, matLinkOffRound, matLinkRound, matMoreHorizRound, matPublicRound })],
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

          <label
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
            </div>
          }

          @if (typeParam(); as type) {
            <cth-param-control variant="sidebar" [block]="block" [param]="type" />
          }

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

          <ng-template #blockMenu>
            <xui-menu>
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
                <xui-menu-divider />
              }
              @if (block.global) {
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

        <div class="flex min-w-0 flex-1 flex-col gap-4">
          @switch (block.type) {
            @case ('nam') {
              <cth-amp-panel [block]="block" />
            }
            @case ('cab') {
              <cth-cab-panel [block]="block" />
            }
            @case ('dynacab') {
              <cth-dynacab-panel [block]="block" />
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
  protected readonly bypassModes = [...BypassModes];
  protected readonly bypassModeName = (mode: string) =>
    ({ thru: 'Thru', mute: 'Mute', muteIn: 'Mute In', muteFxIn: 'Mute FX In (spillover)' })[mode] ?? mode;
  protected readonly globalBlock = computed(() => {
    const id = this.block()?.global;
    return id ? this.globals.settings()?.blocks.find(b => b.id === id) : undefined;
  });
  protected readonly sameTypeGlobals = computed(() =>
    (this.globals.settings()?.blocks ?? []).filter(b => b.type === this.block()?.type)
  );
  protected readonly block = computed(() => {
    const block = this.live.selected();
    return block && !BuiltInTypes[block.type as keyof typeof BuiltInTypes] ? block : null;
  });
  protected readonly info = computed(() => (this.block() ? this.catalog.info(this.block()!.type) : undefined));
  protected readonly title = computed(() => this.info()?.name ?? this.block()?.type ?? '');
  protected readonly typeParam = computed(() => this.info()?.params.find(p => p.id === 'type') ?? null);
  private readonly otherParams = computed(
    () =>
      this.info()?.params.filter(
        p =>
          p.id !== 'type' &&
          !(this.block()?.type === 'pitch' && isCustomTableParam(p.id)) &&
          !(this.block()?.type === 'dynacab' && isDynaCabSlotParam(p.id))
      ) ?? []
  );
  /** The block's own controls, and its output stage (level, mix, balance, pan) set apart on the right. */
  protected readonly mainParams = computed(() => this.otherParams().filter(p => !OutputParams.has(p.id)));
  protected readonly outputParams = computed(() => this.otherParams().filter(p => OutputParams.has(p.id)));
  protected readonly category = computed(() => this.info()?.category ?? '');
  protected readonly accent = computed(() => blockAccent(this.category()));
}
