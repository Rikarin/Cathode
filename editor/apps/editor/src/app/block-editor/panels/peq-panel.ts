import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, input, signal } from '@angular/core';
import type { PresetBlock } from '@cathode/api-client';
import { XuiKonvaImports, type XuiKonvaEventObject } from '@xui/konva';
import { logFrequencies, sectionDb, type FilterShape } from '../../dsp/response';
import { LiveStore } from '../../state/live.store';

const Width = 640;
const Height = 220;
const MaxDb = 24;
const Bands = [1, 2, 3, 4, 5] as const;
const Frequencies = logFrequencies(240);

const xOf = (frequency: number) => (Math.log(frequency / 20) / Math.log(1000)) * Width;
const frequencyOf = (x: number) => 20 * Math.pow(1000, Math.min(Width, Math.max(0, x)) / Width);
const yOf = (db: number) => Height / 2 - (db / MaxDb) * (Height / 2);
const dbOf = (y: number) => Math.max(-MaxDb, Math.min(MaxDb, ((Height / 2 - y) / (Height / 2)) * MaxDb));

/**
 * The parametric EQ as a curve (docs/02-architecture.md §7.3, via @xui/konva). Drag a band's handle to set its
 * frequency and gain; scroll over it to change its Q. The knobs below show the same parameters.
 */
@Component({
    selector: 'cth-peq-panel',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [XuiKonvaImports],
    template: `
        <xui-konva-stage
            class="border-border bg-surface-sunken inline-block rounded-lg border"
            role="img"
            [attr.aria-label]="description()"
            [config]="stage"
        >
            <xui-konva-layer>
                @for (line of grid(); track $index) {
                    <xui-konva-line [config]="line" />
                }
                <xui-konva-line [config]="curve()" />
                @for (handle of handles(); track handle.band) {
                    <xui-konva-circle
                        [config]="handle.config"
                        (dragmove)="drag(handle.band, $event)"
                        (wheel)="wheel(handle.band, $event)"
                    />
                }
            </xui-konva-layer>
        </xui-konva-stage>
    `
})
export class PeqPanel {
    private readonly live = inject(LiveStore);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

    readonly block = input.required<PresetBlock>();

    protected readonly stage = { width: Width, height: Height };
    private readonly colors = signal(this.readColors());

    protected readonly bands = computed(() => {
        const block = this.block();
        const channel = this.live.channelOf(block.id);
        const value = (id: string, fallback: number) => this.live.valueOf(block, channel, id) ?? fallback;
        const defaults = [80, 400, 1_000, 3_000, 8_000];
        return Bands.map(band => {
            const shape: FilterShape =
                band === 1 && value('lowtype', 0) === 0
                    ? 'lowshelf'
                    : band === 5 && value('hightype', 0) === 0
                      ? 'highshelf'
                      : 'peak';
            return {
                band,
                shape,
                frequency: value(`freq${band}`, defaults[band - 1]),
                gain: value(`gain${band}`, 0),
                q: value(`q${band}`, 0.7071)
            };
        });
    });

    protected readonly curve = computed(() => {
        const bands = this.bands();
        const points = Frequencies.flatMap(f => {
            const db = bands.reduce((sum, b) => sum + sectionDb(b.shape, b.frequency, b.gain, b.q, f, 48_000), 0);
            return [xOf(f), yOf(Math.max(-MaxDb, Math.min(MaxDb, db)))];
        });
        return { points, stroke: this.colors().curve, strokeWidth: 2, lineJoin: 'round' as const, listening: false };
    });

    protected readonly handles = computed(() =>
        this.bands().map(b => ({
            band: b.band,
            config: {
                x: xOf(b.frequency),
                y: yOf(b.gain),
                radius: 8,
                fill: this.colors().handle,
                stroke: this.colors().curve,
                strokeWidth: 2,
                draggable: true
            }
        }))
    );

    protected readonly grid = computed(() => {
        const color = this.colors().grid;
        const vertical = [100, 1_000, 10_000].map(f => ({
            points: [xOf(f), 0, xOf(f), Height],
            stroke: color,
            strokeWidth: 1,
            listening: false
        }));
        const horizontal = [-12, 0, 12].map(db => ({
            points: [0, yOf(db), Width, yOf(db)],
            stroke: color,
            strokeWidth: db === 0 ? 1.5 : 1,
            listening: false
        }));
        return [...vertical, ...horizontal];
    });

    protected readonly description = computed(
        () =>
            'Parametric EQ curve: ' +
            this.bands()
                .map(b => `band ${b.band} ${Math.round(b.frequency)} Hz ${b.gain.toFixed(1)} dB`)
                .join(', ')
    );

    protected drag(band: number, event: XuiKonvaEventObject<MouseEvent>): void {
        const node = event.event.target;
        const block = this.block();
        const channel = this.live.channelOf(block.id);
        this.live.setParam(block.id, channel, `freq${band}`, frequencyOf(node.x()));
        this.live.setParam(block.id, channel, `gain${band}`, dbOf(node.y()));
    }

    protected wheel(band: number, event: XuiKonvaEventObject<WheelEvent>): void {
        event.event.evt.preventDefault();
        const block = this.block();
        const current = this.bands()[band - 1].q;
        const next = Math.max(0.1, Math.min(10, current * (event.event.evt.deltaY < 0 ? 1.1 : 1 / 1.1)));
        this.live.setParam(block.id, this.live.channelOf(block.id), `q${band}`, next);
    }

    private readColors() {
        const style = getComputedStyle(this.host.nativeElement.ownerDocument.documentElement);
        const token = (name: string) => style.getPropertyValue(name).trim() || '#888';
        return {
            curve: token('--color-primary'),
            handle: token('--color-surface'),
            grid: token('--color-border-muted')
        };
    }
}
