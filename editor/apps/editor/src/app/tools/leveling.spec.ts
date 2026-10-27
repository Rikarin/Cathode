import { LoudnessIntegrator, levelTarget, suggestLevel } from './leveling';

describe('LoudnessIntegrator', () => {
  it('averages power, not decibels, and ignores silence and pauses', () => {
    const meter = new LoudnessIntegrator();
    expect(meter.integrated()).toBeNull();

    // Equal time at -20 and -14 LUFS: the power average is about -16 LUFS, not the -17 a dB average gives.
    for (let i = 0; i < 50; i++) {
      meter.add(-20);
      meter.add(-14);
    }

    const playing = meter.integrated();
    if (playing === null) {
      throw new Error('No integrated loudness');
    }

    expect(playing).toBeCloseTo(-16.03, 1);

    // Silence never counts, and a long quiet tail more than 10 LU down is gated out.
    for (let i = 0; i < 200; i++) {
      meter.add(-120);
      meter.add(-40);
    }

    expect(meter.integrated()).toBeCloseTo(playing, 6);
    meter.add(undefined);
    meter.reset();
    expect(meter.count).toBe(0);
  });
});

describe('levelTarget', () => {
  it('prefers an amp with a level, then the output', () => {
    const blocks = [
      { id: 'in1', type: 'input' },
      { id: 'drive1', type: 'drive' },
      { id: 'amp1', type: 'circuitamp' },
      { id: 'out1', type: 'output' }
    ];

    expect(levelTarget(blocks, () => true)?.block).toBe('amp1');
    expect(levelTarget(blocks, t => t !== 'circuitamp')?.block).toBe('out1');
    expect(levelTarget([{ id: 'in1', type: 'input' }], () => true)).toBeNull();
  });
});

describe('suggestLevel', () => {
  it('moves the level by the loudness difference, within its range', () => {
    expect(suggestLevel(-18, -23, 0, -80, 20)).toEqual({ change: -5, next: -5, clamped: false });
    expect(suggestLevel(-30, -23, 2.5, -80, 20)).toEqual({ change: 7, next: 9.5, clamped: false });
    expect(suggestLevel(-50, -23, 10, -80, 20)).toEqual({ change: 10, next: 20, clamped: true });
  });
});
