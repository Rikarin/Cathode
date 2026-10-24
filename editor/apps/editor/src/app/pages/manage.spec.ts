import type { Bank, PresetSummary } from '@cathode/api-client';
import { AssetSession, ManageSession, nextSelection } from './manage';

const preset = (id: string, name = id): PresetSummary => ({ id, name, tags: [], modified: '', favorite: false });

function session() {
  let n = 0;
  const banks = [
    { id: 'one', name: 'One', slots: ['a', 'b', 'c'] },
    { id: 'two', name: 'Two', slots: ['d'] }
  ] as Bank[];
  return new ManageSession(banks, [preset('a', 'Alpha'), preset('b'), preset('c'), preset('d')], () => `new${++n}`);
}

describe('Manage Presets', () => {
  it('selects with ⇧ for a range and ⌘ to toggle', () => {
    let s = nextSelection(new Set(), null, 2, { shift: false, toggle: false });
    s = nextSelection(s.selected, s.anchor, 5, { shift: true, toggle: false });
    expect([...s.selected]).toEqual([2, 3, 4, 5]);
    s = nextSelection(s.selected, s.anchor, 3, { shift: false, toggle: true });
    expect([...s.selected].sort()).toEqual([2, 4, 5]);
    s = nextSelection(s.selected, s.anchor, 7, { shift: false, toggle: false });
    expect([...s.selected]).toEqual([7]);
  });

  it('holds swaps, moves and copies until Save, marking what changed', () => {
    const m = session();
    m.swap({ bank: 'one', slot: 0 }, { bank: 'one', slot: 2 });
    m.move({ bank: 'one', slot: 1 }, { bank: 'two', slot: 5 });
    m.copy({ bank: 'two', slot: 0 }, { bank: 'one', slot: 1 });

    expect(m.bank('one').slots.slice(0, 3)).toEqual(['c', 'new1', 'a']);
    expect(m.bank('two').slots[5]).toBe('b');
    expect(m.nameOf('new1')).toBe('d copy');
    expect(m.isChanged({ bank: 'one', slot: 0 })).toBe(true);
    expect(m.isChanged({ bank: 'two', slot: 0 })).toBe(false);
    expect(m.dirty).toBe(true);
  });

  it('plans Save in an order the Server accepts: copies, renames, banks, deletions', () => {
    const m = session();
    m.duplicateSlots([{ bank: 'one', slot: 0 }]);
    m.rename('b', 'Bravo');
    m.setMeta('c', { color: 'red', favorite: true });
    m.delete(['d']);

    const plan = m.plan();
    expect(plan.copies).toEqual([{ id: 'new1', source: 'a', name: 'Alpha copy' }]);
    expect(m.bank('one').slots[3]).toBe('new1');
    expect(plan.meta).toEqual([
      ['b', { name: 'Bravo' }],
      ['c', { color: 'red', favorite: true }]
    ]);
    expect(plan.banks.map(b => [b.id, b.slots])).toEqual([
      ['one', ['a', 'b', 'c', 'new1']],
      ['two', []]
    ]);
    expect(plan.deletes).toEqual(['d']);
  });

  it('drops a pending copy rather than deleting it, and leaves untouched banks out of Save', () => {
    const m = session();
    m.copy({ bank: 'one', slot: 0 }, { bank: 'one', slot: 9 });
    m.delete(['new1']);

    expect(m.plan().copies).toEqual([]);
    expect(m.plan().deletes).toEqual([]);
    expect(m.plan().banks.map(b => b.id)).toEqual(['one']);
  });

  it('starts clean, and a new bank is saved with it', () => {
    const m = session();
    expect(m.dirty).toBe(false);
    m.addBank('Three');
    expect(m.plan().banks.map(b => b.name)).toEqual(['Three']);
  });
});

describe('Manage IRs', () => {
  it('holds renames, tags and deletions until Save', () => {
    const s = new AssetSession();
    s.change(['sha256:1', 'sha256:2'], { color: 'blue' });
    s.change(['sha256:1'], { name: 'Room' });
    s.delete(['sha256:2']);

    expect(s.meta.get('sha256:1')).toEqual({ color: 'blue', name: 'Room' });
    expect(s.meta.has('sha256:2')).toBe(false);
    expect(s.isChanged('sha256:2')).toBe(true);
    expect(s.dirty).toBe(true);
  });
});
