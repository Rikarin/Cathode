import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { BuiltInTypes, type ChannelName, ChannelNames } from '@cathode/api-client';
import { XuiNonIdealStateImports } from '@xui/non-ideal-state';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { XuiSwitchImports } from '@xui/switch';
import { XuiTextImports } from '@xui/text';
import { CatalogStore } from '../state/catalog.store';
import { LiveStore } from '../state/live.store';
import { AmpPanel } from './panels/amp-panel';
import { CabPanel } from './panels/cab-panel';
import { PeqPanel } from './panels/peq-panel';
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
        ParamControl,
        PeqPanel,
        XuiNonIdealStateImports,
        XuiSegmentedControlImports,
        XuiSwitchImports,
        XuiTextImports
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

    protected readonly channels = ChannelNames.map(c => ({ label: c, value: c as ChannelName }));
    protected readonly block = computed(() => {
        const block = this.live.selected();
        return block && !BuiltInTypes[block.type as keyof typeof BuiltInTypes] ? block : null;
    });
    protected readonly info = computed(() => (this.block() ? this.catalog.info(this.block()!.type) : undefined));
    protected readonly title = computed(() => this.info()?.name ?? this.block()?.type ?? '');
    protected readonly typeParam = computed(() => this.info()?.params.find(p => p.id === 'type') ?? null);
    protected readonly otherParams = computed(() => this.info()?.params.filter(p => p.id !== 'type') ?? []);
}
