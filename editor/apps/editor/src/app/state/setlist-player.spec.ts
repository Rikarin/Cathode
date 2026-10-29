import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { GlobalSettings, PresetSummary, Setlist } from '@cathode/api-client';
import { Notify } from '../core/notify';
import { GlobalStore } from './global.store';
import { LibraryStore } from './library.store';
import { PresetSwitcher } from './preset-switcher';
import { SetlistPlayer } from './setlist-player';

const preset = (id: string, name = id): PresetSummary => ({ id, name, tags: [], modified: '', favorite: false });

describe('SetlistPlayer', () => {
  function player(active: string | null, song: number) {
    const friday: Setlist = {
      id: 'friday',
      name: 'Friday',
      entries: [
        { presetId: 'clean', scene: 2, label: 'Opener' },
        { presetId: 'gone', scene: 0, label: null },
        { presetId: 'lead', scene: 5, label: null }
      ]
    };
    const settings = signal({ activeSetlist: active, activeSong: song } as GlobalSettings);
    const load = vi.fn(() => Promise.resolve(true));
    const error = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        {
          provide: LibraryStore,
          useValue: {
            setlists: signal([friday]),
            presets: signal([preset('clean', 'Clean'), preset('lead', 'Lead')])
          }
        },
        {
          provide: GlobalStore,
          useValue: {
            settings,
            update: (change: (s: GlobalSettings) => void) => {
              const next = structuredClone(settings());
              change(next);
              settings.set(next);
              return Promise.resolve(true);
            }
          }
        },
        { provide: PresetSwitcher, useValue: { load } },
        { provide: Notify, useValue: { error } }
      ]
    });
    return { player: TestBed.inject(SetlistPlayer), settings, load, error };
  }

  it('shows the song playing and the next one, passing over a missing preset', () => {
    const { player: p } = player('friday', 0);

    expect(p.current()?.title).toBe('Opener');
    expect(p.next()?.index).toBe(2);
    expect(p.next()?.title).toBe('Lead');
    expect(p.previous()).toBeNull();
  });

  it('loads the next song on its scene, from its setlist entry', async () => {
    const { player: p, load } = player('friday', 0);

    await p.step(1);

    expect(load).toHaveBeenCalledWith('lead', 'Lead', false, {
      presetId: 'lead',
      scene: 5,
      position: { setlist: 'friday', entry: 2 }
    });
  });

  it('picks a setlist before its first song, and none when its id is gone', async () => {
    const { player: p, settings } = player('deleted', 1);

    expect(p.setlist()).toBeNull();
    expect(p.index()).toBe(-1);
    await p.pick('friday');
    expect(settings()).toEqual({ activeSetlist: 'friday', activeSong: -1 });
    expect(p.current()).toBeNull();
    expect(p.next()?.index).toBe(0);
  });

  it('stops at the end of the set and refuses a missing song', async () => {
    const { player: p, load, error } = player('friday', 2);

    expect(await p.step(1)).toBe(false);
    expect(await p.play(1)).toBe(false);
    expect(load).not.toHaveBeenCalled();
    expect(error).toHaveBeenCalledWith(expect.stringContaining('Song 2'));
  });

  it('says so when no setlist is being played', async () => {
    const { player: p, error } = player(null, -1);

    expect(await p.step(1)).toBe(false);
    expect(error).toHaveBeenCalled();
  });
});
