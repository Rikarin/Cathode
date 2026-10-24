import { SweepRange, resonancePercent, sweepFrequency, sweepPosition } from './cab-resonance';

describe('cab resonance helper', () => {
  it('sweeps logarithmically across the cabinet range', () => {
    expect(sweepFrequency(0)).toBeCloseTo(SweepRange.min, 6);
    expect(sweepFrequency(1)).toBeCloseTo(SweepRange.max, 6);
    expect(sweepFrequency(0.5)).toBeCloseTo(Math.sqrt(SweepRange.min * SweepRange.max), 6);
    expect(sweepPosition(sweepFrequency(0.3))).toBeCloseTo(0.3, 6);
  });

  it('turns the found frequency into Load resonance in percent of the model', () => {
    expect(resonancePercent(120, 100)).toBe(120);
    expect(resonancePercent(85, 105)).toBe(81);
    expect(resonancePercent(10, 100)).toBe(25); // clamped to the parameter's range
    expect(resonancePercent(900, 100)).toBe(400);
  });
});
