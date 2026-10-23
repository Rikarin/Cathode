import { DestroyRef, afterNextRender, inject } from '@angular/core';

/**
 * Calls `frame` on every animation frame from first render until the component is destroyed. Visualisations use it
 * to read telemetry directly, so 30-60 Hz updates never go through change detection.
 */
export function onAnimationFrame(frame: (time: number) => void): void {
    let handle = 0;
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
        const loop = (time: number) => {
            frame(time);
            handle = requestAnimationFrame(loop);
        };
        handle = requestAnimationFrame(loop);
    });
    destroyRef.onDestroy(() => cancelAnimationFrame(handle));
}

/** Resolves a theme token (e.g. `--color-success`) to a colour canvas can use. */
export function themeColor(element: Element, token: string): string {
    return getComputedStyle(element).getPropertyValue(token).trim() || 'currentColor';
}
