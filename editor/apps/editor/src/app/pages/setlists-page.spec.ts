import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import type { Setlist } from '@cathode/api-client';
import { Notify } from '../core/notify';
import { LibraryStore } from '../state/library.store';
import { SetlistPlayer } from '../state/setlist-player';
import { SetlistsPage } from './setlists-page';

describe('SetlistsPage', () => {
  function render() {
    const setlists = signal<Setlist[]>([
      {
        id: 'friday',
        name: 'Friday',
        entries: ['a', 'b', 'c'].map(presetId => ({ presetId, scene: 0, label: null }))
      }
    ]);
    const saveSetlist = vi.fn((setlist: Setlist) => {
      setlists.set([setlist]);
      return Promise.resolve(true);
    });
    TestBed.configureTestingModule({
      providers: [
        {
          provide: LibraryStore,
          useValue: {
            setlists,
            presets: signal([]),
            ordered: signal([]),
            saveSetlist,
            refreshSetlists: () => Promise.resolve(),
            refreshPresets: () => Promise.resolve()
          }
        },
        { provide: SetlistPlayer, useValue: { setlist: signal(null) } },
        { provide: Notify, useValue: { success: vi.fn(), error: vi.fn() } },
        { provide: Router, useValue: { navigate: vi.fn(() => Promise.resolve(true)) } },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({ id: 'friday' }) } } }
      ]
    });
    TestBed.overrideComponent(SetlistsPage, { set: { template: '', imports: [] } });
    const fixture = TestBed.createComponent(SetlistsPage);
    fixture.detectChanges();
    const page = fixture.componentInstance as unknown as {
      drop: (event: DragEvent, index: number) => void;
      songKey: (event: KeyboardEvent, index: number) => void;
    };
    const order = () => setlists()[0]?.entries.map(e => e.presetId);
    const settle = () => new Promise(resolve => setTimeout(resolve));
    return { page, order, settle, saveSetlist };
  }

  function drop(data: Record<string, string>): DragEvent {
    const event = new Event('drop', { cancelable: true }) as DragEvent;
    Object.defineProperty(event, 'dataTransfer', { value: { getData: (type: string) => data[type] ?? '' } });
    return event;
  }

  /** A key pressed on a song's row itself (not in its name field). */
  function key(name: string, altKey = false): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key: name, altKey, cancelable: true });
    const row = document.createElement('li');
    Object.defineProperty(event, 'target', { value: row });
    Object.defineProperty(event, 'currentTarget', { value: row });
    return event;
  }

  it('moves a dragged song to the place it is dropped on, before that song', async () => {
    const { page, order, settle } = render();

    page.drop(drop({ 'application/x-cathode-song': '0' }), 2);
    await settle();
    expect(order()).toEqual(['b', 'a', 'c']);

    page.drop(drop({ 'application/x-cathode-song': '2' }), 0);
    await settle();
    expect(order()).toEqual(['c', 'b', 'a']);

    page.drop(drop({ 'application/x-cathode-song': '0' }), 3); // the end of the list
    await settle();
    expect(order()).toEqual(['b', 'a', 'c']);
  });

  it('adds a preset dropped from the library where it is dropped', async () => {
    const { page, order, settle } = render();

    page.drop(drop({ 'application/x-cathode-preset': 'x' }), 1);
    await settle();

    expect(order()).toEqual(['a', 'x', 'b', 'c']);
  });

  it('moves a song with ⌥↑ and ⌥↓ and removes it with Delete', async () => {
    const { page, order, settle, saveSetlist } = render();

    page.songKey(key('ArrowDown', true), 0);
    await settle();
    expect(order()).toEqual(['b', 'a', 'c']);

    page.songKey(key('ArrowUp', true), 0); // already first: nothing to store
    page.songKey(key('ArrowDown'), 0); // the focus only
    await settle();
    expect(saveSetlist).toHaveBeenCalledTimes(1);

    page.songKey(key('Delete'), 2);
    await settle();
    expect(order()).toEqual(['b', 'a']);
  });
});
