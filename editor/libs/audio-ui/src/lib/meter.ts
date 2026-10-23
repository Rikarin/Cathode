import { ChangeDetectionStrategy, Component, ElementRef, input, signal, viewChild } from '@angular/core';
import { onAnimationFrame, themeColor } from './animation';
import { toDecibels } from './values';

const FloorDb = -60;
const CeilingDb = 6;
const FallDbPerSecond = 24;
const HoldMs = 1_500;
const Ticks = [0, -6, -12, -24, -48];

interface ChannelState {
    level: number;
    peak: number;
    peakAt: number;
}

/**
 * A peak meter for one or more channels (docs/02-architecture.md §5.9). `sources` are read on every animation
 * frame, never through change detection: each returns the linear peak since the previous read (the Server already
 * keeps the maximum per telemetry frame). Instant attack, 24 dB/s fall, 1.5 s peak hold, and a clip light that
 * stays on until clicked.
 */
@Component({
    selector: 'cth-meter',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'inline-flex items-stretch gap-1' },
    template: `
        <canvas
            #canvas
            class="h-full"
            role="meter"
            [attr.aria-label]="label()"
            [attr.aria-valuemin]="floor"
            [attr.aria-valuemax]="ceiling"
            [attr.aria-valuenow]="announced()"
            [style.width.px]="sources().length * 8 + (sources().length - 1) * 2"
        ></canvas>
        <button
            type="button"
            class="size-2 self-start rounded-full"
            [class]="clipped() ? 'bg-error' : 'bg-surface-sunken'"
            [attr.aria-label]="clipped() ? label() + ' clipped; click to reset' : label() + ' clip indicator'"
            (click)="clipped.set(false)"
        ></button>
    `
})
export class CthMeter {
    private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
    private states: ChannelState[] = [];
    private last = 0;

    readonly sources = input.required<ReadonlyArray<() => number>>();
    readonly label = input('Level');

    protected readonly floor = FloorDb;
    protected readonly ceiling = CeilingDb;
    protected readonly clipped = signal(false);
    /** Whole-dB level for assistive technology, updated only when it changes. */
    protected readonly announced = signal(FloorDb);

    constructor() {
        onAnimationFrame(time => this.draw(time));
    }

    private draw(time: number): void {
        const canvas = this.canvas().nativeElement;
        const sources = this.sources();
        const elapsed = this.last ? (time - this.last) / 1000 : 0;
        this.last = time;
        if (this.states.length !== sources.length) {
            this.states = sources.map(() => ({ level: FloorDb, peak: FloorDb, peakAt: 0 }));
        }

        sources.forEach((source, i) => {
            const db = toDecibels(source(), FloorDb);
            const state = this.states[i];
            state.level = Math.max(db, state.level - FallDbPerSecond * elapsed);
            if (db >= state.peak || time - state.peakAt > HoldMs) {
                state.peak = db;
                state.peakAt = time;
            }

            if (db >= 0) {
                this.clipped.set(true);
            }
        });

        const loudest = Math.round(Math.max(...this.states.map(s => s.level)));
        if (loudest !== this.announced()) {
            this.announced.set(loudest);
        }

        const ratio = window.devicePixelRatio || 1;
        const width = canvas.clientWidth;
        const height = canvas.clientHeight;
        if (canvas.width !== width * ratio || canvas.height !== height * ratio) {
            canvas.width = width * ratio;
            canvas.height = height * ratio;
        }

        const context = canvas.getContext('2d');
        if (!context || height === 0) {
            return;
        }

        context.setTransform(ratio, 0, 0, ratio, 0, 0);
        context.clearRect(0, 0, width, height);
        const y = (db: number) => height * (1 - (db - FloorDb) / (CeilingDb - FloorDb));
        const colors = {
            track: themeColor(canvas, '--color-surface-sunken'),
            ok: themeColor(canvas, '--color-success'),
            hot: themeColor(canvas, '--color-warning'),
            clip: themeColor(canvas, '--color-error'),
            tick: themeColor(canvas, '--color-border')
        };

        this.states.forEach((state, i) => {
            const x = i * 10;
            context.fillStyle = colors.track;
            context.fillRect(x, 0, 8, height);
            for (const [from, to, color] of [
                [FloorDb, -12, colors.ok],
                [-12, 0, colors.hot],
                [0, CeilingDb, colors.clip]
            ] as const) {
                const top = Math.min(state.level, to);
                if (top > from) {
                    context.fillStyle = color;
                    context.fillRect(x, y(top), 8, y(from) - y(top));
                }
            }

            if (state.peak > FloorDb) {
                context.fillStyle = state.peak >= 0 ? colors.clip : state.peak >= -12 ? colors.hot : colors.ok;
                context.fillRect(x, y(state.peak) - 1, 8, 2);
            }
        });

        context.fillStyle = colors.tick;
        for (const tick of Ticks) {
            context.fillRect(0, Math.round(y(tick)), this.states.length * 10 - 2, 1);
        }
    }
}
