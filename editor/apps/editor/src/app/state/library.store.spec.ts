import { TestBed } from '@angular/core/testing';
import { ApiError, type Bank, type RigEvent, type Setlist } from '@cathode/api-client';
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
        { provide: EventStream, useValue: { events$: new Subject(), connected$: new Subject() } },
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
