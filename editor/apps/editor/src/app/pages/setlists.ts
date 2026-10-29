import type { PresetSummary, Setlist, SetlistEntry } from '@cathode/api-client';

/** The scenes a setlist entry can start on, as stored (0-7). */
export const SetlistScenes = ['0', '1', '2', '3', '4', '5', '6', '7'];

/**
 * The entry a song step of `step` (1 next, -1 previous) goes to from entry `from` (-1: before the first), passing over
 * entries whose preset is missing; null at either end, as the set does not wrap round. The Server steps the same way
 * for the songNext and songPrevious MIDI actions (PresetNavigator.SongStep).
 */
export function songStep(
  setlist: Setlist,
  from: number,
  step: number,
  exists: (presetId: string) => boolean
): number | null {
  const count = setlist.entries.length;
  const direction = Math.sign(step);
  if (direction === 0) {
    return null;
  }

  for (let i = Math.min(Math.max(from, -1), count) + direction; i >= 0 && i < count; i += direction) {
    const entry = setlist.entries[i];
    if (entry && exists(entry.presetId)) {
      return i;
    }
  }

  return null;
}

/** The entries with the one at `from` moved to `to` (clamped to the list); the others keep their order. */
export function movedEntry(entries: readonly SetlistEntry[], from: number, to: number): SetlistEntry[] {
  const moved = [...entries];
  if (from < 0 || from >= moved.length) {
    return moved;
  }

  const [entry] = moved.splice(from, 1);
  if (entry) {
    moved.splice(Math.min(Math.max(0, to), moved.length), 0, entry);
  }

  return moved;
}

/** The entries with a song for each of `presetIds` inserted at `at` (clamped; the end by default), on Scene 1. */
export function withSongs(
  entries: readonly SetlistEntry[],
  presetIds: readonly string[],
  at = entries.length
): SetlistEntry[] {
  const index = Math.min(Math.max(0, at), entries.length);
  const added = presetIds.map(presetId => ({ presetId, scene: 0, label: null }));
  return [...entries.slice(0, index), ...added, ...entries.slice(index)];
}

/** A name not yet taken: `base`, else `base 2`, `base 3`... (compared without regard to case). */
export function freeName(base: string, taken: readonly string[]): string {
  const names = new Set(taken.map(n => n.toLocaleLowerCase()));
  if (!names.has(base.toLocaleLowerCase())) {
    return base;
  }

  let n = 2;
  while (names.has(`${base} ${n}`.toLocaleLowerCase())) {
    n++;
  }

  return `${base} ${n}`;
}

/** A setlist entry as the pages show it. */
export interface Song {
  index: number;
  entry: SetlistEntry;
  /** The preset's name, or null when it is missing (or the library has not been read yet). */
  presetName: string | null;
  /** The song's own label, else its preset's name, else a note that the preset is missing. */
  title: string;
  /** Whether the preset is not in the library: the song cannot be played until it is put back or replaced. */
  missing: boolean;
}

/**
 * The setlist's songs, each with its preset's name; `presets` is the library, or null while it has not been read
 * (nothing shows as missing then).
 */
export function resolveSongs(setlist: Setlist | null, presets: readonly PresetSummary[] | null): Song[] {
  const names = new Map(presets?.map(p => [p.id, p.name]));
  return (setlist?.entries ?? []).map((entry, index) => {
    const presetName = names.get(entry.presetId) ?? null;
    const missing = presets !== null && presetName === null;
    const title = entry.label?.trim() || presetName || (missing ? 'Missing preset' : entry.presetId);
    return { index, entry, presetName, title, missing };
  });
}
