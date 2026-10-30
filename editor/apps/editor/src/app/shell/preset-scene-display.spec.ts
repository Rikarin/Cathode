import { presetBank, presetPlace } from './preset-scene-display';

const banks = [
  { id: 'factory', name: 'Factory', slots: ['f1', 'f2'] },
  { id: 'gig', name: 'Gig', slots: ['a', null, 'b', 'a'] }
];

describe('presetPlace', () => {
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

describe('presetBank', () => {
  it('is the bank the preset was loaded from, even when an earlier bank holds it too', () => {
    expect(presetBank({ bank: 'gig', slot: 0 }, 'a', banks)).toBe('gig');
    expect(presetBank({ bank: 'factory', slot: 1 }, 'f2', banks)).toBe('factory');
  });

  it('is the first bank holding a preset loaded by name', () => {
    expect(presetBank(null, 'b', banks)).toBe('gig');
    expect(presetBank(undefined, 'f1', banks)).toBe('factory');
  });

  it('is none for a setlist entry, a preset in no bank, or a bank since deleted', () => {
    expect(presetBank({ setlist: 's1', entry: 0 }, 'a', banks)).toBeNull();
    expect(presetBank(null, 'loose', banks)).toBeNull();
    expect(presetBank(null, undefined, banks)).toBeNull();
    expect(presetBank({ bank: 'gone', slot: 0 }, 'a', banks)).toBeNull();
  });
});
