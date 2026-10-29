import { Chromatic, MaxStrings, type Tuning, Tunings, findTuning, strings, tuneTo } from './tuning';

/** The frequency `cents` away from MIDI note `midi`, against an A4 of `reference`. */
const hz = (midi: number, cents = 0, reference = 440) => reference * 2 ** ((midi - 69 + cents / 100) / 12);

function tuning(id: string): Tuning {
  const found = findTuning(id);
  if (!found) {
    throw new Error(`No tuning ${id}`);
  }

  return found;
}

describe('tunings', () => {
  it('are all well formed, with unique ids, and none has more strings than the offsets kept', () => {
    expect(new Set(Tunings.map(t => t.id)).size).toBe(Tunings.length);
    expect(findTuning(Chromatic)).toBeUndefined();
    for (const t of Tunings) {
      const all = strings(t);
      expect(all.length).toBeLessThanOrEqual(MaxStrings);
      // Highest first, each lower than the one before.
      all.slice(1).forEach((s, i) => expect(s.midi).toBeLessThan(all[i]?.midi ?? 0));
    }
  });

  it('number strings from the highest, with their notes and MIDI numbers', () => {
    expect(strings(tuning('standard')).map(s => `${s.number}:${s.note}${s.octave}=${s.midi}`)).toEqual([
      '1:E4=64',
      '2:B3=59',
      '3:G3=55',
      '4:D3=50',
      '5:A2=45',
      '6:E2=40'
    ]);
    expect(strings(tuning('halfStepDown'))[5]).toMatchObject({ note: 'Eb', octave: 2, midi: 39 });
    expect(strings(tuning('eightString'))[7]).toMatchObject({ number: 8, note: 'F#', octave: 1, midi: 30 });
    expect(strings(tuning('bass5'))[4]).toMatchObject({ number: 5, note: 'B', octave: 0, midi: 23 });
  });
});

describe('tuneTo', () => {
  const standard = tuning('standard');
  const dropD = tuning('dropD');

  it('in chromatic, reads the nearest note and its cents, as the engine does', () => {
    expect(tuneTo(440)).toEqual({ frequency: 440, note: 'A', octave: 4, cents: 0 });
    const r = tuneTo(hz(40, -12));
    expect(r).toMatchObject({ note: 'E', octave: 2 });
    expect(r.cents).toBeCloseTo(-12, 6);
    expect(r.string).toBeUndefined();
  });

  it('turns the octave at C, from either side', () => {
    // B3 is MIDI 59, C4 is 60: a C4 played 40 cents flat is still C4, one 60 cents flat is B3.
    expect(tuneTo(hz(60, -40))).toMatchObject({ note: 'C', octave: 4 });
    expect(tuneTo(hz(60, -60))).toMatchObject({ note: 'B', octave: 3 });
    expect(tuneTo(hz(59, 49)).octave).toBe(3);
    expect(tuneTo(hz(59, 51)).octave).toBe(4);
    // The lowest octaves: B0 (a 5-string bass) and C0.
    expect(tuneTo(hz(23))).toMatchObject({ note: 'B', octave: 0 });
    expect(tuneTo(hz(12, 3))).toMatchObject({ note: 'C', octave: 0 });
  });

  it('measures against the reference pitch', () => {
    expect(tuneTo(432, 432)).toMatchObject({ note: 'A', octave: 4, cents: 0 });
    // 440 Hz against A4 = 432 is about 31.8 cents sharp of A4.
    expect(tuneTo(440, 432).cents).toBeCloseTo(1200 * Math.log2(440 / 432), 6);
    // The strings of a tuning move with it.
    const low = tuneTo(hz(40, 0, 435), 435, standard);
    expect(low).toMatchObject({ string: 6, note: 'E', octave: 2 });
    expect(low.cents).toBeCloseTo(0, 6);
  });

  it('in a tuning, targets the nearest string, with its number and its cents', () => {
    const g = tuneTo(hz(55, 7), 440, standard);
    expect(g).toMatchObject({ string: 3, note: 'G', octave: 3 });
    expect(g.cents).toBeCloseTo(7, 6);

    // An F3 (MIDI 53) in standard is nearest the G string (55), two semitones flat: not a note of its own.
    const f = tuneTo(hz(53), 440, standard);
    expect(f).toMatchObject({ string: 3, note: 'G' });
    expect(f.cents).toBeCloseTo(-200, 6);

    // A low E string tuned down a tone reads as a flat E in standard, and in tune in Drop D.
    expect(tuneTo(hz(38), 440, standard)).toMatchObject({ string: 6, note: 'E' });
    expect(tuneTo(hz(38), 440, dropD)).toMatchObject({ string: 6, note: 'D', octave: 2 });
    expect(tuneTo(hz(38), 440, dropD).cents).toBeCloseTo(0, 6);
  });

  it('beyond the lowest or the highest string, targets that string', () => {
    expect(tuneTo(hz(30), 440, standard)).toMatchObject({ string: 6 });
    expect(tuneTo(hz(30), 440, standard).cents).toBeCloseTo(-1000, 6);
    expect(tuneTo(hz(76), 440, standard)).toMatchObject({ string: 1, note: 'E', octave: 4 });
  });

  it('adds each string its own offset, string 1 first', () => {
    // B string (2) +1.5 cents: a B3 at pitch reads 1.5 flat of its target, and one 1.5 sharp reads in tune.
    const offsets = [0, 1.5, 0, -2];
    expect(tuneTo(hz(59), 440, standard, offsets).cents).toBeCloseTo(-1.5, 6);
    expect(tuneTo(hz(59, 1.5), 440, standard, offsets).cents).toBeCloseTo(0, 6);
    // D string (4) −2 cents.
    expect(tuneTo(hz(50), 440, standard, offsets)).toMatchObject({ string: 4 });
    expect(tuneTo(hz(50), 440, standard, offsets).cents).toBeCloseTo(2, 6);
    // Strings without an offset have none, and chromatic ignores them.
    expect(tuneTo(hz(64), 440, standard, offsets).cents).toBeCloseTo(0, 6);
    expect(tuneTo(hz(59), 440, undefined, offsets).cents).toBeCloseTo(0, 6);
  });

  it('chooses between two strings by the cents to each target, offsets included', () => {
    // Just above halfway between G3 (55) and B3 (59) is nearer B; a +10 offset on the B string moves its target up,
    // so the G string is then the nearer.
    expect(tuneTo(hz(57, 1), 440, standard)).toMatchObject({ string: 2 });
    expect(tuneTo(hz(57, 1), 440, standard, [0, 10])).toMatchObject({ string: 3 });
  });
});
