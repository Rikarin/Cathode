import type { FootswitchLayout, FootswitchOverride, SwitchAction } from '@cathode/api-client';

/** A layout's view names; a layout without views has one, "Main". */
export function viewNames(layout: FootswitchLayout): string[] {
  return layout.views && layout.views.length > 0 ? [...layout.views] : ['Main'];
}

/** The layout with one more view (page of switches) at the end. */
export function addView(layout: FootswitchLayout): FootswitchLayout {
  const views = viewNames(layout);
  return { ...layout, views: [...views, `View ${views.length + 1}`] };
}

/** The layout without view `view`: its switches go too, and later views move down one. */
export function removeView(layout: FootswitchLayout, view: number): FootswitchLayout {
  const views = viewNames(layout);
  if (views.length <= 1) {
    return layout;
  }

  return {
    ...layout,
    views: views.filter((_, i) => i !== view),
    switches: layout.switches
      .filter(s => s.view !== view)
      .map(s => ({ ...s, view: s.view > view ? s.view - 1 : s.view }))
  };
}

/** The preset's override for a switch, if it has one. */
export function overrideOf(
  overrides: readonly FootswitchOverride[],
  layout: string,
  index: number
): FootswitchOverride | undefined {
  return overrides.find(o => o.layout === layout && o.index === index);
}

/** The overrides with the switch's set to `tap` (null removes it). */
export function withOverride(
  overrides: readonly FootswitchOverride[],
  layout: string,
  index: number,
  tap: SwitchAction | null
): FootswitchOverride[] {
  const others = overrides.filter(o => !(o.layout === layout && o.index === index));
  return tap ? [...others, { layout, index, tap, hold: null, label: null }] : others;
}

/**
 * Actions whose value is a scene, channel, Manual or view. The engine counts them from 0 (Scene 1 is 0, channel A is 0),
 * but people count from 1, as External does: the editor shows and takes them 1-based and converts here, at the edge, so
 * stored settings and presets keep their meaning.
 */
const CountedFromZero: ReadonlySet<string> = new Set(['scene', 'channel', 'manual', 'view']);

/** An action's value as people read and type it: 1-based for scenes, channels, Manuals and views. */
export function shownValue(action: string, value: number | null | undefined): number | null {
  if (value === null || value === undefined) {
    return null;
  }

  return CountedFromZero.has(action) ? value + 1 : value;
}

/** The value to store for a number people typed: the inverse of {@link shownValue} (1 and below are the first). */
export function storedValue(action: string, shown: number | null): number | null {
  if (shown === null) {
    return null;
  }

  return CountedFromZero.has(action) ? Math.max(0, shown - 1) : shown;
}

/** An action as text, "action target value", with the value as people count it: Scene 1 is "scene 1". */
export function actionText(action: SwitchAction): string {
  return [action.action, action.target, shownValue(action.action, action.value)]
    .filter(p => p !== null && p !== undefined)
    .join(' ');
}

/**
 * "bypass delay1" → { action: bypass, target: delay1 }; a trailing number is the value, as people count it ("scene 1"
 * is the first scene); empty text, or text that does not start with one of `actions`, → null.
 */
export function parseAction(text: string, actions: readonly string[]): SwitchAction | null {
  const parts = text.trim().split(/\s+/).filter(Boolean);
  const [action] = parts;
  if (action === undefined || !actions.includes(action)) {
    return null;
  }

  const final = parts.at(-1);
  const last = parts.length > 1 && final !== undefined && /^\d+$/.test(final) ? Number(parts.pop()) : null;
  return { action, target: parts[1] ?? null, value: storedValue(action, last) };
}
