import { type CompressorValues, compressorCurve, compressorOutput } from './compressor-curve';
import fixtures from './compressor-curve.fixtures.json';

/** The Compressor's defaults, as the engine declares them; each case lists only what it changes. */
const Defaults: CompressorValues = fixtures.defaults;

describe('the Compressor curve', () => {
  // The engine's CompressorCurveTests write these from the gain computer the audio path runs.
  it.each(fixtures.cases.map(c => [c.name, c] as const))('matches the engine: %s', (_, fixture) => {
    const curve = compressorCurve({ ...Defaults, ...fixture.values });
    fixtures.levels.forEach((level, i) => {
      expect(compressorOutput(curve, level)).toBeCloseTo(fixture.output[i] ?? Number.NaN, 3);
    });
  });

  it('covers every type and the Compression knob', () => {
    const types = new Set(fixtures.cases.map(c => c.values.type));
    expect([...types].sort((a, b) => a - b)).toEqual([0, 1, 2, 3, 4, 5, 6, 7, 8]);
    expect(fixtures.cases.some(c => 'controls' in c.values)).toBe(true);
  });

  it('is unity under the threshold and the ratio over it, lifted by the makeup', () => {
    const curve = compressorCurve({ ...Defaults, threshold: -30, ratio: 3, knee: 0, makeup: 4 });
    expect(compressorOutput(curve, -40)).toBeCloseTo(-36, 6);
    expect(compressorOutput(curve, 0)).toBeCloseTo(-30 + 30 / 3 + 4, 6);
  });
});
