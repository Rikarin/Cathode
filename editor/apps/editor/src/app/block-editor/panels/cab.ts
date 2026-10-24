import type { ConeMic } from '@cathode/audio-ui';
import { sectionDb } from '../../dsp/response';

/** Slots in the Cab block, as the engine's `CabBlock.Slots`. */
export const CabSlots = 4;

/** The per-slot parameters, in the engine's order (`m1source` … `m4solo`). */
export const SlotKeys = [
  'source',
  'cab',
  'speaker',
  'mic',
  'position',
  'distance',
  'angle',
  'lowcut',
  'lowslope',
  'highcut',
  'highslope',
  'length',
  'level',
  'pan',
  'delay',
  'invert',
  'mute',
  'solo'
] as const;

export type SlotKey = (typeof SlotKeys)[number];

/** A slot's Source values, as the engine's `CabBlock.SourceIr`, `SourceDynaCab` and `SourcePack`. */
export const Source = { ir: 0, dynaCab: 1, pack: 2 } as const;

export type SourceValue = (typeof Source)[keyof typeof Source];

/** Parameter id of `key` in slot `slot` (0-based). */
export const slotParamId = (slot: number, key: SlotKey): string => `m${slot + 1}${key}`;

const SlotParam = new RegExp(`^m[1-${CabSlots}](${SlotKeys.join('|')})$`);

/** True for a slot's parameters, which the Cab panel shows per slot instead of as 72 knobs. */
export const isCabSlotParam = (id: string): boolean => SlotParam.test(id);

/** The asset slot of slot `slot` (0-based): an IR for an IR slot, a pack (or one IR) for an IR Pack slot. */
export const slotAsset = (slot: number): string => `slot${slot + 1}`;

/** The source a slot plays. */
export const sourceOf = (slot: number, value: (id: string) => number): SourceValue =>
  Math.round(value(slotParamId(slot, 'source'))) as SourceValue;

/** "Dynamic 57 (SM57)" → "SM57": the model in brackets, or the whole name. */
export function shortMicName(option: string | undefined): string {
  const model = option?.match(/\(([^)]+)\)\s*$/);
  return model ? model[1] : (option ?? '');
}

/** Whether a slot's own mute switch is on. */
export const isMuted = (slot: number, value: (id: string) => number): boolean =>
  value(slotParamId(slot, 'mute')) >= 0.5;

/** Whether a slot is heard: not muted, and soloed if any slot is. */
export function isAudible(slot: number, value: (id: string) => number): boolean {
  const soloed = Array.from({ length: CabSlots }, (_, s) => value(slotParamId(s, 'solo')) >= 0.5);
  return !isMuted(slot, value) && (!soloed.some(Boolean) || soloed[slot]);
}

/**
 * The mute switch to turn off when the user edits `key` on `slot`, or null. Editing a muted slot (moving it, choosing
 * its source, mic, speaker or cab, loading its file, turning a knob) unmutes it, so the edit is heard; the mute switch
 * itself is the one edit that leaves it muted.
 */
export function unmuteOnEdit(slot: number, key: SlotKey | 'asset', value: (id: string) => number): string | null {
  return key !== 'mute' && isMuted(slot, value) ? slotParamId(slot, 'mute') : null;
}

/**
 * The mics for the cone diagram from the block's values: the DynaCab and IR Pack slots (an IR file has no place on the
 * cone). A slot playing a pack is named after the pack, as its mic choice does not apply.
 */
export function coneMics(
  value: (id: string) => number,
  micOptions: readonly string[],
  packName: (slot: number) => string | undefined
): ConeMic[] {
  return Array.from({ length: CabSlots }, (_, slot) => slot)
    .filter(slot => sourceOf(slot, value) !== Source.ir)
    .map(slot => ({
      slot,
      name:
        sourceOf(slot, value) === Source.pack
          ? (packName(slot) ?? 'No pack')
          : shortMicName(micOptions[Math.round(value(slotParamId(slot, 'mic')))]),
      position: value(slotParamId(slot, 'position')),
      distance: value(slotParamId(slot, 'distance')),
      angle: sourceOf(slot, value) === Source.pack ? 0 : value(slotParamId(slot, 'angle')),
      muted: !isAudible(slot, value)
    }));
}

/** Low cut at or below this, and high cut at or above `HighCutOff`, are off (the engine's `CabBlock.LowCutOff`). */
export const LowCutOff = 10;
export const HighCutOff = 20_000;

/**
 * The response in dB at `frequency` of a cut of slope index `slope` (6, 12, 18, 24 dB/oct), as the engine's
 * `CutFilter`: a one-pole and up to two Butterworth sections.
 */
export function cutDb(
  highPass: boolean,
  cutoff: number,
  slope: number,
  frequency: number,
  sampleRate = 48_000
): number {
  const shape = highPass ? 'highpass' : 'lowpass';
  const ratio = (frequency / cutoff) ** 2;
  const onePole = highPass ? 10 * Math.log10(ratio / (1 + ratio)) : -10 * Math.log10(1 + ratio);
  const section = (q: number) => sectionDb(shape, cutoff, 0, q, frequency, sampleRate);
  switch (Math.round(slope)) {
    case 0:
      return onePole;
    case 2:
      return onePole + section(1);
    case 3:
      return section(0.5412) + section(1.3066);
    default:
      return section(0.7071);
  }
}

/** A low and high cut pair's response in dB at `frequency`, either side off at its limit. */
export function cutsDb(
  lowCut: number,
  lowSlope: number,
  highCut: number,
  highSlope: number,
  frequency: number
): number {
  return (
    (lowCut > LowCutOff ? cutDb(true, lowCut, lowSlope, frequency) : 0) +
    (highCut < HighCutOff ? cutDb(false, highCut, highSlope, frequency) : 0)
  );
}

/**
 * Each slot's level as heard, in dB: the engine scales the levels of the slots that play (audible, with something to
 * play) to sum to unity, so a slot's level sets the blend, not the loudness. `plays(slot)` says whether its source has
 * something to play.
 */
export function relativeLevelsDb(value: (id: string) => number, plays: (slot: number) => boolean): number[] {
  const gains = Array.from({ length: CabSlots }, (_, slot) => {
    const db = value(slotParamId(slot, 'level'));
    return isAudible(slot, value) && plays(slot) && db > -80 ? 10 ** (db / 20) : 0;
  });
  const total = gains.reduce((sum, g) => sum + g, 0);
  return gains.map(g => (g > 0 && total > 0 ? 20 * Math.log10(g / total) : -Infinity));
}
