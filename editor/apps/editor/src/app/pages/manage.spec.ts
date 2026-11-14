import type { Bank, PresetSummary } from '@cathode/api-client';
import { ManageSession, nextSelection } from './manage';

/** Bank One as the Server lists it once it has stored it four times. */
const revised = (slots: string[]): Bank & { revision: number } => ({ id: 'one', name: 'One', slots, revision: 4 });

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

  it("keeps a renamed bank's name on Save, and a deleted bank takes its pending slot changes with it", () => {
    const m = session();
    m.renameBank('one', 'Live');
    m.swap({ bank: 'one', slot: 0 }, { bank: 'one', slot: 1 });
    m.move({ bank: 'two', slot: 0 }, { bank: 'two', slot: 3 });
    m.deleteBank('two');

    expect(m.plan().banks.map(b => b.name)).toEqual(['Live']);
    expect(m.banks.map(b => b.id)).toEqual(['one']);
  });

  it('lets go of each step Save has stored, so a Save that failed part-way can be tried again', () => {
    const m = session();
    m.duplicateSlots([{ bank: 'one', slot: 0 }]);
    m.setMeta('new1', { color: 'red' });
    m.rename('b', 'Bravo');
    m.move({ bank: 'one', slot: 2 }, { bank: 'two', slot: 0 });
    m.delete(['d']);

    // The copy and the rename were stored; then a request was refused.
    m.copyStored('new1');
    m.metaStored('b');

    let plan = m.plan();
    expect(plan.copies).toEqual([]);
    expect(plan.meta).toEqual([['new1', { color: 'red' }]]); // the copy is a stored preset now: all of it is sent
    expect(plan.banks.map(b => b.id)).toEqual(['one', 'two']);
    expect(plan.deletes).toEqual(['d']);
    expect(m.nameOf('new1')).toBe('Alpha copy');
    expect(m.nameOf('b')).toBe('Bravo');
    expect(m.dirty).toBe(true);

    m.metaStored('new1');
    m.bankStored('one');
    plan = m.plan();
    expect(plan.banks.map(b => b.id)).toEqual(['two']);
    expect(m.isChanged({ bank: 'one', slot: 3 })).toBe(false);

    m.bankStored('two');
    m.deleteStored('d');
    expect(m.dirty).toBe(false);
    expect(m.presets.has('d')).toBe(false);
  });

  it('takes a preset back out of the deletions', () => {
    const m = session();
    m.delete(['d']);
    m.keep(['d']);

    expect(m.plan().deletes).toEqual([]);
    expect(m.bank('two').slots[0]).toBeNull(); // it stays out of the slot it left
  });

  it('starts clean, and a bank stored by the page joins it without a change to save', () => {
    const m = session();
    m.addBank({ id: 'three', name: 'Three', slots: [] });
    expect(m.banks.map(b => b.name)).toEqual(['One', 'Two', 'Three']);
    expect(m.dirty).toBe(false);
  });

  it('leaves a ⌘-drag onto a factory slot alone, instead of emptying the slot it came from', () => {
    const banks = [
      { id: 'factory', name: 'Factory', slots: ['f1'], factory: true },
      { id: 'mine', name: 'Mine', slots: ['a'] }
    ] as Bank[];
    const m = new ManageSession(banks, [{ ...preset('f1'), factory: true }, preset('a')], () => 'new1');

    m.move({ bank: 'mine', slot: 0 }, { bank: 'factory', slot: 5 });

    expect(m.bank('mine').slots[0]).toBe('a'); // still there: the factory slot took nothing
    expect(m.bank('factory').slots[5] ?? null).toBeNull();
    expect(m.dirty).toBe(false);
  });

  it('keeps the factory bank and its presets read-only, but lets them into user banks', () => {
    let n = 0;
    const banks = [
      { id: 'factory', name: 'Factory', slots: ['f1', 'f2'], factory: true },
      { id: 'mine', name: 'Mine', slots: ['a'] }
    ] as Bank[];
    const m = new ManageSession(
      banks,
      [{ ...preset('f1', 'Brown Sound'), factory: true }, { ...preset('f2'), factory: true }, preset('a')],
      () => `new${++n}`
    );

    m.swap({ bank: 'factory', slot: 0 }, { bank: 'mine', slot: 0 });
    m.copy({ bank: 'factory', slot: 1 }, { bank: 'mine', slot: 3 });
    m.copy({ bank: 'mine', slot: 3 }, { bank: 'factory', slot: 5 });
    m.duplicateSlots([{ bank: 'factory', slot: 0 }]);
    m.rename('f1', 'Mine now');
    m.delete(['f2']);

    expect(m.bank('factory').slots.slice(0, 6)).toEqual(['f1', 'f2', null, null, null, null]);
    expect(m.bank('mine').slots.slice(0, 4)).toEqual(['f1', null, null, 'new1']);
    expect(m.nameOf('new1')).toBe('f2 copy');
    expect(m.nameOf('f1')).toBe('Brown Sound');
    const plan = m.plan();
    expect(plan.copies.map(c => c.source)).toEqual(['f2']);
    expect(plan.banks.map(b => b.id)).toEqual(['mine']);
    expect(plan.deletes).toEqual([]);
    expect(m.isFactoryBank('factory')).toBe(true);
    expect(m.isFactory('a')).toBe(false);
  });

  it('takes a pending copy\'s metadata with it when the copy is deleted', () => {
    const m = session();
    m.copy({ bank: 'one', slot: 0 }, { bank: 'two', slot: 1 });
    m.setMeta('new1', { color: 'limegreen' });
    expect(m.plan().meta.map(([id]) => id)).toEqual(['new1']);

    m.delete(['new1']);

    expect(m.plan().meta).toEqual([]); // nothing to name or colour: the copy was never created
    expect(m.plan().copies).toEqual([]);
  });

  it('puts its own slot changes on a bank another editor has stored since, and keeps the other slots as stored', () => {
    const m = new ManageSession([revised(['a', 'b', 'c'])], [preset('a'), preset('b'), preset('c'), preset('d')]);
    m.swap({ bank: 'one', slot: 0 }, { bank: 'one', slot: 1 }); // this page: b, a, c

    // Meanwhile another editor renamed the bank, replaced slot 2 and filled slot 5.
    m.rebaseBank({ id: 'one', name: 'Gig', slots: ['a', 'b', 'd', null, null, 'c'], revision: 6 } as unknown as Bank);

    const bank = m.plan().banks[0];
    expect(bank?.slots).toEqual(['b', 'a', 'd', null, null, 'c']);
    expect([bank?.name, bank?.revision]).toEqual(['Gig', 6]);
    expect(m.isChanged({ bank: 'one', slot: 0 })).toBe(true); // still to be saved
    expect(m.isChanged({ bank: 'one', slot: 2 })).toBe(false);
  });

  it('takes the revision of its own rename only when its working copy was made from the bank that was renamed', () => {
    const m = new ManageSession([revised(['a'])], [preset('a')]);

    m.bankSaved('one', 4, 5);
    expect(m.bank('one').revision).toBe(5);

    // The stored bank was at 7 by the time of the next rename (another editor): Save must still be refused.
    m.bankSaved('one', 7, 8);
    expect(m.bank('one').revision).toBe(5);

    m.bankStored('one', 9);
    expect(m.bank('one').revision).toBe(9);
  });
});
