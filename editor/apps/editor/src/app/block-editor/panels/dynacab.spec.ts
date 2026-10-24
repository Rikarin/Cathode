import { coneMics, gridSlot, isAudible, isDynaCabSlotParam, shortMicName, slotParamId } from './dynacab';

const Mics = ['Dynamic 57 (SM57)', 'Ribbon 121 (R-121)', 'Measurement Omni'];

/** The engine's defaults for the values these helpers read: slot 1 on, the rest muted. */
function values(overrides: Record<string, number> = {}): (id: string) => number {
  const base: Record<string, number> = {};
  for (let slot = 0; slot < 4; slot++) {
    base[slotParamId(slot, 'mic')] = slot % 3;
    base[slotParamId(slot, 'position')] = 25 * slot;
    base[slotParamId(slot, 'distance')] = slot;
    base[slotParamId(slot, 'angle')] = 0;
    base[slotParamId(slot, 'mute')] = slot === 0 ? 0 : 1;
    base[slotParamId(slot, 'solo')] = 0;
  }

  return id => overrides[id] ?? base[id] ?? 0;
}

describe('DynaCab panel helpers', () => {
  it('names slot parameters and asset slots as the engine does', () => {
    expect(slotParamId(0, 'cab')).toBe('m1cab');
    expect(slotParamId(3, 'solo')).toBe('m4solo');
    expect(gridSlot(1)).toBe('grid2');
  });

  it('recognises slot parameters and nothing else', () => {
    expect(isDynaCabSlotParam('m2position')).toBe(true);
    expect(isDynaCabSlotParam('m4invert')).toBe(true);
    expect(isDynaCabSlotParam('m5mic')).toBe(false);
    expect(isDynaCabSlotParam('level')).toBe(false);
    expect(isDynaCabSlotParam('lowcut')).toBe(false);
  });

  it('shortens mic names to their model', () => {
    expect(shortMicName('Dynamic 57 (SM57)')).toBe('SM57');
    expect(shortMicName('Measurement Omni')).toBe('Measurement Omni');
    expect(shortMicName(undefined)).toBe('');
  });

  it('follows mute and solo', () => {
    expect(isAudible(0, values())).toBe(true);
    expect(isAudible(1, values())).toBe(false);
    const soloTwo = values({ m2mute: 0, m2solo: 1 });
    expect(isAudible(0, soloTwo)).toBe(false);
    expect(isAudible(1, soloTwo)).toBe(true);
  });

  it('builds the diagram mics, named after a loaded pack where there is one', () => {
    const mics = coneMics(values(), Mics, slot => (slot === 2 ? 'My 4x12' : undefined));

    expect(mics.map(m => m.name)).toEqual(['SM57', 'R-121', 'My 4x12', 'SM57']);
    expect(mics[1]).toEqual({ slot: 1, name: 'R-121', position: 25, distance: 1, angle: 0, muted: true });
    expect(mics[0].muted).toBe(false);
  });
});
