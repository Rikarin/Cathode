import { presetPlace } from './preset-scene-display';

describe('presetPlace', () => {
  const banks = [
    { id: 'factory', name: 'Factory', slots: ['f1', 'f2'] },
    { id: 'gig', name: 'Gig', slots: ['a', null, 'b', 'a'] }
  ];

  it('numbers the slot or setlist entry the preset was loaded from, from 1', () => {
    expect(presetPlace({ bank: 'gig', slot: 3 }, 'a', banks)).toEqual({ number: '004', where: 'Gig, slot 4' });
    expect(presetPlace({ setlist: 's1', entry: 1 }, 'b', banks)).toEqual({
      number: '002',
      where: 'Setlist entry 2'
    });
  });

  it('finds the first slot holding a preset loaded by name, in bank order', () => {
    expect(presetPlace(null, 'a', banks)).toEqual({ number: '001', where: 'Gig, slot 1' });
    expect(presetPlace(undefined, 'f2', banks)).toEqual({ number: '002', where: 'Factory, slot 2' });
  });

  it('has no number for a preset in no bank', () => {
    expect(presetPlace(null, 'loose', banks)).toEqual({ number: '---', where: 'In no bank' });
    expect(presetPlace(null, undefined, banks)).toEqual({ number: '---', where: 'In no bank' });
  });
});
