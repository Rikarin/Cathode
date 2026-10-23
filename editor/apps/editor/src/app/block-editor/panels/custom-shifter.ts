/** Note names, C first, as the engine's Pitch block names its keys. */
export const NoteNames = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B'] as const;

/** Index of "Custom Shifter" in the Pitch block's Type list. */
export const CustomShifterType = 8;

/** Parameter id of voice `voice`'s (1-4) shift for note `degree` (0-11) of the key, 0 being the key's root. */
export const customParamId = (voice: number, degree: number): string =>
    `custom${voice}${NoteNames[degree].replace('#', 's')}`;

/** True for the Custom Shifter table parameters, which the Pitch panel shows as a table instead of 48 knobs. */
export const isCustomTableParam = (id: string): boolean => /^custom[1-4][A-G]s?$/.test(id);

/** Name of the note `semitones` above pitch class `from` (0 = C), any octave. */
export const noteAbove = (from: number, semitones: number): string =>
    NoteNames[(((from + Math.round(semitones)) % 12) + 12) % 12];
