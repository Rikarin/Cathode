import { formatValue, fromNormal, parseValue, toDecibels, toNormal } from './values';

describe('values', () => {
  it('maps linear and log ranges both ways', () => {
    expect(toNormal(5, { min: 0, max: 10 })).toBe(0.5);
    expect(fromNormal(0.5, { min: 0, max: 10 })).toBe(5);
    // A log knob puts the geometric mean in the middle: 20 Hz..20 kHz centres on 632 Hz.
    expect(fromNormal(0.5, { min: 20, max: 20_000, taper: 'log' })).toBeCloseTo(632.46, 1);
    expect(toNormal(632.4555, { min: 20, max: 20_000, taper: 'log' })).toBeCloseTo(0.5, 5);
    expect(toNormal(-5, { min: 0, max: 10 })).toBe(0);
    expect(fromNormal(2, { min: 0, max: 10 })).toBe(10);
  });

  it('formats values for people', () => {
    expect(formatValue(1250, 'Hz')).toBe('1.25 kHz');
    expect(formatValue(12_500, 'Hz')).toBe('12.5 kHz');
    expect(formatValue(80, 'Hz')).toBe('80 Hz');
    expect(formatValue(-6, 'dB')).toBe('-6.0 dB');
    expect(formatValue(3, 'dB')).toBe('+3.0 dB');
    expect(formatValue(-120, 'dB')).toBe('-∞ dB');
    expect(formatValue(350, 'ms')).toBe('350 ms');
    expect(formatValue(1500, 'ms')).toBe('1.5 s');
    expect(formatValue(42.4, '%')).toBe('42 %');
  });

  it('parses typed values with units', () => {
    expect(parseValue('1.2k', 'Hz')).toBe(1200);
    expect(parseValue('1.2 kHz', 'Hz')).toBe(1200);
    expect(parseValue('-6', 'dB')).toBe(-6);
    expect(parseValue('2 s', 'ms')).toBe(2000);
    expect(parseValue('loud', 'dB')).toBeNull();
  });

  it('converts peaks to decibels with a floor', () => {
    expect(toDecibels(1)).toBe(0);
    expect(toDecibels(0.5)).toBeCloseTo(-6.02, 2);
    expect(toDecibels(0)).toBe(-120);
  });
});
