import type { Telemetry } from '@cathode/api-client';
import { blockMeter } from './events';

describe('blockMeter', () => {
  const frame = {
    type: 'telemetry',
    blocks: { in1: { peak: 0.8 }, cab1: { peak: 0.4, preampIn: -6, motorReduction: 1.5 } }
  } as unknown as Telemetry;

  it('reads a block peak and its own meters from the one blocks section', () => {
    expect(blockMeter(frame, 'in1', 'peak')).toBe(0.8);
    expect(blockMeter(frame, 'cab1', 'peak')).toBe(0.4);
    expect(blockMeter(frame, 'cab1', 'preampIn')).toBe(-6);
    expect(blockMeter(frame, 'cab1', 'motorReduction')).toBe(1.5);
  });

  it('is undefined before a frame, for an unknown block and for a meter the block does not have', () => {
    expect(blockMeter(null, 'cab1', 'peak')).toBeUndefined();
    expect(blockMeter(frame, 'delay1', 'peak')).toBeUndefined();
    expect(blockMeter(frame, 'in1', 'preampIn')).toBeUndefined();
  });
});
