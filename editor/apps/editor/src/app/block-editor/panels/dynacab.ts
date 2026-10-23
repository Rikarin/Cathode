import type { ConeMic } from '@cathode/audio-ui';

/** Mic slots in the DynaCab block, as the engine's `DynaCabBlock.Slots`. */
export const DynaCabSlots = 4;

/** The per-slot parameters, in the engine's order (`m1cab` … `m4solo`). */
export const SlotKeys = [
  'cab',
  'speaker',
  'mic',
  'position',
  'distance',
  'angle',
  'level',
  'pan',
  'delay',
  'invert',
  'mute',
  'solo'
] as const;

export type SlotKey = (typeof SlotKeys)[number];

/** Parameter id of `key` in mic slot `slot` (0-based). */
export const slotParamId = (slot: number, key: SlotKey): string => `m${slot + 1}${key}`;

const SlotParam = new RegExp(`^m[1-${DynaCabSlots}](${SlotKeys.join('|')})$`);

/** True for a mic slot's parameters, which the DynaCab panel shows per mic instead of as 48 knobs. */
export const isDynaCabSlotParam = (id: string): boolean => SlotParam.test(id);

/** The grid asset slot of mic slot `slot` (0-based). */
export const gridSlot = (slot: number): string => `grid${slot + 1}`;

/** "Dynamic 57 (SM57)" → "SM57": the model in brackets, or the whole name. */
export function shortMicName(option: string | undefined): string {
  const model = option?.match(/\(([^)]+)\)\s*$/);
  return model ? model[1] : (option ?? '');
}

/** Whether a slot is heard: not muted, and soloed if any slot is. */
export function isAudible(slot: number, value: (id: string) => number): boolean {
  const soloed = Array.from({ length: DynaCabSlots }, (_, s) => value(slotParamId(s, 'solo')) >= 0.5);
  return value(slotParamId(slot, 'mute')) < 0.5 && (!soloed.some(Boolean) || soloed[slot]);
}

/**
 * The mics for the cone diagram from the block's values. A slot with an IR pack is named after the pack, as its mic
 * choice does not apply.
 */
export function coneMics(
  value: (id: string) => number,
  micOptions: readonly string[],
  packName: (slot: number) => string | undefined
): ConeMic[] {
  return Array.from({ length: DynaCabSlots }, (_, slot) => ({
    slot,
    name: packName(slot) ?? shortMicName(micOptions[Math.round(value(slotParamId(slot, 'mic')))]),
    position: value(slotParamId(slot, 'position')),
    distance: value(slotParamId(slot, 'distance')),
    angle: value(slotParamId(slot, 'angle')),
    muted: !isAudible(slot, value)
  }));
}
