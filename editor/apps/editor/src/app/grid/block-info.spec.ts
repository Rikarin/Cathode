import type { BlockTypeInfo, PresetBlock } from '@cathode/api-client';
import { abbreviate, ampsInUse } from './block-info';
import { type CopiedBlock, dropAction, pasteAction } from './grid-editor';

const block = (id: string, type: string, channels: PresetBlock['channels'] = {}): PresetBlock => ({
  id,
  type,
  row: 0,
  column: 1,
  channels,
  assets: {},
  bypassMode: 'thru'
});

describe('Block Info', () => {
  it('abbreviates block names', () => {
    expect(abbreviate('Drive', 'drive1')).toBe('DRV');
    expect(abbreviate('Drive', 'drive2')).toBe('DRV2');
    expect(abbreviate('Graphic EQ')).toBe('GEQ');
    expect(abbreviate('Amp (circuit)', 'circuitamp1')).toBe('AMP');
    expect(abbreviate('Multitap Delay')).toBe('MD');
    expect(abbreviate('RTA')).toBe('RTA');
  });

  it('sums up the amps in use: model per channel and the scenes on it', () => {
    const info = (type: string): BlockTypeInfo | undefined =>
      type === 'circuitamp'
        ? {
            typeId: type,
            name: 'Amp (circuit)',
            category: 'Amp',
            maxInstances: 2,
            channels: 4,
            assetSlots: [],
            cpuCost: 0,
            params: [
              {
                id: 'type',
                name: 'Type',
                kind: 'enum',
                min: 0,
                max: 2,
                default: 0,
                unit: '',
                taper: 'linear',
                options: ['Blackface', 'Plexi', 'AC30']
              }
            ]
          }
        : {
            typeId: type,
            name: type,
            category: 'Delay',
            maxInstances: 2,
            channels: 4,
            assetSlots: [],
            cpuCost: 0,
            params: []
          };
    const scenes = Array.from({ length: 8 }, (_, i) => ({
      name: `Scene ${i + 1}`,
      level: 0,
      controllers: [],
      midi: [],
      blocks: i >= 4 ? { circuitamp1: { bypassed: false, channel: 'B' } } : {}
    }));
    const amps = ampsInUse(
      {
        blocks: [block('circuitamp1', 'circuitamp', { B: { type: 1 } }), block('delay1', 'delay')],
        scenes
      } as never,
      info
    );

    expect(amps).toEqual([
      {
        id: 'circuitamp1',
        type: 'circuitamp',
        models: [
          { channel: 'A', model: 'Blackface', scenes: [1, 2, 3, 4] },
          { channel: 'B', model: 'Plexi', scenes: [5, 6, 7, 8] }
        ]
      }
    ]);
  });
});

describe('grid operations', () => {
  const copied: CopiedBlock = { type: 'delay', channels: {}, assets: {} };

  it('pastes a block on an empty cell or a shunt, and settings onto a block of its type', () => {
    expect(pasteAction(copied, null)).toBe('place');
    expect(pasteAction(copied, block('shunt1', 'shunt'))).toBe('place');
    expect(pasteAction(copied, block('delay2', 'delay'))).toBe('settings');
    expect(pasteAction(copied, block('drive1', 'drive'))).toBeNull();
    expect(pasteAction(null, null)).toBeNull();
  });

  it('drops a block to move, copy (⌥), swap, or replace (⌘)', () => {
    const none = { altKey: false, metaKey: false, ctrlKey: false };
    const drive = block('drive1', 'drive');
    expect(dropAction(drive, null, none)).toBe('move');
    expect(dropAction(drive, null, { ...none, altKey: true })).toBe('copy');
    expect(dropAction(drive, block('delay1', 'delay'), none)).toBe('swap');
    expect(dropAction(drive, block('delay1', 'delay'), { ...none, metaKey: true })).toBe('replace');
    expect(dropAction(drive, block('out1', 'output'), { ...none, metaKey: true })).toBeNull();
    expect(dropAction(block('in1', 'input'), null, { ...none, altKey: true })).toBe('move');
    expect(dropAction(drive, drive, none)).toBeNull();
  });
});
