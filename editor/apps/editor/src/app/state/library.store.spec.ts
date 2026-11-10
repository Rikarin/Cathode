import { TestBed } from '@angular/core/testing';
import { ApiError, type Bank, type RigEvent, type Setlist } from '@cathode/api-client';
import { Subject } from 'rxjs';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';
import { BlockPresetsStore } from './block-presets.store';
import { CatalogStore } from './catalog.store';
import { LibraryStore, presetUses } from './library.store';
import { LiveStore } from './live.store';

describe('LibraryStore.isStored', () => {
  function store(get: () => Promise<unknown>): LibraryStore {
    TestBed.configureTestingModule({
      providers: [
        { provide: ConnectionService, useValue: { client: { GET: get } } },
        { provide: EventStream, useValue: { events$: new Subject(), connected$: new Subject() } },
        { provide: Notify, useValue: { run: (action: () => Promise<unknown>) => action(), error: () => undefined } }
      ]
    });
    return TestBed.inject(LibraryStore);
  }

  it('is false for a preset the Server does not have (404)', async () => {
    const library = store(() => Promise.reject(new ApiError(404, 'Not found.')));
    await expect(library.isStored('new')).resolves.toBe(false);
  });

  it('is true for a stored preset', async () => {
    const library = store(() => Promise.resolve({ data: {}, response: new Response('{}') }));
    await expect(library.isStored('p1')).resolves.toBe(true);
  });

  it('rejects when the Server refuses for another reason, rather than calling the preset new', async () => {
    const library = store(() => Promise.reject(new ApiError(503, 'The engine is not connected.')));
    await expect(library.isStored('p1')).rejects.toThrow('The engine is not connected.');
  });
});

describe('stores resync after a reconnection', () => {
  function setup() {
    const reads: string[] = [];
    const answers: Record<string, unknown> = {
      '/api/v1/presets': [{ id: 'p2', name: 'New' }],
      '/api/v1/assets': [{ ref: 'ir1', kind: 'ir' }],
      '/api/v1/banks': [{ id: 'gig', name: 'Gig', slots: [] }],
      '/api/v1/setlists': [{ id: 'friday', name: 'Friday', entries: [] }],
      '/api/v1/block-presets': [{ id: 'b1', type: 'delay', name: 'Echo', values: {} }],
      '/api/v1/catalog/blocks': [{ typeId: 'delay', category: 'Delay' }],
      '/api/v1/catalog/rig': { typeId: 'rig', params: [] }
    };
    const GET = (path: string) => {
      reads.push(path);
      return Promise.resolve({ data: answers[path], response: new Response('{}') });
    };
    const events = { events$: new Subject<RigEvent>(), connected$: new Subject<void>() };
    TestBed.configureTestingModule({
      providers: [
        { provide: ConnectionService, useValue: { client: { GET } } },
        { provide: EventStream, useValue: events },
        { provide: Notify, useValue: { run: (action: () => Promise<unknown>) => action(), error: () => undefined } },
        { provide: LiveStore, useValue: {} }
      ]
    });
    const settle = () => new Promise(resolve => setTimeout(resolve));
    return { reads, events, settle };
  }

  it('reads the presets, assets, banks and setlists again', async () => {
    const { reads, events, settle } = setup();
    const library = TestBed.inject(LibraryStore);

    events.connected$.next();
    await settle();

    expect(reads.sort()).toEqual(['/api/v1/assets', '/api/v1/banks', '/api/v1/presets', '/api/v1/setlists']);
    expect(library.presets().map(p => p.id)).toEqual(['p2']);
    expect(library.banks().map(b => b.id)).toEqual(['gig']);
    expect(library.setlists().map(s => s.id)).toEqual(['friday']);
    expect(library.impulseResponses()).toHaveLength(1);
  });

  it('reads the preset list again when a preset is renamed or tagged, not only saved', async () => {
    const { reads, events, settle } = setup();
    TestBed.inject(LibraryStore);

    events.events$.next({ type: 'presetMetadataChanged', revision: 4 });
    events.events$.next({ type: 'presetSaved', revision: 5 });
    events.events$.next({ type: 'paramChanged', revision: 6 });
    await settle();

    expect(reads).toEqual(['/api/v1/presets', '/api/v1/presets']);
  });

  it('reads the block catalogue again', async () => {
    const { reads, events, settle } = setup();
    const catalog = TestBed.inject(CatalogStore);

    events.connected$.next();
    await settle();

    expect(reads.sort()).toEqual(['/api/v1/catalog/blocks', '/api/v1/catalog/rig']);
    expect(catalog.info('delay')).toBeDefined();
  });

  it('reads the Block Library again once it has been read', async () => {
    const { reads, events, settle } = setup();
    const blockPresets = TestBed.inject(BlockPresetsStore);

    events.connected$.next();
    await settle();
    expect(reads).toEqual([]); // never opened: nothing to keep fresh

    await blockPresets.refresh();
    events.connected$.next();
    await settle();
    expect(reads).toEqual(['/api/v1/block-presets', '/api/v1/block-presets']);
    expect(blockPresets.forType('delay')).toHaveLength(1);
  });
});

