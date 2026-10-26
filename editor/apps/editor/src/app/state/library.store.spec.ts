import type { Bank, Setlist } from '@cathode/api-client';
import { presetUses } from './library.store';

describe('presetUses', () => {
  const banks: Bank[] = [
    { id: 'factory', name: 'Factory', slots: ['f1'], factory: true },
    { id: 'gig', name: 'Gig', slots: ['a', 'x', 'b', 'a'] }
  ];
  const setlists: Setlist[] = [
    {
      id: 'friday',
      name: 'Friday',
      entries: [
        { presetId: 'b', scene: 0 },
        { presetId: 'a', scene: 2 }
      ]
    }
  ];

  it('names every bank slot and setlist entry that holds the preset, counted from 1', () => {
    expect(presetUses('a', banks, setlists)).toEqual(['bank “Gig”, slots 1, 4', 'setlist “Friday”, entry 2']);
    expect(presetUses('b', banks, setlists)).toEqual(['bank “Gig”, slot 3', 'setlist “Friday”, entry 1']);
  });

  it('is empty for a preset nothing uses', () => {
    expect(presetUses('c', banks, setlists)).toEqual([]);
  });
});
