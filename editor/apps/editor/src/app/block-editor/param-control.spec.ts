import type { FootswitchLayout, MidiMapping } from '@cathode/api-client';
import { freeExternal } from './param-control';

describe('Learn MIDI controller', () => {
  const mapping = (value: number): MidiMapping => ({
    id: `m${value}`,
    message: 'cc',
    channel: 0,
    number: 10 + value,
    action: 'external',
    value
  });

  it('picks the first External no global mapping, footswitch or preset modifier uses', () => {
    expect(freeExternal({ midiMappings: [], footswitches: [] }, [], [])).toBe(1);
    // Learned in another preset: External 1 already follows a pedal, though this preset does not use it.
    expect(freeExternal({ midiMappings: [mapping(1)], footswitches: [] }, [], [])).toBe(2);
    expect(freeExternal({ midiMappings: [mapping(1)], footswitches: [] }, [{ source: 'ext2' }], [])).toBe(3);
    const layout: FootswitchLayout = {
      id: 'main',
      name: 'Main',
      switches: [
        {
          index: 1,
          label: '1',
          message: 'cc',
          channel: 1,
          number: 1,
          tap: { action: 'external', value: 1 },
          hold: { action: 'external', value: 2 },
          led: false,
          view: 0
        }
      ]
    };
    expect(freeExternal({ midiMappings: [], footswitches: [layout] }, [], [])).toBe(3);
    expect(
      freeExternal(
        { midiMappings: [], footswitches: [] },
        [],
        [{ layout: 'main', index: 1, tap: { action: 'external', value: 1 }, hold: null }]
      )
    ).toBe(2);
  });

  it('finds none when all eight are taken', () => {
    const all = [1, 2, 3, 4, 5, 6, 7, 8].map(mapping);
    expect(freeExternal({ midiMappings: all, footswitches: [] }, [], [])).toBeNull();
  });
});
