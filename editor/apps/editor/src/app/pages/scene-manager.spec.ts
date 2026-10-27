import type { Preset } from '@cathode/api-client';
import { sceneRows } from './scene-manager';

describe('sceneRows', () => {
  it('lists every block but shunts in grid order with its state in each scene', () => {
    const scenes = Array.from({ length: 8 }, (_, i) => ({
      name: `Scene ${i + 1}`,
      level: 0,
      blocks: i === 2 ? { delay1: { bypassed: true, channel: 'B' } } : {},
      controllers: [0, 0, 0, 0],
      midi: []
    }));
    const preset = {
      blocks: [
        { id: 'out1', type: 'output', row: 0, column: 13, sceneIgnore: [] },
        { id: 'delay1', type: 'delay', row: 0, column: 2, sceneIgnore: ['A'] },
        { id: 'shunt-r0c1', type: 'shunt', row: 0, column: 1 },
        { id: 'in1', type: 'input', row: 0, column: 0 }
      ],
      scenes
    } as unknown as Preset;
    const info = (type: string) =>
      ({
        input: { name: 'Input', channels: 4 },
        output: { name: 'Output', channels: 4 },
        delay: { name: 'Delay', channels: 4 }
      })[type];

    const rows = sceneRows(preset, info);

    expect(rows.map(r => r.id)).toEqual(['in1', 'delay1', 'out1']);
    expect(rows[0]?.bypassable).toBe(false);
    expect(rows[1]?.bypassable).toBe(true);
    expect(rows[1]?.ignores).toEqual(['A']);
    expect(rows[1]?.channels).toEqual(['A', 'B', 'C', 'D']);
    expect(rows[1]?.states[2]).toEqual({ bypassed: true, channel: 'B' });
    expect(rows[1]?.states[0]).toEqual({ bypassed: false, channel: 'A' });
  });
});
