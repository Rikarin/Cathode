import { keyNames } from './platform';

describe('keyNames', () => {
  it('names the modifiers as a Mac keyboard prints them', () => {
    expect(keyNames(true)).toEqual({ mod: '⌘', alt: '⌥', shift: '⇧' });
  });

  it('names them in words elsewhere, where no key carries those symbols', () => {
    expect(keyNames(false)).toEqual({ mod: 'Ctrl', alt: 'Alt', shift: 'Shift' });
  });
});
