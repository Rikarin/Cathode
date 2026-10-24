import { scopeName } from './controllers-page';

describe('scopeName', () => {
  it('names a modifier scope', () => {
    expect(scopeName(null)).toBe('All channels');
    expect(scopeName('all')).toBe('All channels');
    expect(scopeName('B')).toBe('Channel B only');
  });
});
