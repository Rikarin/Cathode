import type { PresetBlock, PresetCable } from '@cathode/api-client';
import { monoHintText, monoHints, sumsToMono } from './mono-hint';

const block = (
  id: string,
  type: string,
  column: number,
  row = 0,
  params: Record<string, number> = {}
): PresetBlock => ({
  id,
  type,
  row,
  column,
  channels: { A: params },
  assets: {},
  bypassMode: 'thru',
  sceneIgnore: []
});
const cable = (from: string, to: string): PresetCable => ({ from, to });
const defaults: Record<string, number> = { input: 3, inputSelect: 0, mode: 0 };
const param = (b: PresetBlock, id: string) =>
  b.channels['A']?.[id] ?? (b.type === 'irplayer' && id === 'input' ? 0 : (defaults[id] ?? 0));

describe('mono hint', () => {
  it('knows which blocks sum to mono', () => {
    expect(sumsToMono(block('a', 'circuitamp', 1), param)).toBe(true);
    expect(sumsToMono(block('c', 'cab', 1), param)).toBe(false); // a stereo cab
    expect(sumsToMono(block('c', 'cab', 1, 0, { input: 2 }), param)).toBe(true);
    expect(sumsToMono(block('d', 'drive', 1), param)).toBe(false); // left and right through their own circuits
    expect(sumsToMono(block('m', 'mixer', 1, 0, { mode: 1 }), param)).toBe(true);
  });

  it('marks a mono block with the engaged stereo effects ahead of it on its path', () => {
    const blocks = [
      block('in1', 'input', 0),
      block('chorus1', 'chorus', 1),
      block('delay1', 'delay', 2),
      block('amp1', 'circuitamp', 3),
      block('reverb1', 'reverb', 4),
      block('out1', 'output', 5),
      block('flanger1', 'flanger', 2, 1) // another row, not on the amp's path
    ];
    const cables = [
      cable('in1', 'chorus1'),
      cable('chorus1', 'delay1'),
      cable('delay1', 'amp1'),
      cable('amp1', 'reverb1'),
      cable('reverb1', 'out1')
    ];

    const hints = monoHints(blocks, cables, param, b => b.id === 'delay1');

    expect([...hints.keys()]).toEqual(['amp1']);
    const amp = hints.get('amp1');
    if (!amp) {
      throw new Error('No hint on amp1');
    }

    expect(amp.map(b => b.id)).toEqual(['chorus1']); // the delay is bypassed
    expect(monoHintText(amp, b => b.id)).toContain('chorus1');
  });

  it('says nothing when the stereo effects come after the mono block', () => {
    const blocks = [block('in1', 'input', 0), block('amp1', 'circuitamp', 1), block('chorus1', 'chorus', 2)];
    const hints = monoHints(blocks, [cable('in1', 'amp1'), cable('amp1', 'chorus1')], param, () => false);

    expect(hints.size).toBe(0);
  });
});
