/**
 * The Compressor's transfer curve, ported from the engine's gain computer (`CompressorCurve` in
 * engine/src/Cathode.Dsp/Blocks/CompressorCurve.cs, which the block's audio path calls on every sample). The
 * fixtures next to this file are the engine's own values, written by its `CompressorCurveTests`; the spec pins this
 * port to them, and the engine test fails when the engine no longer matches them, so the two cannot drift apart.
 * Levels are dB as the gain computer reads them: after Input Level's offset and the sidechain filters.
 */

/** The Compressor's types, as the engine's `CompressorBlock.Pedal` … `Compander`. */
export const CompressorType = {
  pedal: 0,
  studio: 1,
  opto: 2,
  sustainer: 3,
  dynamics: 4,
  feedback: 5,
  jfet: 6,
  tube: 7,
  compander: 8
} as const;

/** A ratio at or above this is ∞:1 (`CompressorBlock.InfiniteRatio`). */
const InfiniteRatio = 100;
/** The most the Sustainer and the Dynamics type raise a signal, dB (`MaxSustainDb`). */
const MaxSustainDb = 24;
/** The Tube type's narrowest knee, dB (`TubeKneeDb`). */
const TubeKneeDb = 18;
/** What Input Level Line takes off the detector, dB (`LineLevelDb`). */
const LineLevelDb = 12;
/** The deepest the Compander's expander goes, dB (`ExpanderFloorDb`). */
const ExpanderFloorDb = -80;

/** The parameters the curve depends on, by the Compressor's parameter ids; read with their defaults filled in. */
export const CompressorCurveParams = [
  'type',
  'threshold',
  'ratio',
  'knee',
  'makeup',
  'autoMakeup',
  'inputLevel',
  'controls',
  'compression',
  'dynamics',
  'expThreshold',
  'expRatio'
] as const;

export type CompressorValues = Record<(typeof CompressorCurveParams)[number], number>;

/** The gain computer's settings, resolved for the type and the Controls mode (`CompressorCurve`'s fields). */
export interface CompressorCurve {
  type: number;
  threshold: number;
  ratio: number;
  knee: number;
  makeup: number;
  expanderThreshold: number;
  expanderSlope: number;
}

/** `CompressorBlock.StaticCurve`: output for input `x`, dB; soft knee, ∞:1 at {@link InfiniteRatio}, below 1 expands. */
export function staticCurve(x: number, threshold: number, ratio: number, knee: number): number {
  const over = x - threshold;
  if (2 * over < -knee) {
    return x;
  }

  const infinite = ratio >= InfiniteRatio;
  if (knee > 0 && 2 * Math.abs(over) <= knee) {
    const t = over + knee / 2;
    return x + ((infinite ? -1 : 1 / ratio - 1) * t * t) / (2 * knee);
  }

  return infinite ? threshold : threshold + over / ratio;
}

/** `CompressorBlock.SingleKnob`: the Compression knob (0–100 %) as a threshold and a ratio. */
export function singleKnob(compression: number): { threshold: number; ratio: number } {
  const c = Math.min(1, Math.max(0, compression / 100));
  return { threshold: -6 - 44 * c, ratio: 1 + 19 * c * c };
}

/** `CompressorBlock.DynamicsRatio`: the Dynamics type's ratio for its Dynamics knob (-100..100 %). */
export function dynamicsRatio(dynamics: number): number {
  const slope = 1 - dynamics / 100;
  return slope <= 1 / InfiniteRatio ? InfiniteRatio : 1 / slope;
}

/** `CompressorBlock.AutoMakeupDb`: half the reduction a 0 dBFS signal gets (read `lineDb` lower); none on the Sustainer. */
export function autoMakeupDb(type: number, threshold: number, ratio: number, knee: number, lineDb = 0): number {
  if (type === CompressorType.sustainer) {
    return 0;
  }

  const level = -lineDb;
  return Math.max(0, -0.5 * (staticCurve(level, threshold, ratio, knee) - level));
}

/** `CompressorCurve.Of`: the curve for a block's parameter values. */
export function compressorCurve(values: CompressorValues): CompressorCurve {
  const type = Math.trunc(values.type);
  let { threshold, ratio } = values;
  if (values.controls >= 0.5) {
    ({ threshold, ratio } = singleKnob(values.compression));
  }

  if (type === CompressorType.dynamics) {
    ratio = dynamicsRatio(values.dynamics);
  }

  const knee = type === CompressorType.tube ? Math.max(values.knee, TubeKneeDb) : values.knee;
  let makeup = values.makeup;
  if (values.autoMakeup >= 0.5) {
    makeup += autoMakeupDb(type, threshold, ratio, knee, values.inputLevel >= 0.5 ? LineLevelDb : 0);
  }

  return {
    type,
    threshold,
    ratio,
    knee,
    makeup,
    expanderThreshold: values.expThreshold,
    expanderSlope: values.expRatio - 1
  };
}

/** `CompressorCurve.Target`: the compressor's steady gain for `level`, dB (makeup and the expander apart). */
export function compressorTarget(curve: CompressorCurve, level: number): number {
  const { type, threshold, ratio, knee } = curve;
  if (type === CompressorType.sustainer) {
    return Math.min(MaxSustainDb, -staticCurve(-level, -threshold, ratio, knee) - level);
  }

  const target = staticCurve(level, threshold, ratio, knee) - level;
  return type === CompressorType.dynamics && ratio < 1 ? Math.min(MaxSustainDb, target) : target;
}

/** `CompressorCurve.Expansion`: the Compander's expander gain for `level`, dB; 0 on the other types. */
export function compressorExpansion(curve: CompressorCurve, level: number): number {
  return curve.type === CompressorType.compander
    ? Math.max(ExpanderFloorDb, Math.min(0, curve.expanderSlope * (level - curve.expanderThreshold)))
    : 0;
}

/** Whether the detector hears the output (Feedback, JFET, Tube): their curve is where the gain settles. */
export function isFeedback(type: number): boolean {
  return type === CompressorType.feedback || type === CompressorType.jfet || type === CompressorType.tube;
}

/**
 * `CompressorCurve.Output`: the level a steady `input` comes out at, dB, makeup included. On the feedback types the
 * gain r settles where r = target(input + r), found by bisection as the engine does.
 */
export function compressorOutput(curve: CompressorCurve, input: number): number {
  let gain: number;
  if (isFeedback(curve.type)) {
    let low = Math.min(0, compressorTarget(curve, input));
    let high = 0;
    for (let i = 0; i < 40 && high - low > 1e-5; i++) {
      const middle = 0.5 * (low + high);
      if (compressorTarget(curve, input + middle) < middle) {
        high = middle;
      } else {
        low = middle;
      }
    }

    gain = 0.5 * (low + high);
  } else {
    gain = compressorTarget(curve, input) + compressorExpansion(curve, input);
  }

  return input + gain + curve.makeup;
}
