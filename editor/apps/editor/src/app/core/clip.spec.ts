import type { Telemetry } from '@cathode/api-client';
import { ClipHold, InputClipLevel, OutputClipLevel, clippedOutputs } from './clip';

const frame = (left: number, right: number, outputPeaks?: number[]): Telemetry => ({
  type: 'telemetry',
  inputPeak: 0,
  outputPeakLeft: left,
  outputPeakRight: right,
  cpuP999: 0,
  overloads: 0,
  tempo: 120,
  outputPeaks
});

describe('clip warnings', () => {
  it('warns within half a decibel of full scale at the input and at -1 dBFS at the outputs', () => {
    expect(20 * Math.log10(InputClipLevel)).toBeCloseTo(-0.5, 6);
    expect(20 * Math.log10(OutputClipLevel)).toBeCloseTo(-1, 6);
  });

  it('names each output that clipped', () => {
    expect(clippedOutputs(frame(0.5, 0.95))).toEqual([1]);
    expect(clippedOutputs(frame(0.5, 0.5, [0.5, 0.2, 0.9, 1.2]))).toEqual([3, 4]);
    expect(clippedOutputs(frame(0.88, 0.88, [0.88, 0.88, 0, 0]))).toEqual([]);
    expect(clippedOutputs(null)).toEqual([]);
  });

  it('holds a warning after the last over', () => {
    const hold = new ClipHold(1_000);
    expect(hold.update(false, 0)).toBe(false);
    expect(hold.update(true, 100)).toBe(true);
    expect(hold.update(false, 900)).toBe(true);
    expect(hold.update(false, 1_200)).toBe(false);
  });
});
