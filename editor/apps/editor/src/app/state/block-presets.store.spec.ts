import { TestBed } from '@angular/core/testing';
import { ApiError, type BlockPreset, type PresetBlock, type PresetModifier } from '@cathode/api-client';
import { Subject } from 'rxjs';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';
import { BlockPresetsStore, libraryFolders, wholeBlock } from './block-presets.store';
import { LiveStore } from './live.store';

function entry(id: string, name: string, folder?: string, factory = false): BlockPreset {
  return { id, type: 'delay', name, factory, values: {}, folder: folder ?? null };
}

describe('libraryFolders', () => {
  it('puts the unfiled entries first, then the folders by name, each with its entries by name', () => {
    const folders = libraryFolders([
      entry('1', 'Slap', 'Rock'),
      entry('2', 'Tape'),
      entry('3', 'Ambient', 'Ballads'),
      entry('4', 'Dotted', 'Rock'),
      entry('5', 'Analog')
    ]);

    expect(folders.map(f => [f.name, f.entries.map(e => e.name)])).toEqual([
      ['', ['Analog', 'Tape']],
      ['Ballads', ['Ambient']],
      ['Rock', ['Dotted', 'Slap']]
    ]);
  });
});

describe('wholeBlock', () => {
  it('copies every channel and the assets, and keeps only the block’s own modifiers, without its id', () => {
    const block = {
      id: 'd1',
      type: 'delay',
      channels: { A: { time: 300 }, B: { time: 450 } },
      assets: { ir: 'ir1' }
    } as unknown as PresetBlock;
    const modifiers = [
      { block: 'd1', param: 'mix', source: 'pedal1' },
      { block: 'other', param: 'gain', source: 'pedal1' }
    ] as unknown as PresetModifier[];

    const whole = wholeBlock(block, 'B', modifiers);

    expect(whole.values).toEqual({ time: 450 });
    expect(whole.channels).toEqual(block.channels);
    expect(whole.channels).not.toBe(block.channels);
    expect(whole.modifiers).toEqual([{ block: '', param: 'mix', source: 'pedal1' }]);
    expect(whole.assets).toEqual({ ir: 'ir1' });
  });
});