describe('LibraryStore.saveSetlist', () => {
  /** A Server whose answers to PUT wait until the test lets them go. */
  function setup(stored: Setlist[]) {
    const puts: { setlist: Setlist; answer: (ok: boolean) => void }[] = [];
    const PUT = (_path: string, request: { body: Setlist }) =>
      new Promise((resolve, reject) =>
        puts.push({
          setlist: request.body,
          answer: ok => {
            if (ok) {
              stored = [...stored.filter(s => s.id !== request.body.id), request.body];
              resolve({ data: request.body, response: new Response('{}') });
            } else {
              reject(new ApiError(400, 'Refused.'));
            }
          }
        })
      );
    const GET = () => Promise.resolve({ data: stored, response: new Response('{}') });
    const run = async (action: () => Promise<unknown>) => {
      try {
        return await action();
      } catch {
        return undefined;
      }
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: ConnectionService, useValue: { client: { GET, PUT } } },
        { provide: EventStream, useValue: { events$: new Subject(), connected$: new Subject() } },
        { provide: Notify, useValue: { run, error: () => undefined } }
      ]
    });
    const library = TestBed.inject(LibraryStore);
    library.setlists.set(stored);
    const settle = () => new Promise(resolve => setTimeout(resolve));
    const songs = () => library.setlists()[0]?.entries.map(e => e.presetId);
    /** An edit as the Setlists page makes one: from the setlist the store shows now. */
    const add = (presetId: string) => {
      const shown = library.setlists()[0] ?? friday;
      return library.saveSetlist({ ...shown, entries: [...shown.entries, { presetId, scene: 0 }] });
    };
    return { library, puts, settle, songs, add };
  }

  const friday: Setlist = { id: 'friday', name: 'Friday', entries: [{ presetId: 'a', scene: 0 }] };

  it('keeps both of two edits made before the Server answered the first', async () => {
    const { puts, settle, songs, add } = setup([friday]);

    const first = add('b');
    expect(songs()).toEqual(['a', 'b']); // shown at once, so the next edit starts from it
    const second = add('c');
    await settle();

    expect(puts).toHaveLength(1); // one request at a time, in the order the edits were made
    puts[0]?.answer(true);
    await settle();
    expect(puts).toHaveLength(2);
    expect(puts[1]?.setlist.entries.map(e => e.presetId)).toEqual(['a', 'b', 'c']);
    puts[1]?.answer(true);

    await expect(first).resolves.toBe(true);
    await expect(second).resolves.toBe(true);
    expect(songs()).toEqual(['a', 'b', 'c']);
  });

  it('shows what is stored after a refusal, and drops the edit that was built on the refused one', async () => {
    const { puts, settle, songs, add } = setup([friday]);

    const first = add('missing');
    const second = add('c');
    await settle();
    puts[0]?.answer(false);

    await expect(first).resolves.toBe(false);
    await expect(second).resolves.toBe(false);
    expect(puts).toHaveLength(1);
    expect(songs()).toEqual(['a']);

    // The next edit is stored as usual.
    const third = add('d');
    await settle();
    puts[1]?.answer(true);
    await expect(third).resolves.toBe(true);
    expect(songs()).toEqual(['a', 'd']);
  });
});

describe('presetUses', () => {
  const banks: Bank[] = [
    { id: 'factory', name: 'Factory', slots: ['f1'], factory: true },
    { id: 'gig', name: 'Gig', slots: ['a', 'x', 'b', 'a'] }
  ];
  const setlists: Setlist[] = [
    {
      id: 'friday',
      name: 'Friday',
      entries: [
        { presetId: 'b', scene: 0 },
        { presetId: 'a', scene: 2 }
      ]
    }
  ];

  it('names every bank slot and setlist entry that holds the preset, counted from 1', () => {
    expect(presetUses('a', banks, setlists)).toEqual(['bank “Gig”, slots 1, 4', 'setlist “Friday”, entry 2']);
    expect(presetUses('b', banks, setlists)).toEqual(['bank “Gig”, slot 3', 'setlist “Friday”, entry 1']);
  });

  it('is empty for a preset nothing uses', () => {
    expect(presetUses('c', banks, setlists)).toEqual([]);
  });
});
