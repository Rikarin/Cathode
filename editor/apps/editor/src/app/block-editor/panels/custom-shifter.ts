/** Note names, C first, as the engine's Pitch block names its keys. */
export const NoteNames = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B'] as const;

/** Index of "Custom Shifter" in the Pitch block's Type list. */
export const CustomShifterType = 8;

/** Parameter id of voice `voice`'s (1-4) shift for note `degree` (0-11) of the key, 0 being the key's root. */
export function customParamId(voice: number, degree: number): string {
  const note = NoteNames[degree];
  if (note === undefined) {
    throw new RangeError(`No scale degree ${degree}`);
  }

  return `custom${voice}${note.replace('#', 's')}`;
}

/** True for the Custom Shifter table parameters, which the Pitch panel shows as a table instead of 48 knobs. */
export const isCustomTableParam = (id: string): boolean => /^custom[1-4][A-G]s?$/.test(id);

/** Name of the note `semitones` above pitch class `from` (0 = C), any octave. */
export const noteAbove = (from: number, semitones: number): string =>
  NoteNames[(((from + Math.round(semitones)) % 12) + 12) % 12] ?? ''; // '' only for a NaN shift

/** Index of "Diatonic" in the Pitch block's Type list, and of "Custom" in its Scale list. */
export const DiatonicType = 2;
export const CustomScale = 12;

/**
 * The custom scale's notes named in `tonic` (0 = C), as the engine reads them: the first `count` notes (semitones
 * above the tonic) with the root added, rising and without repeats.
 */
export function customScaleNotes(tonic: number, count: number, notes: readonly number[]): string[] {
  const degrees = new Set([0, ...notes.slice(0, count).map(n => ((Math.round(n) % 12) + 12) % 12)]);
  return [...degrees].sort((a, b) => a - b).map(d => noteAbove(tonic, d));
}
