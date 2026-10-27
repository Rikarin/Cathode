import type { BlockTypeInfo, ParameterInfo, PerformControl, Preset, PresetBlock } from '@cathode/api-client';

/** The page with `control` added at the end, unless it already holds that block's parameter. */
export function withControl(page: readonly PerformControl[], control: PerformControl): PerformControl[] {
  return page.some(c => c.block === control.block && c.param === control.param) ? [...page] : [...page, control];
}

export function withoutControl(page: readonly PerformControl[], control: PerformControl): PerformControl[] {
  return page.filter(c => !(c.block === control.block && c.param === control.param));
}

/** The page with `control`'s caption set to `label`: blank text (or null) goes back to the parameter's own name. */
export function withLabel(
  page: readonly PerformControl[],
  control: PerformControl,
  label: string | null
): PerformControl[] {
  const caption = label?.trim() || null;
  return page.map(c => (c.block === control.block && c.param === control.param ? { ...c, label: caption } : c));
}

/** The page with the control at `from` moved to `to` (both clamped to the page); the others keep their order. */
export function movedControl(page: readonly PerformControl[], from: number, to: number): PerformControl[] {
  const moved = [...page];
  if (from < 0 || from >= moved.length) {
    return moved;
  }

  const [control] = moved.splice(from, 1);
  if (control) {
    moved.splice(Math.min(Math.max(0, to), moved.length), 0, control);
  }

  return moved;
}

/** A control as the Perform page shows it, or why it cannot: its block is not in this preset. */
export interface ResolvedControl {
  control: PerformControl;
  block: PresetBlock | null;
  param: ParameterInfo | null;
  /** The caption shown: the control's own label, else {@link name}. */
  label: string;
  /** The caption without a label of its own: block and parameter names. */
  name: string;
}

/** Resolves a page against the loaded preset: a global control whose block the preset lacks shows as missing. */
export function resolveControls(
  page: readonly PerformControl[],
  preset: Preset | null,
  info: (type: string) => BlockTypeInfo | undefined
): ResolvedControl[] {
  return page.map(control => {
    const block = preset?.blocks.find(b => b.id === control.block) ?? null;
    const param = block ? (info(block.type)?.params.find(p => p.id === control.param) ?? null) : null;
    const blockName = block ? (info(block.type)?.name ?? block.type) : control.block;
    const name = param ? `${blockName} ${param.name}` : `${control.block} ${control.param}`;
    return { control, block, param, label: control.label ?? name, name };
  });
}
