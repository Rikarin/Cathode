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

  it('parses a value back in any unit the engine uses, as it is shown', () => {
    expect(parseValue('8 :1', ':1')).toBe(8);
    expect(parseValue('7 st', 'st')).toBe(7);
    expect(parseValue('-12st', 'st')).toBe(-12);
    expect(parseValue('0.5 x', 'x')).toBe(0.5);
    expect(parseValue('90 °', '°')).toBe(90);
    expect(parseValue('120 bpm', 'BPM')).toBe(120);
    expect(parseValue('500 ms', 's')).toBe(0.5);
    expect(parseValue('2 s', 's')).toBe(2);
    const shown: [number, string][] = [
      [1250, 'Hz'],
      [80, 'Hz'],
      [-6, 'dB'],
      [3, 'dB'],
      [350, 'ms'],
      [1500, 'ms'],
      [2.5, 's'],
      [42, '%'],
      [4, ':1'],
      [12, 'st'],
      [1.5, 'x'],
      [0.25, '']
    ];
    for (const [value, unit] of shown) {
      expect(parseValue(formatValue(value, unit), unit)).toBe(value);
    }
  });

  it('parses a decimal comma, a trailing point, a typographic minus and minus infinity', () => {
    expect(parseValue('2,5', 'dB')).toBe(2.5);
    expect(parseValue('2,5 kHz', 'Hz')).toBe(2500);
    expect(parseValue(',5', '')).toBe(0.5);
    expect(parseValue('5.', '%')).toBe(5);
    expect(parseValue('\u22126 dB', 'dB')).toBe(-6);
    expect(parseValue('-∞ dB', 'dB')).toBe(Number.NEGATIVE_INFINITY);
    expect(parseValue('-inf', 'dB')).toBe(Number.NEGATIVE_INFINITY);
  });

  it('does not guess at text with a second number in it, or with none', () => {
    expect(parseValue('1.2.3', 'dB')).toBeNull();
    expect(parseValue('4 5', '%')).toBeNull();
    expect(parseValue('', 'dB')).toBeNull();
    expect(parseValue('dB', 'dB')).toBeNull();
    expect(parseValue('–', 'dB')).toBeNull();
  });

  it('converts peaks to decibels with a floor', () => {
    expect(toDecibels(1)).toBe(0);
    expect(toDecibels(0.5)).toBeCloseTo(-6.02, 2);
    expect(toDecibels(0)).toBe(-120);
  });
});
