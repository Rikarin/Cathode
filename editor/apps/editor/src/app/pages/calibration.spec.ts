import { dbuFromFullScale, fullScaleFromDbu, fullScaleFromReference } from './calibration';

describe('input calibration', () => {
  it('turns a documented maximum input level into peak volts at full scale', () => {
    // +4 dBu is 1.228 V RMS, 1.736 V peak; 1 V peak (Cathode's default) is -0.79 dBu.
    expect(fullScaleFromDbu(4)).toBeCloseTo(1.736, 3);
    expect(fullScaleFromDbu(dbuFromFullScale(2.5))).toBeCloseTo(2.5, 9);
    expect(dbuFromFullScale(1)).toBeCloseTo(-0.79, 2);
  });

  it('measures full scale from a known reference sine', () => {
    // 0.5 V RMS reads half scale: full scale is 0.5 · √2 / 0.5 V peak.
    expect(fullScaleFromReference(0.5, 0.5)).toBeCloseTo(Math.SQRT2, 9);
    expect(fullScaleFromReference(0.5, 0)).toBeNull();
  });
});
