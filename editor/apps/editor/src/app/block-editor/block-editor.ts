import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { BuiltInTypes, type ChannelName, ChannelNames } from '@cathode/api-client';
import { BypassModes } from '@cathode/api-client';
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
import { XuiSelectImports } from '@xui/select';
import { XuiTagImports } from '@xui/tag';
import { GlobalStore } from '../state/global.store';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { XuiSwitchImports } from '@xui/switch';
import { XuiTextImports } from '@xui/text';
import { CatalogStore } from '../state/catalog.store';
import { LiveStore } from '../state/live.store';
import { AmpPanel } from './panels/amp-panel';
import { CabPanel } from './panels/cab-panel';
import { isCustomTableParam } from './panels/custom-shifter';
import { IrPlayerPanel } from './panels/ir-player-panel';
import { PeqPanel } from './panels/peq-panel';
import { PitchPanel } from './panels/pitch-panel';
import { ToneMatchPanel } from './panels/tone-match-panel';
import { ParamControl } from './param-control';

/**
 * The selected block's page: bypass, channel A-D (which channel the block uses in the active scene, as on the
 * Axe-Fx), the Type selector first, a custom panel where the block has one, then every other parameter.
 */
@Component({
    selector: 'cth-block-editor',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [
        AmpPanel,
        CabPanel,
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
    providers: [
        provideIcons({ matContentCopyRound, matLinkOffRound, matLinkRound, matMoreHorizRound, matPublicRound })
    ],
    host: { class: 'block h-full overflow-auto p-4' },
    template: `
        @if (block(); as block) {
            <section class="flex flex-col gap-4" [attr.aria-label]="title() + ' settings'">
                <header class="flex flex-wrap items-center gap-4">
                    <div class="flex flex-col">
                        <h2 xuiHeading class="text-base">{{ title() }}</h2>
                        <span class="text-foreground-muted text-xs">{{ block.id }}</span>
                    </div>
                    <label class="flex items-center gap-2 text-sm">
                        <xui-switch
                            data-testid="engage"
                            aria-label="Engaged"
                            [checked]="!live.isBypassed(block.id)"
                            (checkedChange)="live.setBypass(block.id, !$event)"
                        />
                        {{ live.isBypassed(block.id) ? 'Bypassed' : 'Engaged' }}
                    </label>
                    @if ((info()?.channels ?? 0) > 1) {
                        <xui-segmented-control
                            size="sm"
                            aria-label="Channel"
                            [options]="channels"
                            [value]="live.channelOf(block.id)"
                            (valueChange)="$event && live.setChannel(block.id, $event)"
                        />
                    }

                    <label class="flex items-center gap-2 text-sm">
                        <span class="text-foreground-muted text-xs">Bypass</span>
                        <xui-select
                            class="w-36"
                            aria-label="Bypass mode"
                            [filterable]="false"
                            [items]="bypassModes"
                            [itemText]="bypassModeName"
                            [value]="block.bypassMode"
                            (valueChange)="$event && live.setBypassMode(block.id, $event)"
                        />
                    </label>
                    @if (globalBlock(); as shared) {
                        <xui-tag minimal color="primary" icon="matPublicRound">Global: {{ shared.name }}</xui-tag>
                    }
                    <button
                        xuiButton
                        variant="ghost"
                        size="sm"
                        aria-label="Block actions"
                        [xuiMenuTriggerFor]="blockMenu"
                    >
                        <ng-icon xui name="matMoreHorizRound" size="sm" />
                    </button>
                    <ng-template #blockMenu>
                        <xui-menu>
                            @if ((info()?.channels ?? 0) > 1) {
                                <xui-menu-divider [title]="'Copy channel ' + live.channelOf(block.id) + ' to'" />
                                @for (channel of channels; track channel.value) {
                                    @if (channel.value !== live.channelOf(block.id)) {
                                        <button
                                            xuiMenuItem
                                            icon="matContentCopyRound"
                                            (click)="
                                                live.copyChannel(block.id, live.channelOf(block.id), channel.value)
                                            "
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
                                    <button
                                        xuiMenuItem
                                        icon="matLinkRound"
                                        (click)="live.useGlobal(block.id, shared.id)"
                                    >
                                        Use “{{ shared.name }}”
                                    </button>
                                }
                            }
                        </xui-menu>
                    </ng-template>
                </header>

                @if (typeParam(); as type) {
                    <div class="flex flex-wrap gap-6">
                        <cth-param-control [block]="block" [param]="type" />
                    </div>
                }

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

                <div class="flex flex-wrap items-start gap-x-6 gap-y-4">
                    @for (param of otherParams(); track param.id) {
                        <cth-param-control [block]="block" [param]="param" />
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
    protected readonly otherParams = computed(
        () =>
            this.info()?.params.filter(
                p => p.id !== 'type' && !(this.block()?.type === 'pitch' && isCustomTableParam(p.id))
            ) ?? []
    );
}
