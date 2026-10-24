/** The sweep range of the cab resonance helper, in hertz: where guitar cabinets resonate. */
export const SweepRange = { min: 40, max: 250 } as const;

/** Load resonance's range on the Circuit Amp, in percent of the model's speaker resonance. */
export const ResonancePercentRange = { min: 25, max: 400 } as const;

/** A 0..1 slider position on a logarithmic scale across {@link SweepRange}. */
export function sweepFrequency(position: number): number {
  const p = Math.min(1, Math.max(0, position));
  return SweepRange.min * (SweepRange.max / SweepRange.min) ** p;
}

export function sweepPosition(frequency: number): number {
  const f = Math.min(SweepRange.max, Math.max(SweepRange.min, frequency));
  return Math.log(f / SweepRange.min) / Math.log(SweepRange.max / SweepRange.min);
}

/** The Load resonance (%) that puts the amp's speaker resonance at `frequency`, for a model resonating at `baseHz`. */
export function resonancePercent(frequency: number, baseHz: number): number {
  const percent = (frequency / baseHz) * 100;
  return Math.round(Math.min(ResonancePercentRange.max, Math.max(ResonancePercentRange.min, percent)) * 10) / 10;
}
