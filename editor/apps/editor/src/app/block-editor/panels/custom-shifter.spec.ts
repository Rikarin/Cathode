import { customParamId, customScaleNotes, isCustomTableParam, noteAbove } from './custom-shifter';

describe('custom shifter', () => {
  it('names the table parameters as the engine does', () => {
    expect(customParamId(1, 0)).toBe('custom1C');
    expect(customParamId(2, 1)).toBe('custom2Cs');
    expect(customParamId(4, 11)).toBe('custom4B');
  });

  it('recognises table parameters and nothing else', () => {
    expect(isCustomTableParam('custom3Fs')).toBe(true);
    expect(isCustomTableParam('custom1A')).toBe(true);
    expect(isCustomTableParam('voice3Shift')).toBe(false);
    expect(isCustomTableParam('custom5C')).toBe(false);
    expect(isCustomTableParam('keySource')).toBe(false);
  });

  it('names the note a shift lands on', () => {
    expect(noteAbove(9, 0)).toBe('A');
    expect(noteAbove(9, 3)).toBe('C');
    expect(noteAbove(0, -1)).toBe('B');
    expect(noteAbove(4, 24)).toBe('E');
  });
});

describe('custom scale', () => {
  it('names the notes in the tonic, rising, with the root and without repeats', () => {
    expect(customScaleNotes(0, 7, [0, 2, 4, 5, 7, 9, 11, 11])).toEqual(['C', 'D', 'E', 'F', 'G', 'A', 'B']);
    expect(customScaleNotes(9, 4, [7, 3, 3, 10, 1])).toEqual(['A', 'C', 'E', 'G']);
  });
});
