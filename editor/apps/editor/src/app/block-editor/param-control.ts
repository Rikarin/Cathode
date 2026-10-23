import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import type { ParameterInfo, PresetBlock } from '@cathode/api-client';
import { CthKnob } from '@cathode/audio-ui';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { XuiSelectImports } from '@xui/select';
import { XuiSwitchImports } from '@xui/switch';
import { LiveStore } from '../state/live.store';

/**
 * One parameter of the selected block, generated from its descriptor: a knob for continuous values, a segmented
 * control or select for choices, a switch for on/off. Edits go to the channel the block uses in the active scene.
 */
@Component({
    selector: 'cth-param-control',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [CthKnob, XuiSegmentedControlImports, XuiSelectImports, XuiSwitchImports],
    host: { class: 'contents' },
    template: `
        @switch (param().kind) {
            @case ('float') {
                <cth-knob
                    [attr.data-testid]="'param-' + param().id"
                    [label]="param().name"
                    [unit]="param().unit"
                    [min]="param().min"
                    [max]="param().max"
                    [defaultValue]="param().default"
                    [taper]="param().taper === 'log' ? 'log' : 'linear'"
                    [value]="value()"
                    (valueChange)="set($event)"
                />
            }
            @case ('enum') {
                <div class="flex min-w-36 flex-col gap-1">
                    <span class="text-foreground-muted text-xs" [id]="labelId()">{{ param().name }}</span>
                    @if (segmented()) {
                        <xui-segmented-control
                            size="sm"
                            [attr.aria-labelledby]="labelId()"
                            [options]="choices()"
                            [value]="value()"
                            (valueChange)="set($event ?? 0)"
                        />
                    } @else {
                        <xui-select
                            class="w-44"
                            [attr.aria-labelledby]="labelId()"
                            [filterable]="false"
                            [items]="indices()"
                            [itemText]="optionText"
                            [value]="value()"
                            (valueChange)="set($event ?? 0)"
                        />
                    }
                </div>
            }
            @case ('bool') {
                <label class="flex flex-col items-center gap-2">
                    <span class="text-foreground-muted text-xs">{{ param().name }}</span>
                    <xui-switch
                        [attr.aria-label]="param().name"
                        [checked]="value() >= 0.5"
                        (checkedChange)="set($event ? 1 : 0)"
                    />
                </label>
            }
        }
    `
})
export class ParamControl {
    private readonly live = inject(LiveStore);

    readonly block = input.required<PresetBlock>();
    readonly param = input.required<ParameterInfo>();

    protected readonly labelId = computed(() => `param-${this.block().id}-${this.param().id}`);
    protected readonly value = computed(() => {
        const block = this.block();
        return this.live.valueOf(block, this.live.channelOf(block.id), this.param().id) ?? this.param().default;
    });
    protected readonly options = computed(() => this.param().options ?? []);
    protected readonly indices = computed(() => this.options().map((_, i) => i));
    protected readonly choices = computed(() => this.options().map((label, value) => ({ label, value })));
    protected readonly segmented = computed(() => this.param().id === 'type' && this.options().length <= 5);
    protected readonly optionText = (index: number) => this.options()[index] ?? String(index);

    protected set(value: number): void {
        const block = this.block();
        this.live.setParam(block.id, this.live.channelOf(block.id), this.param().id, value);
    }
}
