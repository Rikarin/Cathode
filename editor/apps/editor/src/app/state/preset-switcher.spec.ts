import { needsConfirm, pushRecent } from './preset-switcher';

describe('recent presets', () => {
  it('puts the latest first, once, and keeps the list short', () => {
    let list = pushRecent([], { id: 'a', name: 'A' });
    list = pushRecent(list, { id: 'b', name: 'B' });
    list = pushRecent(list, { id: 'a', name: 'A renamed' });

    expect(list).toEqual([
      { id: 'a', name: 'A renamed' },
      { id: 'b', name: 'B' }
    ]);
    const long = Array.from({ length: 12 }, (_, i) => ({ id: `p${i}`, name: `P${i}` })).reduce<
      { id: string; name: string }[]
    >((all, p) => pushRecent(all, p), []);
    expect(long).toHaveLength(8);
    expect(long[0]?.id).toBe('p11');
  });
});

describe('confirming a preset change', () => {
  it('asks only with unsaved edits, unless turned off', () => {
    expect(needsConfirm(true, undefined)).toBe(true);
    expect(needsConfirm(true, true)).toBe(true);
    expect(needsConfirm(true, false)).toBe(false);
    expect(needsConfirm(false, true)).toBe(false);
    expect(needsConfirm(undefined, true)).toBe(false);
  });
});
