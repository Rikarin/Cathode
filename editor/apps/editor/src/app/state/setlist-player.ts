import { Injectable, computed, inject } from '@angular/core';
import { Notify } from '../core/notify';
import { type Song, resolveSongs, songStep } from '../pages/setlists';
import { GlobalStore } from './global.store';
import { LibraryStore } from './library.store';
import { PresetSwitcher } from './preset-switcher';

/**
 * Playing a setlist: which one is active and which song is playing, both kept in the global settings (as the active
 * footswitch layout is) so footswitches, every editor and a restarted Cathode agree. Loading any of its songs makes a
 * setlist the active one; song next and previous step through it, passing over missing presets, and stop at either end.
 */
@Injectable({ providedIn: 'root' })
export class SetlistPlayer {
  private readonly library = inject(LibraryStore);
  private readonly globals = inject(GlobalStore);
  private readonly switcher = inject(PresetSwitcher);
  private readonly notify = inject(Notify);

  /** The active setlist, or null (none picked, or it has been deleted). */
  readonly setlist = computed(() => {
    const id = this.globals.settings()?.activeSetlist;
    return (id && this.library.setlists().find(s => s.id === id)) || null;
  });
  readonly songs = computed(() => {
    const presets = this.library.presets();
    return resolveSongs(this.setlist(), presets.length > 0 ? presets : null);
  });
  /** The entry last loaded, from 0; -1 before the first. */
  readonly index = computed(() => (this.setlist() ? (this.globals.settings()?.activeSong ?? -1) : -1));
  readonly current = computed<Song | null>(() => this.songs()[this.index()] ?? null);
  readonly next = computed(() => this.target(1));
  readonly previous = computed(() => this.target(-1));

  /** Makes a setlist the active one, before its first song (song next then loads the first); null for none. */
  async pick(id: string | null): Promise<boolean> {
    return this.globals.update(g => {
      g.activeSetlist = id;
      g.activeSong = -1;
    });
  }

  /** Loads the next (1) or previous (-1) song, asking first about unsaved changes. */
  async step(direction: 1 | -1): Promise<boolean> {
    if (!this.setlist()) {
      this.notify.error('No setlist is being played: pick one on the Perform page.');
      return false;
    }

    const song = direction > 0 ? this.next() : this.previous();
    return song ? this.play(song.index) : false;
  }

  /** Loads a song of the active setlist on its scene, asking first about unsaved changes. */
  async play(index: number): Promise<boolean> {
    const setlist = this.setlist();
    const song = this.songs()[index];
    if (!setlist || !song) {
      return false;
    }

    if (song.missing) {
      this.notify.error(`Song ${index + 1} cannot be played: its preset is not in the library any more.`);
      return false;
    }

    const { presetId, scene } = song.entry;
    return this.switcher.load(presetId, song.presetName ?? song.title, false, {
      presetId,
      scene,
      position: { setlist: setlist.id, entry: index }
    });
  }

  private target(direction: 1 | -1): Song | null {
    const setlist = this.setlist();
    const songs = this.songs();
    const missing = new Set(songs.filter(s => s.missing).map(s => s.entry.presetId));
    const at = setlist ? songStep(setlist, this.index(), direction, id => !missing.has(id)) : null;
    return at === null ? null : (songs[at] ?? null);
  }
}
