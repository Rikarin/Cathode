import { TestBed } from '@angular/core/testing';
import { ApiError, type Bank, type Setlist } from '@cathode/api-client';
import { Subject } from 'rxjs';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';
import { LibraryStore, presetUses } from './library.store';

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
    await expect(library.isStored('p1')).rejects.toThrowError('The engine is not connected.');
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
