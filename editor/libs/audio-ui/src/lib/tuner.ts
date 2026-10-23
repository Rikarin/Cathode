import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { onAnimationFrame } from './animation';

/** What the engine's tuner reports (TunerInfo in Cathode.Contracts). */
export interface TunerReading {
    frequency: number;
    note: string;
    octave: number;
    cents: number;
}

/** Within this many cents a string reads as in tune. */
const InTuneCents = 2;

/**
 * A needle tuner. `source` is polled on animation frames (telemetry bypasses change detection); the display only
 * updates when the rounded reading changes. The needle spans ±50 cents and turns green within ±2.
 */
@Component({
    selector: 'cth-tuner',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'flex flex-col items-center gap-3' },
    template: `
        <div class="flex items-baseline gap-1" aria-live="polite">
            <span class="text-6xl font-semibold tabular-nums" [class]="inTune() ? 'text-success' : 'text-foreground'">
                {{ reading()?.note ?? '–' }}
            </span>
            <span class="text-foreground-muted text-xl">{{ reading()?.octave ?? '' }}</span>
        </div>
        <svg viewBox="0 0 200 60" class="w-72" role="img" [attr.aria-label]="description()">
            @for (tick of ticks; track tick) {
                <line
                    [attr.x1]="100 + tick * 1.8"
                    [attr.x2]="100 + tick * 1.8"
                    y1="40"
                    [attr.y2]="tick % 25 === 0 ? 20 : 30"
                    class="stroke-border"
                    stroke-width="1.5"
                />
            }
            @if (reading(); as r) {
                <line
                    [attr.x1]="100 + clamp(r.cents) * 1.8"
                    [attr.x2]="100 + clamp(r.cents) * 1.8"
                    y1="8"
                    y2="52"
                    [class]="inTune() ? 'stroke-success' : 'stroke-warning'"
                    stroke-width="3"
                    stroke-linecap="round"
                />
            }
        </svg>
        <span class="text-foreground-muted text-sm tabular-nums">
            @if (reading(); as r) {
                {{ r.frequency.toFixed(1) }} Hz · {{ r.cents > 0 ? '+' : '' }}{{ r.cents.toFixed(1) }} cents
            } @else {
                Play a string
            }
        </span>
    `
})
export class CthTuner {
    readonly source = input.required<() => TunerReading | null | undefined>();

    protected readonly ticks = [-50, -40, -30, -20, -10, 0, 10, 20, 30, 40, 50];
    protected readonly reading = signal<TunerReading | null>(null);
    protected readonly inTune = computed(() => {
        const r = this.reading();
        return r !== null && Math.abs(r.cents) <= InTuneCents;
    });
    protected readonly description = computed(() => {
        const r = this.reading();
        return r ? `${r.note}${r.octave}, ${r.cents.toFixed(1)} cents` : 'No pitch detected';
    });

    constructor() {
        onAnimationFrame(() => {
            const next = this.source()() ?? null;
            const current = this.reading();
            if (
                next?.note !== current?.note ||
                next?.octave !== current?.octave ||
                Math.round((next?.cents ?? 0) * 10) !== Math.round((current?.cents ?? 0) * 10)
            ) {
                this.reading.set(next ? { ...next } : null);
            }
        });
    }

    protected clamp(cents: number): number {
        return Math.max(-50, Math.min(50, cents));
    }
}
