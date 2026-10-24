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
