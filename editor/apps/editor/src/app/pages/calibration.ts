/** 0 dBu: 1 mW into 600 Ω, 0.7746 V RMS. */
const DbuVolts = Math.sqrt(0.6);

/**
 * The peak volts of full scale for an interface whose documented maximum input level is `dbu` (the level of a sine that
 * just reaches 0 dBFS; data sheets give it in dBu, an RMS level).
 */
export function fullScaleFromDbu(dbu: number): number {
  return DbuVolts * Math.SQRT2 * 10 ** (dbu / 20);
}

/** The maximum input level in dBu of a full scale of `volts` peak. */
export function dbuFromFullScale(volts: number): number {
  return 20 * Math.log10(volts / (DbuVolts * Math.SQRT2));
}

/**
 * Full scale from a measurement: a sine of `referenceRms` volts RMS played into the input read `peak` (0..1 of full
 * scale) on the meter.
 */
export function fullScaleFromReference(referenceRms: number, peak: number): number | null {
  return peak > 0 && referenceRms > 0 ? (referenceRms * Math.SQRT2) / peak : null;
}

/** The global settings' calibration range: −30 to +40 dBV. */
export const FullScaleRange = { min: 0.0316, max: 100 } as const;
