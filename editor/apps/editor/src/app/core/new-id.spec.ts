import { newId } from './new-id';

describe('newId', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('makes 32 hex digits, different each time', () => {
    const ids = Array.from({ length: 50 }, newId);
    for (const id of ids) {
      expect(id).toMatch(/^[0-9a-f]{32}$/);
    }
    expect(new Set(ids).size).toBe(ids.length);
  });

  it('works where crypto.randomUUID does not exist (plain http on the LAN)', () => {
    // An insecure context has crypto.getRandomValues and nothing else of use here.
    vi.stubGlobal('crypto', { getRandomValues: crypto.getRandomValues.bind(crypto) });
    expect((crypto as Partial<Crypto>).randomUUID).toBeUndefined();

    expect(newId()).toMatch(/^[0-9a-f]{32}$/);
  });
});
