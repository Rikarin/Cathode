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

/** Bumped whenever the theme may have changed: a class, data-theme or style change on <html>, or the OS scheme. */
let themeVersion = 0;
let watchingTheme = false;

function watchTheme(): void {
  if (watchingTheme || typeof document === 'undefined') {
    return;
  }

  watchingTheme = true;
  const bump = () => themeVersion++;
  new MutationObserver(bump).observe(document.documentElement, {
    attributes: true,
    attributeFilter: ['class', 'data-theme', 'style']
  });
  if (typeof window.matchMedia === 'function') {
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', bump);
  }
}

/**
 * Makes a resolver for a set of theme tokens that calls `getComputedStyle` only when the theme may have changed,
 * not on every frame: a visualisation drawing at 60 Hz then costs one style resolution per theme switch. A set with
 * an unresolved token (the element not yet styled) is resolved again on the next call.
 */
export function themeColors<K extends string>(
  tokens: Readonly<Record<K, string>>
): (element: Element) => Readonly<Record<K, string>> {
  watchTheme();
  const keys = Object.keys(tokens) as K[];
  let cached: Record<K, string> | undefined;
  let version = -1;
  return element => {
    if (cached && version === themeVersion) {
      return cached;
    }

    const style = getComputedStyle(element);
    const resolved = {} as Record<K, string>;
    let complete = true;
    for (const key of keys) {
      const value = style.getPropertyValue(tokens[key]).trim();
      complete &&= value !== '';
      resolved[key] = value || 'currentColor';
    }

    cached = resolved;
    version = complete ? themeVersion : -1;
    return resolved;
  };
}
