import { refreshesGlobals } from './global.store';

describe('refreshesGlobals', () => {
  it('refetches after any change to the global settings, whatever made it', () => {
    expect(refreshesGlobals({ type: 'globalChanged', revision: 3 })).toBe(true);
    expect(refreshesGlobals({ type: 'tempoChanged', revision: 4 })).toBe(true);
    expect(refreshesGlobals({ type: 'globalLinked', revision: 5, block: 'amp1' })).toBe(true);
  });

  it('leaves the global settings alone for edits of the working copy', () => {
    expect(refreshesGlobals({ type: 'paramChanged', revision: 6, block: 'amp1' })).toBe(false);
    expect(refreshesGlobals({ type: 'sceneChanged', revision: 7, scene: 1 })).toBe(false);
  });
});
