import type { BlockTypeInfo, ParameterInfo, PerformControl, Preset, PresetBlock } from '@cathode/api-client';

/** The page with `control` added at the end, unless it already holds that block's parameter. */
export function withControl(page: readonly PerformControl[], control: PerformControl): PerformControl[] {
  return page.some(c => c.block === control.block && c.param === control.param) ? [...page] : [...page, control];
}

export function withoutControl(page: readonly PerformControl[], control: PerformControl): PerformControl[] {
  return page.filter(c => !(c.block === control.block && c.param === control.param));
}

/** A control as the Perform page shows it, or why it cannot: its block is not in this preset. */
export interface ResolvedControl {
  control: PerformControl;
  block: PresetBlock | null;
  param: ParameterInfo | null;
  label: string;
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
    return {
      control,
      block,
      param,
      label: control.label ?? (param ? `${blockName} ${param.name}` : `${control.block} ${control.param}`)
    };
  });
}
