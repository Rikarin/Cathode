import { ChangeDetectionStrategy, Component, ElementRef, input, viewChild } from '@angular/core';
import { onAnimationFrame, themeColor } from './animation';

/** A window of samples at a known rate, oldest first (ScopeFrame in Cathode.Contracts). */
export interface ScopeTrace {
    sampleRate: number;
    samples: ArrayLike<number>;
}

/**
 * An oscilloscope (docs/02-architecture.md §5.9). `source` is read on animation frames. The trace starts at the
 * first rising zero crossing in the first half of the window, so a steady note stands still; with no crossing it
 * free-runs. Vertical scale is ±1 (0 dBFS); the grid marks ±0.5 and 5 ms steps.
 */
@Component({
    selector: 'cth-scope',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'block' },
    template: `<canvas #canvas class="size-full" role="img" [attr.aria-label]="label()"></canvas>`
})
export class CthScope {
    private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');

    readonly source = input.required<() => ScopeTrace | null | undefined>();
    readonly label = input('Output waveform');

    constructor() {
        onAnimationFrame(() => this.draw());
    }

    private draw(): void {
        const canvas = this.canvas().nativeElement;
        const ratio = window.devicePixelRatio || 1;
        const width = canvas.clientWidth;
        const height = canvas.clientHeight;
        if (canvas.width !== width * ratio || canvas.height !== height * ratio) {
            canvas.width = width * ratio;
            canvas.height = height * ratio;
        }

        const context = canvas.getContext('2d');
        if (!context || width === 0 || height === 0) {
            return;
        }

        context.setTransform(ratio, 0, 0, ratio, 0, 0);
        context.clearRect(0, 0, width, height);
        const trace = this.source()();
        const grid = themeColor(canvas, '--color-border-muted');
        context.strokeStyle = grid;
        context.lineWidth = 1;
        for (const level of [-0.5, 0, 0.5]) {
            const y = Math.round(height / 2 - (level * height) / 2) + 0.5;
            context.beginPath();
            context.moveTo(0, y);
            context.lineTo(width, y);
            context.stroke();
        }

        if (!trace || trace.samples.length < 4) {
            return;
        }

        const samples = trace.samples;
        const shown = Math.floor(samples.length / 2);
        const start = trigger(samples, samples.length - shown);
        const step = width / (shown - 1);
        for (let ms = 5; ms < (shown / trace.sampleRate) * 1000; ms += 5) {
            const x = Math.round((ms / 1000) * trace.sampleRate * step) + 0.5;
            context.beginPath();
            context.moveTo(x, 0);
            context.lineTo(x, height);
            context.stroke();
        }

        context.strokeStyle = themeColor(canvas, '--color-primary');
        context.lineWidth = 1.5;
        context.beginPath();
        for (let i = 0; i < shown; i++) {
            const value = Math.max(-1, Math.min(1, samples[start + i]));
            const x = i * step;
            const y = height / 2 - (value * height) / 2;
            if (i === 0) {
                context.moveTo(x, y);
            } else {
                context.lineTo(x, y);
            }
        }

        context.stroke();
    }
}

/** Index of the first rising zero crossing at or before `latest`, or `latest` (free-running) if there is none. */
export function trigger(samples: ArrayLike<number>, latest: number): number {
    for (let i = 1; i <= latest; i++) {
        if (samples[i - 1] < 0 && samples[i] >= 0) {
            return i;
        }
    }

    return latest;
}
