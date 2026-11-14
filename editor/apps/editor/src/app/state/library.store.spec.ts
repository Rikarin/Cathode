import { TestBed } from '@angular/core/testing';
import { ApiError, type Bank, type LibraryChanged, type RigEvent, type Setlist } from '@cathode/api-client';
import { Subject } from 'rxjs';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';
import { BlockPresetsStore } from './block-presets.store';
import { CatalogStore } from './catalog.store';
import { LibraryStore, presetUses, restoreSummary } from './library.store';
import { LiveStore } from './live.store';

describe('LibraryStore.isStored', () => {
  function store(get: () => Promise<unknown>): LibraryStore {
    TestBed.configureTestingModule({
      providers: [
        { provide: ConnectionService, useValue: { client: { GET: get } } },
        {
          provide: EventStream,
          useValue: { events$: new Subject(), connected$: new Subject(), library$: new Subject() }
        },
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
    const events = {
      events$: new Subject<RigEvent>(),
      connected$: new Subject<void>(),
      library$: new Subject<LibraryChanged>()
    };
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

  it('reads again the list the Server says has changed, whoever changed it', async () => {
    const { reads, events, settle } = setup();
    TestBed.inject(LibraryStore);

    events.library$.next({ type: 'libraryChanged', kind: 'presets' });
    events.library$.next({ type: 'libraryChanged', kind: 'setlists' });
    events.library$.next({ type: 'libraryChanged', kind: 'banks' });
    events.library$.next({ type: 'libraryChanged', kind: 'assets' });
    events.library$.next({ type: 'libraryChanged', kind: 'snapshots' });
    events.events$.next({ type: 'paramChanged', revision: 6 });
    await settle();

    expect(reads).toEqual(['/api/v1/presets', '/api/v1/setlists', '/api/v1/banks', '/api/v1/assets']);
  });

  it('reads the Block Library again when another editor changes it, once it has been read', async () => {
    const { reads, events, settle } = setup();
    const blockPresets = TestBed.inject(BlockPresetsStore);

    events.library$.next({ type: 'libraryChanged', kind: 'blockPresets' });
    await settle();
    expect(reads).toEqual([]);

    await blockPresets.refresh();
    events.library$.next({ type: 'libraryChanged', kind: 'blockPresets' });
    events.library$.next({ type: 'libraryChanged', kind: 'presets' });
    await settle();
    expect(reads).toEqual(['/api/v1/block-presets', '/api/v1/block-presets']);
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
    const puts: { setlist: Setlist; ifMatch: string | undefined; answer: (ok: boolean) => void }[] = [];
    /** The revision of each stored setlist, counted as the Server counts it. */
    const revisions = new Map<string, number>();
    const withRevisions = () => stored.map(s => ({ ...s, revision: revisions.get(s.id) }));
    const PUT = (_path: string, request: { body: Setlist; headers?: Record<string, string> }) =>
      new Promise((resolve, reject) => {
        const { id } = request.body;
        const ifMatch = request.headers?.['If-Match'];
        puts.push({
          setlist: request.body,
          ifMatch,
          answer: ok => {
            if (ifMatch !== undefined && ifMatch !== `"${revisions.get(id) ?? 0}"`) {
              reject(new ApiError(412, 'Changed elsewhere.'));
            } else if (ok) {
              revisions.set(id, (revisions.get(id) ?? 0) + 1);
              stored = [...stored.filter(s => s.id !== id), request.body];
              resolve({ data: { ...request.body, revision: revisions.get(id) }, response: new Response('{}') });
            } else {
              reject(new ApiError(400, 'Refused.'));
            }
          }
        });
      });
    /** Another editor stores a setlist. */
    const elsewhere = (setlist: Setlist) => {
      revisions.set(setlist.id, (revisions.get(setlist.id) ?? 0) + 1);
      stored = [...stored.filter(s => s.id !== setlist.id), setlist];
    };
    const GET = () => Promise.resolve({ data: withRevisions(), response: new Response('{}') });
    stored.forEach(s => revisions.set(s.id, 1));
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
        {
          provide: EventStream,
          useValue: { events$: new Subject(), connected$: new Subject(), library$: new Subject() }
        },
        { provide: Notify, useValue: { run, error: () => undefined } }
      ]
    });
    const library = TestBed.inject(LibraryStore);
    void library.refreshSetlists();
    const settle = () => new Promise(resolve => setTimeout(resolve));
    const songs = () => library.setlists()[0]?.entries.map(e => e.presetId);
    /** An edit as the Setlists page makes one: from the setlist the store shows now. */
    const add = (presetId: string) => {
      const shown = library.setlists()[0] ?? friday;
      return library.saveSetlist({ ...shown, entries: [...shown.entries, { presetId, scene: 0 }] });
    };
    return { library, puts, settle, songs, add, elsewhere };
  }

  const friday: Setlist = { id: 'friday', name: 'Friday', entries: [{ presetId: 'a', scene: 0 }] };

  it('keeps both of two edits made before the Server answered the first', async () => {
    const { puts, settle, songs, add } = setup([friday]);
    await settle();

    const first = add('b');
    expect(songs()).toEqual(['a', 'b']); // shown at once, so the next edit starts from it
    const second = add('c');
    await settle();

    expect(puts).toHaveLength(1); // one request at a time, in the order the edits were made
    puts[0]?.answer(true);
    await settle();
    expect(puts).toHaveLength(2);
    expect(puts[1]?.setlist.entries.map(e => e.presetId)).toEqual(['a', 'b', 'c']);
    // Each save is guarded with the revision it was made on: the one read, then the one the first was answered with.
    expect(puts.map(p => p.ifMatch)).toEqual(['"1"', '"2"']);
    puts[1]?.answer(true);

    await expect(first).resolves.toBe(true);
    await expect(second).resolves.toBe(true);
    expect(songs()).toEqual(['a', 'b', 'c']);
  });

  it('does not put a setlist back over what another editor stored since this one read it (412)', async () => {
    const { library, puts, settle, songs, add, elsewhere } = setup([friday]);
    await settle();

    elsewhere({ ...friday, entries: [{ presetId: 'theirs', scene: 0 }] });
    const stale = add('mine'); // made on the setlist as it was read, before the other editor's change is known
    await settle();
    puts[0]?.answer(true);

    await expect(stale).resolves.toBe(false);
    expect(songs()).toEqual(['theirs']); // read again: the other editor's setlist, not the refused one

    // Made again on what is stored now, the edit lands with the other editor's change kept.
    const again = add('mine');
    await settle();
    expect(puts[1]?.ifMatch).toBe('"2"');
    puts[1]?.answer(true);
    await expect(again).resolves.toBe(true);
    expect(songs()).toEqual(['theirs', 'mine']);
    expect(library.setlists()).toHaveLength(1);
  });

  it('guards an edit with the revision it was made on, not one read while it waited behind another save', async () => {
    const { library, puts, settle, add, elsewhere } = setup([friday, { id: 'sat', name: 'Saturday', entries: [] }]);
    await settle();

    const other = library.saveSetlist({ id: 'sat', name: 'Saturday night', entries: [] });
    const mine = add('mine'); // Friday, at revision 1, waiting behind the save of Saturday
    elsewhere({ ...friday, entries: [{ presetId: 'theirs', scene: 0 }] });
    await library.refreshSetlists(); // the other editor's change is announced and read while the edit waits
    await settle();
    puts[0]?.answer(true);
    await other;
    await settle();

    expect(puts[1]?.ifMatch).toBe('"1"');
    puts[1]?.answer(true);
    await expect(mine).resolves.toBe(false);
  });

  it('sends no If-Match for a setlist that has never been stored', async () => {
    const { library, puts, settle } = setup([]);
    await settle();

    const made = library.saveSetlist({ id: 'new', name: 'New', entries: [] });
    await settle();
    expect(puts[0]?.ifMatch).toBeUndefined();
    puts[0]?.answer(true);
    await expect(made).resolves.toBe(true);
  });

  it('shows what is stored after a refusal, and drops the edit that was built on the refused one', async () => {
    const { puts, settle, songs, add } = setup([friday]);
    await settle();

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

describe('LibraryStore asset usage', () => {
  it('reads usage again after an empty answer, which is a rig where nothing uses an asset yet', async () => {
    const reads: string[] = [];
    let usage: unknown[] = [];
    const GET = (path: string) => {
      reads.push(path);
      const data = path === '/api/v1/assets/usage' ? usage : [];
      return Promise.resolve({ data, response: new Response('{}') });
    };
    const library$ = new Subject<LibraryChanged>();
    TestBed.configureTestingModule({
      providers: [
        { provide: ConnectionService, useValue: { client: { GET } } },
        {
          provide: EventStream,
          useValue: { events$: new Subject<RigEvent>(), connected$: new Subject(), library$ }
        },
        { provide: Notify, useValue: { run: (action: () => Promise<unknown>) => action(), error: () => undefined } }
      ]
    });
    const library = TestBed.inject(LibraryStore);

    await library.refreshUsage(); // the asset page opens: nothing uses anything
    expect(library.usage().size).toBe(0);

    // A preset that uses a capture is saved; the Server says the presets changed.
    usage = [{ ref: 'capture1', presets: ['p1'] }];
    reads.length = 0;
    library$.next({ type: 'libraryChanged', kind: 'presets' });
    await new Promise(resolve => setTimeout(resolve));

    expect(reads).toContain('/api/v1/assets/usage');
    expect(library.usage().get('capture1')).toEqual(['p1']);
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

describe('restoring a backup', () => {
  const result = {
    presets: 0,
    blockPresets: 0,
    templates: 0,
    snapshots: 0,
    setlists: 0,
    banks: 0,
    assets: 0,
    settings: false,
    skipped: []
  };

  it('sends the file to the Server and reads the library again', async () => {
    const read: string[] = [];
    const post = vi.fn((_path: string, _init: unknown) =>
      Promise.resolve({ data: { ...result, presets: 3 }, response: new Response('{}') })
    );
    TestBed.configureTestingModule({
      providers: [
        {
          provide: ConnectionService,
          useValue: {
            client: {
              POST: post,
              GET: (path: string) => {
                read.push(path);
                return Promise.resolve({ data: [], response: new Response('[]') });
              }
            }
          }
        },
        {
          provide: EventStream,
          useValue: { events$: new Subject(), connected$: new Subject(), library$: new Subject() }
        },
        { provide: Notify, useValue: { run: (action: () => Promise<unknown>) => action(), error: () => undefined } }
      ]
    });
    const file = new File(['zip'], 'rig.cthbackup');

    const restored = await TestBed.inject(LibraryStore).restore(file);

    expect(restored?.presets).toBe(3);
    expect(post.mock.calls[0]?.[0]).toBe('/api/v1/library/restore');
    expect((post.mock.calls[0]?.[1] as { body: unknown }).body).toBe(file);
    expect(read).toEqual(
      expect.arrayContaining(['/api/v1/presets', '/api/v1/assets', '/api/v1/banks', '/api/v1/setlists'])
    );
  });

  it('says what was restored in one sentence', () => {
    expect(restoreSummary({ ...result, presets: 1 })).toBe('Restored 1 preset.');
    expect(restoreSummary({ ...result, presets: 12, banks: 2, assets: 3, settings: true })).toBe(
      'Restored 12 presets, 2 banks, 3 new IRs and captures and the global settings.'
    );
    expect(restoreSummary(result)).toBe('The backup held nothing to restore.');
  });
});
