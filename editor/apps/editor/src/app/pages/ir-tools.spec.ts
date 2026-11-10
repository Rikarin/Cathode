import { CaptureRanges, captureValue } from './ir-tools';

describe('captureValue', () => {
  it('takes a sweep level of 0 dBFS, the top of the range', () => {
    expect(captureValue(0, CaptureRanges.levelDb, -18)).toBe(0);
  });

  it('limits a typed number to the range of its field', () => {
    expect(captureValue(600, CaptureRanges.seconds, 5)).toBe(30);
    expect(captureValue(0, CaptureRanges.seconds, 5)).toBe(1);
    expect(captureValue(20, CaptureRanges.levelDb, -18)).toBe(0);
    expect(captureValue(-90, CaptureRanges.levelDb, -18)).toBe(-60);
    expect(captureValue(1, CaptureRanges.lengthMs, 500)).toBe(20);
    expect(captureValue(5000, CaptureRanges.lengthMs, 500)).toBe(2000);
  });

  it('keeps a number within the range as typed', () => {
    expect(captureValue(2.5, CaptureRanges.seconds, 5)).toBe(2.5);
    expect(captureValue(-12, CaptureRanges.levelDb, -18)).toBe(-12);
  });

  it('keeps the current value when the field holds no number', () => {
    expect(captureValue(Number.NaN, CaptureRanges.levelDb, -6)).toBe(-6);
    expect(captureValue(Number.NaN, CaptureRanges.seconds, 5)).toBe(5);
  });
});