describe('BlockPresetsStore', () => {
  /** A chosen file with this text (jsdom's File has no text()). */
  function file(text: string): File {
    return { name: 'mine.cthblock', text: () => Promise.resolve(text) } as File;
  }

  interface Call {
    method: string;
    path: string;
    init: { params?: { path?: { id?: string } }; body?: unknown } | undefined;
  }

  function setup() {
    const calls: Call[] = [];
    const errors: unknown[] = [];
    const successes: string[] = [];
    const server = {
      entries: [entry('f1', 'Factory echo', undefined, true), entry('u1', 'Mine', 'Rock')] as unknown,
      /** A refusal for the next write, as the Server would answer it. */
      refuse: null as ApiError | null,
      /** Whether the Server answers reads. */
      up: true
    };
    const answer = (method: string, data: () => unknown) => (path: string, init?: Call['init']) => {
      calls.push({ method, path, init });
      if (method === 'GET' ? !server.up : server.refuse) {
        return Promise.reject(server.refuse ?? new ApiError(503, 'The Server is away.'));
      }

      return Promise.resolve({ data: data(), response: new Response('{}') });
    };
    const client = {
      GET: answer('GET', () => server.entries),
      PUT: answer('PUT', () => ({})),
      POST: answer('POST', () => [entry('n1', 'New'), entry('n2', 'Newer')]),
      DELETE: answer('DELETE', () => ({}))
    };
    const connected$ = new Subject<void>();
    const live = { mutate: vi.fn(() => Promise.resolve(true)) };
    TestBed.configureTestingModule({
      providers: [
        { provide: ConnectionService, useValue: { client } },
        { provide: EventStream, useValue: { connected$, library$: new Subject() } },
        { provide: LiveStore, useValue: live },
        {
          provide: Notify,
          useValue: {
            // As the real one: a failure is shown and the action resolves to undefined.
            run: async (action: () => Promise<unknown>) => {
              try {
                return await action();
              } catch (error) {
                errors.push(error);
                return undefined;
              }
            },
            error: (error: unknown) => errors.push(error),
            success: (message: string) => successes.push(message)
          }
        }
      ]
    });
    const store = TestBed.inject(BlockPresetsStore);
    const settle = () => new Promise(resolve => setTimeout(resolve));
    const writes = () => calls.filter(c => c.method !== 'GET');
    const reads = () => calls.filter(c => c.method === 'GET').length;
    return { store, calls, writes, reads, errors, successes, server, connected$, live, settle };
  }

  it('reads the Library, and tells the user’s entries and their folders from the factory ones', async () => {
    const { store } = setup();
    await store.refresh();

    expect(store.presets().map(p => p.id)).toEqual(['f1', 'u1']);
    expect(store.user().map(p => p.id)).toEqual(['u1']);
    expect(store.folders().map(f => f.name)).toEqual(['Rock']);
    expect(store.forType('delay')).toHaveLength(2);
    expect(store.forType('reverb')).toEqual([]);
  });

  it('keeps what it has when the Library cannot be read', async () => {
    const { store, server, errors } = setup();
    await store.refresh();
    server.up = false;

    await store.refresh();

    expect(errors).toHaveLength(1);
    expect(store.presets().map(p => p.id)).toEqual(['f1', 'u1']);
  });

  it('reads the Library again after a reconnection, but only once it has been read at all', async () => {
    const { store, reads, connected$, settle } = setup();

    connected$.next();
    await settle();
    expect(reads()).toBe(0);

    await store.refresh();
    connected$.next();
    await settle();
    expect(reads()).toBe(2);
  });

  it('saves a new entry under a fresh id, reads the Library again and says it was stored', async () => {
    const { store, writes, reads } = setup();

    const saved = await store.save('delay', 'Slap', { values: { time: 120 }, folder: '' });

    expect(saved).toBe(true);
    const [put] = writes();
    expect(put?.path).toBe('/api/v1/block-presets/{id}');
    const body = put?.init?.body as BlockPreset;
    expect(body.id).toMatch(/^[0-9a-f]{32}$/);
    expect(put?.init?.params?.path?.id).toBe(body.id);
    expect(body).toMatchObject({
      type: 'delay',
      name: 'Slap',
      factory: false,
      values: { time: 120 },
      folder: null, // no folder is stored as none, not as ''
      channels: null,
      modifiers: null,
      assets: null
    });
    expect(reads()).toBe(1);
  });

  it('saves a whole block with its channels, modifiers, assets and folder', async () => {
    const { store, writes } = setup();
    const channels = { A: { time: 1 }, B: { time: 2 } };

    await store.save('delay', 'Both', { values: { time: 1 }, channels, modifiers: [], assets: {}, folder: 'Rock' });

    expect(writes()[0]?.init?.body).toMatchObject({ channels, modifiers: [], assets: {}, folder: 'Rock' });
  });

  it('reports a refused save and says it was not stored (#264)', async () => {
    const { store, server, errors, reads } = setup();
    server.refuse = new ApiError(400, 'The folder name is not valid.');

    const saved = await store.save('delay', 'Slap', { values: {}, folder: 'a/b' });

    expect(saved).toBe(false);
    expect(errors).toEqual([server.refuse]);
    expect(reads()).toBe(0);
  });

  it('saves every block of the working copy into a folder and counts the entries', async () => {
    const { store, writes, reads } = setup();

    await expect(store.saveAll('Gig')).resolves.toBe(2);

    expect(writes()[0]).toMatchObject({ path: '/api/v1/live/block-presets', init: { body: { folder: 'Gig' } } });
    expect(reads()).toBe(1);
  });

  it('counts none when saving every block is refused', async () => {
    const { store, server, errors } = setup();
    server.refuse = new ApiError(503, 'The engine is not connected.');

    await expect(store.saveAll(null)).resolves.toBe(0);
    expect(errors).toHaveLength(1);
  });

  it('moves an entry by saving it again under its id, with no folder for the unfiled ones', async () => {
    const { store, writes } = setup();
    const mine = entry('u1', 'Mine', 'Rock');

    await store.moveTo(mine, 'Ballads');
    await store.moveTo(mine, '');

    expect(writes().map(w => [w.init?.params?.path?.id, (w.init?.body as BlockPreset).folder])).toEqual([
      ['u1', 'Ballads'],
      ['u1', null]
    ]);
  });

  it('deletes an entry and reads the Library again', async () => {
    const { store, writes, reads } = setup();

    await store.delete('u1');

    expect(writes()[0]).toMatchObject({ method: 'DELETE', init: { params: { path: { id: 'u1' } } } });
    expect(reads()).toBe(1);
  });

  it('imports a .cthblock file and says how many entries it added', async () => {
    const { store, writes, successes } = setup();
    const content = { entries: [entry('n1', 'New'), entry('n2', 'Newer')] };

    const added = await store.import(file(JSON.stringify(content)));

    expect(added.map(a => a.id)).toEqual(['n1', 'n2']);
    expect(writes()[0]).toMatchObject({ path: '/api/v1/block-presets/import', init: { body: content } });
    expect(successes).toEqual(['Imported 2 Library entries.']);
  });

  it('reports a file that is not a Library file and adds nothing', async () => {
    const { store, writes, errors, successes } = setup();

    await expect(store.import(file('not json'))).resolves.toEqual([]);

    expect(writes()).toEqual([]);
    expect(errors).toHaveLength(1);
    expect(successes).toEqual([]);
  });

  it('recalls an entry into the active channel, a named one, or all of them', async () => {
    const { store, live } = setup();
    const posts: unknown[] = [];
    live.mutate.mockImplementation(((request: (client: unknown) => unknown) => {
      request({ POST: (path: string, init: unknown) => posts.push([path, init]) });
      return Promise.resolve(true);
    }) as never);

    await store.apply('d1', 'u1');
    await store.apply('d1', 'u1', { channel: 'C' });
    await store.apply('d1', 'u1', { all: true });

    const body = (preset: object) => [
      '/api/v1/live/blocks/{id}/preset',
      { params: { path: { id: 'd1' } }, body: preset }
    ];
    expect(posts).toEqual([
      body({ preset: 'u1', channel: null, allChannels: false }),
      body({ preset: 'u1', channel: 'C', allChannels: false }),
      body({ preset: 'u1', channel: null, allChannels: true })
    ]);
  });
});
