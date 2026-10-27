import { sectionDb } from '../../dsp/response';
import { cutDb } from './cab';

/** Band 1 and 5 types, as the engine's `ParametricEqBlock.Shelf` … `Peak2`. */
export const PeqType = { shelf: 0, peak: 1, blocking: 2, shelf2: 3, peak2: 4 } as const;

/** Index of 12 dB/oct in the slope choices (the engine's `CutFilter.Slope12`). */
const Slope12 = 1;

/** One band as the panel draws it. */
export interface PeqBand {
  band: number;
  type: number;
  frequency: number;
  gain: number;
  q: number;
  slope: number;
}

/** The variable-Q law of the engine's `GraphicEqualizer.VariableQ`: 40 % of Q near 0 dB, all of it at ±12 dB. */
export const variableQ = (q: number, gainDb: number): number => q * (0.4 + 0.6 * Math.min(1, Math.abs(gainDb) / 12));

/**
 * The response in dB of one PEQ band at `frequency`, as the engine computes it: shelves, peaks, blocking cuts (the
 * band's Q at 12 dB/oct, Butterworth at the other slopes), the first-order Shelving 2 and the proportional-Q Peaking 2.
 * Bands 2–4 are always peaks.
 */
export function peqBandDb(b: PeqBand, frequency: number, sampleRate = 48_000): number {
  const outer = b.band === 1 || b.band === 5;
  const low = b.band === 1;
  const type = outer ? Math.round(b.type) : PeqType.peak;
  switch (type) {
    case PeqType.shelf:
      return sectionDb(low ? 'lowshelf' : 'highshelf', b.frequency, b.gain, b.q, frequency, sampleRate);
    case PeqType.blocking:
      return Math.round(b.slope) === Slope12
        ? sectionDb(low ? 'highpass' : 'lowpass', b.frequency, 0, b.q, frequency, sampleRate)
        : cutDb(low, b.frequency, b.slope, frequency, sampleRate);
    case PeqType.shelf2: {
      // (s + w G) / (s + w) with w = f / sqrt(G) for a low shelf; (G s + w) / (s + w) with w = f sqrt(G) for a high.
      const g = 10 ** (b.gain / 20);
      const pole = low ? b.frequency / Math.sqrt(g) : b.frequency * Math.sqrt(g);
      const f2 = frequency * frequency;
      const p2 = pole * pole;
      return low ? 10 * Math.log10((f2 + p2 * g * g) / (f2 + p2)) : 10 * Math.log10((g * g * f2 + p2) / (f2 + p2));
    }
    case PeqType.peak2:
      return sectionDb('peak', b.frequency, b.gain, variableQ(b.q, b.gain), frequency, sampleRate);
    default:
      return sectionDb('peak', b.frequency, b.gain, b.q, frequency, sampleRate);
  }
}

/** The whole EQ's response in dB at `frequency`: the bands' sum. */
export function peqTotalDb(bands: readonly PeqBand[], frequency: number): number {
  return bands.reduce((sum, b) => sum + peqBandDb(b, frequency), 0);
}

/**
 * The gain that makes the whole EQ read `targetDb` at `frequency` with band `band` moved there: the other bands' sum is
 * fixed, and a band's own dB at its frequency moves in step with its gain (a peak reads its gain, a shelf half of it).
 * Null for a band whose gain does nothing there (a cut).
 */
export function peqGainFor(
  bands: readonly PeqBand[],
  band: number,
  frequency: number,
  targetDb: number
): number | null {
  const found = bands.find(b => b.band === band);
  if (!found) {
    return null;
  }

  const own = { ...found, frequency };
  const others = peqTotalDb(
    bands.filter(b => b.band !== band),
    frequency
  );
  // How the band's own dB at its frequency moves with its gain (a cut reads -3 dB there whatever its gain).
  const flat = peqBandDb({ ...own, gain: 0 }, frequency);
  const perDb = (peqBandDb({ ...own, gain: 12 }, frequency) - flat) / 12;
  return Math.abs(perDb) < 0.05 ? null : (targetDb - others - flat) / perDb;
}
