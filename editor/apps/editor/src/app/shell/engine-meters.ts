import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CthMeter, onAnimationFrame } from '@cathode/audio-ui';
import { XuiStatusImports } from '@xui/status';
import { XuiTooltipImports } from '@xui/tooltip';
import { EventStream } from '../core/events';
import { CatalogStore } from '../state/catalog.store';
import { LiveStore } from '../state/live.store';

/**
 * Input and output meters, CPU and the connection light, all fed straight from telemetry frames, plus the preset's
 * estimated DSP load from the catalog's measured block costs.
 */
@Component({
    selector: 'cth-engine-meters',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [CthMeter, XuiStatusImports, XuiTooltipImports],
    host: { class: 'flex h-8 items-center gap-3' },
    template: `
        <cth-meter class="h-8" label="Input level" [sources]="input" />
        <cth-meter class="h-8" label="Output level" [sources]="output" />
        <span
            class="text-foreground-muted w-20 text-xs whitespace-nowrap tabular-nums"
            [xuiTooltip]="'Engine CPU, 99.9th percentile of callbacks'"
        >
            CPU {{ cpu() }} %
        </span>
        <span
            class="text-foreground-muted text-xs whitespace-nowrap tabular-nums"
            data-testid="cpu-estimate"
            [xuiTooltip]="'Estimated DSP load of this preset, from its blocks’ measured costs at 48 kHz'"
        >
            Est. {{ estimate() }} %
        </span>
        <span class="flex items-center gap-1.5 text-xs" [xuiTooltip]="statusText()">
            <xui-status [presence]="presence()" size="sm" />
            <span class="sr-only">{{ statusText() }}</span>
        </span>
    `
})
export class EngineMeters {
    private readonly events = inject(EventStream);
    private readonly live = inject(LiveStore);
    private readonly catalog = inject(CatalogStore);

    protected readonly input = [() => this.events.latest?.inputPeak ?? 0];
    protected readonly output = [
        () => this.events.latest?.outputPeakLeft ?? 0,
        () => this.events.latest?.outputPeakRight ?? 0
    ];
    protected readonly cpu = signal(0);
    protected readonly estimate = computed(() => {
        const total = (this.live.preset()?.blocks ?? []).reduce(
            (sum, block) => sum + (this.catalog.info(block.type)?.cpuCost ?? 0),
            0
        );
        return total < 10 ? total.toFixed(1) : Math.round(total).toString();
    });
    protected readonly presence = computed(() =>
        this.events.status() === 'open' ? 'online' : this.events.status() === 'connecting' ? 'idle' : 'dnd'
    );
    protected readonly statusText = computed(() =>
        this.events.status() === 'open'
            ? 'Connected to the engine'
            : this.events.status() === 'connecting'
              ? 'Connecting…'
              : 'Disconnected: retrying'
    );

    constructor() {
        onAnimationFrame(() => {
            const cpu = Math.round(this.events.latest?.cpuP999 ?? 0);
            if (cpu !== this.cpu()) {
                this.cpu.set(cpu);
            }
        });
    }
}
