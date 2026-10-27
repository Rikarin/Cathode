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

/** A change to a switch's override: the fields left out keep their value. */
export interface OverrideChange {
  /** The preset's tap action; null removes the override (the contract needs a tap). */
  tap?: SwitchAction | null;
  /** The preset's hold action; null means none while the preset is loaded, whatever the layout holds. */
  hold?: SwitchAction | null;
  /** The switch's caption while the preset is loaded; blank goes back to the layout's. */
  label?: string | null;
}

/**
 * The overrides with the switch's changed. A new override needs a tap: a hold or label for a switch the preset does
 * not override yet changes nothing. An override keeps its place in the list.
 */
export function withOverride(
  overrides: readonly FootswitchOverride[],
  layout: string,
  index: number,
  change: OverrideChange
): FootswitchOverride[] {
  const own = overrideOf(overrides, layout, index);
  const tap = change.tap === undefined ? own?.tap : change.tap;
  if (!tap) {
    return overrides.filter(o => o !== own);
  }

  const next: FootswitchOverride = {
    layout,
    index,
    tap,
    hold: change.hold === undefined ? (own?.hold ?? null) : change.hold,
    label: change.label === undefined ? (own?.label ?? null) : change.label?.trim() || null
  };
  return own ? overrides.map(o => (o === own ? next : o)) : [...overrides, next];
}

/**
 * Actions whose value is a scene, channel, Manual or view. The engine counts them from 0 (Scene 1 is 0, channel A is 0),
 * but people count from 1, as External does: the editor shows them 1-based, so stored settings and presets keep their
 * meaning.
 */
const CountedFromZero: ReadonlySet<string> = new Set(['scene', 'channel', 'manual', 'view']);

/** An action's value as people read it: 1-based for scenes, channels, Manuals and views. */
export function shownValue(action: string, value: number | null | undefined): number | null {
  if (value === null || value === undefined) {
    return null;
  }

  return CountedFromZero.has(action) ? value + 1 : value;
}

/** An action as text, "action target value", with the value as people count it: Scene 1 is "scene 1". */
export function actionText(action: SwitchAction): string {
  return [action.action, action.target, shownValue(action.action, action.value)]
    .filter(p => p !== null && p !== undefined)
    .join(' ');
}
