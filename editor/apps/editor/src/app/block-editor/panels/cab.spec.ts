import {
  Source,
  coneMics,
  cutDb,
  cutsDb,
  isAudible,
  isCabSlotParam,
  isMuted,
  relativeLevelsDb,
  shortMicName,
  slotAsset,
  slotParamId,
  sourceOf,
  unmuteOnEdit
} from './cab';

const Mics = ['Dynamic 57 (SM57)', 'Ribbon 121 (R-121)', 'Measurement Omni'];

/** The engine's defaults for the values these helpers read: slot 1 a DynaCab, the rest muted. */
function values(overrides: Record<string, number> = {}): (id: string) => number {
  const base: Record<string, number> = {};
  for (let slot = 0; slot < 4; slot++) {
    base[slotParamId(slot, 'source')] = Source.dynaCab;
    base[slotParamId(slot, 'mic')] = slot % 3;
    base[slotParamId(slot, 'position')] = 25 * slot;
    base[slotParamId(slot, 'distance')] = slot;
    base[slotParamId(slot, 'angle')] = 10;
    base[slotParamId(slot, 'mute')] = slot === 0 ? 0 : 1;
    base[slotParamId(slot, 'solo')] = 0;
  }

  return id => overrides[id] ?? base[id] ?? 0;
}

describe('Cab panel helpers', () => {
  it('names slot parameters and asset slots as the engine does', () => {
    expect(slotParamId(0, 'source')).toBe('m1source');
    expect(slotParamId(0, 'cab')).toBe('m1cab');
    expect(slotParamId(3, 'solo')).toBe('m4solo');
    expect(slotAsset(1)).toBe('slot2');
  });

  it('recognises slot parameters and nothing else', () => {
    expect(isCabSlotParam('m2position')).toBe(true);
    expect(isCabSlotParam('m4invert')).toBe(true);
    expect(isCabSlotParam('m1source')).toBe(true);
    expect(isCabSlotParam('m3highslope')).toBe(true);
    expect(isCabSlotParam('m2length')).toBe(true);
    expect(isCabSlotParam('lowslope')).toBe(false);
    expect(isCabSlotParam('drive')).toBe(false);
    expect(isCabSlotParam('m5mic')).toBe(false);
    expect(isCabSlotParam('level')).toBe(false);
    expect(isCabSlotParam('lowcut')).toBe(false);
    expect(isCabSlotParam('autodelay')).toBe(false);
    expect(isCabSlotParam('roomsize')).toBe(false);
    expect(isCabSlotParam('smoothing')).toBe(false);
  });

  it('shortens mic names to their model', () => {
    expect(shortMicName('Dynamic 57 (SM57)')).toBe('SM57');
    expect(shortMicName('Measurement Omni')).toBe('Measurement Omni');
    expect(shortMicName(undefined)).toBe('');
  });

  it('follows mute and solo', () => {
    expect(isAudible(0, values())).toBe(true);
    expect(isAudible(1, values())).toBe(false);
    expect(isMuted(1, values())).toBe(true);
    const soloTwo = values({ m2mute: 0, m2solo: 1 });
    expect(isAudible(0, soloTwo)).toBe(false);
    expect(isMuted(0, soloTwo)).toBe(false); // silenced by the solo, not by its own switch
    expect(isAudible(1, soloTwo)).toBe(true);
  });

  it('reads each slot source', () => {
    const mixed = values({ m2source: Source.ir, m3source: Source.pack });
    expect([0, 1, 2, 3].map(slot => sourceOf(slot, mixed))).toEqual([
      Source.dynaCab,
      Source.ir,
      Source.pack,
      Source.dynaCab
    ]);
  });

  it('puts DynaCab and IR pack slots on the cone, named after their mic or pack, and leaves IR files off', () => {
    const mixed = values({ m2source: Source.ir, m3source: Source.pack });
    const mics = coneMics(mixed, Mics, slot => (slot === 2 ? 'My 4x12' : undefined));

    expect(mics.map(m => m.slot)).toEqual([0, 2, 3]);
    expect(mics.map(m => m.name)).toEqual(['SM57', 'My 4x12', 'SM57']);
    expect(mics[1]).toEqual({ slot: 2, name: 'My 4x12', position: 50, distance: 2, angle: 0, muted: true });
    expect(mics[0].muted).toBe(false);
    expect(coneMics(values({ m3source: Source.pack }), Mics, () => undefined)[2].name).toBe('No pack');
  });

  describe('editing a muted slot', () => {
    const muted = values(); // slots 2-4 muted

    it('unmutes it for a move, a choice, a source, a file or a knob', () => {
      for (const key of ['position', 'distance', 'mic', 'speaker', 'cab', 'source', 'level', 'solo'] as const) {
        expect(unmuteOnEdit(1, key, muted)).toBe('m2mute');
      }

      expect(unmuteOnEdit(3, 'asset', muted)).toBe('m4mute');
    });

    it('leaves it muted when the edit is the mute switch itself', () => {
      expect(unmuteOnEdit(1, 'mute', muted)).toBeNull();
    });

    it('does nothing for a slot that is already on', () => {
      expect(unmuteOnEdit(0, 'position', muted)).toBeNull();
      expect(unmuteOnEdit(1, 'position', values({ m2mute: 0 }))).toBeNull();
    });
  });

  it('draws cuts with their slopes, as the engine filters', () => {
    expect(cutDb(true, 100, 1, 100)).toBeCloseTo(-3.01, 1); // 12 dB/oct Butterworth: -3 dB at the corner
    expect(cutDb(true, 100, 0, 100)).toBeCloseTo(-3.01, 1); // 6 dB/oct too
    expect(cutDb(true, 100, 3, 100)).toBeCloseTo(-3.01, 1); // and 24
    expect(cutDb(true, 1_000, 3, 250) - cutDb(true, 1_000, 3, 500)).toBeCloseTo(-24, 0); // an octave down at 24 dB/oct
    expect(cutDb(false, 1_000, 0, 8_000) - cutDb(false, 1_000, 0, 4_000)).toBeCloseTo(-6, 0);
    expect(cutsDb(10, 1, 20_000, 1, 50)).toBe(0); // both off
  });

  it('scales the levels of the slots that play to sum to unity', () => {
    const plays = () => true;
    const one = relativeLevelsDb(values({ m1level: -12 }), plays);
    expect(one[0]).toBeCloseTo(0, 6); // alone, a slot is at unity whatever its level
    expect(one[1]).toBe(-Infinity); // muted

    const two = relativeLevelsDb(values({ m2mute: 0, m1level: 6, m2level: 6 }), plays);
    expect(two[0]).toBeCloseTo(-6.02, 2);
    expect(two[1]).toBeCloseTo(-6.02, 2);

    const empty = relativeLevelsDb(values({ m2mute: 0 }), slot => slot === 0); // slot 2 has nothing to play
    expect(empty[0]).toBeCloseTo(0, 6);
  });
});
