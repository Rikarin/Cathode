/** A tuning: its strings' notes in scientific pitch (A4 = the reference), lowest string first. */
export interface Tuning {
  id: string;
  name: string;
  notes: string;
}

/** The tuning id for the nearest note of the twelve, whatever the string: the tuner's default. */
export const Chromatic = 'chromatic';

/**
 * The tunings the tuner offers besides chromatic, by the id the global settings store. One line adds one: notes are
 * a letter, an optional `#` or `b` and an octave, lowest string first (string 1 is the last, the highest).
 */
export const Tunings: readonly Tuning[] = [
  { id: 'standard', name: 'Standard', notes: 'E2 A2 D3 G3 B3 E4' },
  { id: 'halfStepDown', name: 'Half step down', notes: 'Eb2 Ab2 Db3 Gb3 Bb3 Eb4' },
  { id: 'wholeStepDown', name: 'Whole step down', notes: 'D2 G2 C3 F3 A3 D4' },
  { id: 'dropD', name: 'Drop D', notes: 'D2 A2 D3 G3 B3 E4' },
  { id: 'dropC', name: 'Drop C', notes: 'C2 G2 C3 F3 A3 D4' },
  { id: 'dadgad', name: 'DADGAD', notes: 'D2 A2 D3 G3 A3 D4' },
  { id: 'openG', name: 'Open G', notes: 'D2 G2 D3 G3 B3 D4' },
  { id: 'openD', name: 'Open D', notes: 'D2 A2 D3 F#3 A3 D4' },
  { id: 'openE', name: 'Open E', notes: 'E2 B2 E3 G#3 B3 E4' },
  { id: 'sevenString', name: '7-string standard', notes: 'B1 E2 A2 D3 G3 B3 E4' },
  { id: 'eightString', name: '8-string standard', notes: 'F#1 B1 E2 A2 D3 G3 B3 E4' },
  { id: 'bass4', name: 'Bass 4-string', notes: 'E1 A1 D2 G2' },
  { id: 'bass5', name: 'Bass 5-string', notes: 'B0 E1 A1 D2 G2' }
];

/** The most strings a tuning has, so the most offsets the global settings keep (GlobalValidation on the Server). */
export const MaxStrings = 8;

/** What the tuner shows: the note (or string) it targets and how far the pitch is from it. */
export interface TunerTarget {
  frequency: number;
  note: string;
  octave: number;
  /** Deviation from the target in cents; beyond ±50 when a tuning's nearest string is further than a quarter tone. */
  cents: number;
  /** The string targeted, 1 for the highest; absent in chromatic. */
  string?: number;
}

/** One string of a tuning, as {@link strings} reads it. */
export interface TuningString {
  /** 1 for the highest string. */
  number: number;
  note: string;
  octave: number;
  /** MIDI note number (A4 = 69). */
  midi: number;
}

const Sharps = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B'];
const Letters: Record<string, number> = { C: 0, D: 2, E: 4, F: 5, G: 7, A: 9, B: 11 };

/** The tuning with this id; undefined for chromatic or an id this editor does not know (which reads as chromatic). */
export function findTuning(id: string | null | undefined): Tuning | undefined {
  return Tunings.find(t => t.id === id);
}

const parsed = new WeakMap<Tuning, readonly TuningString[]>();

/** A tuning's strings, highest (string 1) first. Parsed once per tuning: the tuner asks on every frame. */
export function strings(tuning: Tuning): readonly TuningString[] {
  let result = parsed.get(tuning);
  if (!result) {
    result = parse(tuning);
    parsed.set(tuning, result);
  }

  return result;
}

function parse(tuning: Tuning): TuningString[] {
  const notes = tuning.notes.split(' ');
  return notes
    .map((name, i) => {
      const match = /^([A-G])([#b]?)(-?\d+)$/.exec(name);
      if (!match) {
        throw new Error(`Tuning ${tuning.id}: '${name}' is not a note.`);
      }

      const [, letter = 'C', accidental = '', digits = '4'] = match;
      const octave = Number(digits);
      const shift = accidental === '#' ? 1 : accidental === 'b' ? -1 : 0;
      return {
        number: notes.length - i,
        note: letter + accidental,
        octave,
        midi: 12 * (octave + 1) + (Letters[letter] ?? 0) + shift
      };
    })
    .reverse();
}

/**
 * Where a detected `frequency` sits, against an A4 of `reference` Hz. Without a tuning, the nearest of the twelve
 * notes, as the engine reports it. With one, the nearest string of it, whose target is its note moved by that
 * string's offset in cents (`offsets[0]` for string 1): a +2 offset tunes the string 2 cents sharp, so a string at
 * pitch then reads −2. "Nearest" counts the offsets, so it is the string the pitch is fewest cents from.
 */
export function tuneTo(
  frequency: number,
  reference = 440,
  tuning?: Tuning,
  offsets: readonly number[] = []
): TunerTarget {
  const midi = 69 + 12 * Math.log2(frequency / reference);
  if (!tuning) {
    const nearest = Math.round(midi);
    return { frequency, ...name(nearest), cents: (midi - nearest) * 100 };
  }

  let best: TunerTarget | undefined;
  for (const s of strings(tuning)) {
    const cents = (midi - s.midi) * 100 - (offsets[s.number - 1] ?? 0);
    if (!best || Math.abs(cents) < Math.abs(best.cents)) {
      best = { frequency, note: s.note, octave: s.octave, cents, string: s.number };
    }
  }

  return best ?? { frequency, ...name(Math.round(midi)), cents: 0 };
}

function name(midi: number): { note: string; octave: number } {
  return { note: Sharps[((midi % 12) + 12) % 12] ?? 'C', octave: Math.floor(midi / 12) - 1 };
}
