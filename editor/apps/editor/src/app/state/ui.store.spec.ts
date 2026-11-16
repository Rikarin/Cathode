import { activeViewAfterRemoving } from '../pages/global-page';
import { isBuiltIn } from './ui.store';

describe('the grid helpers', () => {
  it('counts every Output as a built-in block, as the Server does (#633)', () => {
    for (const type of ['input', 'shunt', 'output', 'output2', 'output3', 'output4']) {
      expect(isBuiltIn(type)).toBe(true);
    }

    expect(isBuiltIn('amp')).toBe(false);
  });

  it('keeps the active footswitch view when another one is removed (#631)', () => {
    expect(activeViewAfterRemoving(2, 0)).toBe(1); // A, B, C with C active; A goes: C is now at 1
    expect(activeViewAfterRemoving(0, 2)).toBe(0); // a later view goes
    expect(activeViewAfterRemoving(1, 1)).toBe(0); // the active one goes: back to the first
  });
});
