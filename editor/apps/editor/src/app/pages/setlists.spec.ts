import type { PresetSummary, Setlist, SetlistEntry } from '@cathode/api-client';
import { withSetlist } from '../state/library.store';
import { freeName, movedEntry, resolveSongs, songStep, withSongs } from './setlists';

const entry = (presetId: string, scene = 0, label: string | null = null): SetlistEntry => ({ presetId, scene, label });
const preset = (id: string, name = id): PresetSummary => ({ id, name, tags: [], modified: '', favorite: false });
const setlist = (...entries: SetlistEntry[]): Setlist => ({ id: 's', name: 'Friday', entries });

describe('setlists', () => {
  it('steps song by song, passing over missing presets and stopping at either end', () => {
    const s = setlist(entry('a'), entry('missing'), entry('b'));
    const exists = (id: string) => id !== 'missing';

    expect(songStep(s, -1, 1, exists)).toBe(0); // picked, not started: the first song
    expect(songStep(s, 0, 1, exists)).toBe(2);
    expect(songStep(s, 2, 1, exists)).toBeNull(); // no wrapping round
    expect(songStep(s, 2, -1, exists)).toBe(0);
    expect(songStep(s, 0, -1, exists)).toBeNull();
    expect(songStep(s, -1, -1, exists)).toBeNull();
    expect(songStep(s, 9, -1, exists)).toBe(2); // the setlist got shorter since
    expect(songStep(s, 0, 0, exists)).toBeNull();
    expect(songStep(setlist(), -1, 1, exists)).toBeNull();
  });

  it('moves a song, keeping the others in order', () => {
    const entries = [entry('a'), entry('b'), entry('c'), entry('d')];

    expect(movedEntry(entries, 0, 2).map(e => e.presetId)).toEqual(['b', 'c', 'a', 'd']);
    expect(movedEntry(entries, 3, 0).map(e => e.presetId)).toEqual(['d', 'a', 'b', 'c']);
    expect(movedEntry(entries, 1, 99).map(e => e.presetId)).toEqual(['a', 'c', 'd', 'b']);
    expect(movedEntry(entries, 7, 0)).toEqual(entries);
  });

  it('adds songs on Scene 1 at the end or before a song', () => {
    const entries = [entry('a', 3, 'Intro'), entry('b')];

    expect(withSongs(entries, ['x'])).toEqual([...entries, entry('x')]);
    expect(withSongs(entries, ['x', 'y'], 1).map(e => e.presetId)).toEqual(['a', 'x', 'y', 'b']);
    expect(withSongs(entries, ['x'], -4).map(e => e.presetId)).toEqual(['x', 'a', 'b']);
  });

  it('names a copy so it does not clash with another setlist', () => {
    expect(freeName('Friday copy', ['Friday'])).toBe('Friday copy');
    expect(freeName('Friday copy', ['Friday', 'friday COPY', 'Friday copy 2'])).toBe('Friday copy 3');
  });

  it('shows each song by its label, else its preset, and marks a missing preset', () => {
    const s = setlist(entry('a', 0, ' Opener '), entry('b', 2), entry('gone', 1, 'Encore'), entry('gone'));
    const songs = resolveSongs(s, [preset('a', 'Clean'), preset('b', 'Lead')]);

    expect(songs.map(x => [x.title, x.presetName, x.missing])).toEqual([
      ['Opener', 'Clean', false],
      ['Lead', 'Lead', false],
      ['Encore', null, true],
      ['Missing preset', null, true]
    ]);
    // Before the library has been read, nothing is called missing.
    expect(resolveSongs(s, null).some(x => x.missing)).toBe(false);
    expect(resolveSongs(null, [])).toEqual([]);
  });

  it('keeps the setlists by name after one is stored', () => {
    const list = [setlist(), { id: 't', name: 'saturday', entries: [] }];

    const renamed = withSetlist(list, { id: 's', name: 'Sunday', entries: [] });
    const added = withSetlist(list, { id: 'u', name: 'Acoustic', entries: [] });

    expect(renamed.map(s => s.name)).toEqual(['saturday', 'Sunday']);
    expect(added.map(s => s.id)).toEqual(['u', 's', 't']);
  });
});
