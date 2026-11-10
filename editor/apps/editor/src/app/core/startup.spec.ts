import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { CatalogStore } from '../state/catalog.store';
import { GlobalStore } from '../state/global.store';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';
import { ConnectionService } from './connection';
import { EventStream } from './events';
import { Startup, failure } from './startup';

describe('Startup', () => {
  function setup() {
    const tokenRefused$ = new Subject<void>();
    let refused = false;
    const connection = {
      desktop: null,
      connection: signal({ baseUrl: 'http://rig.local:5180', token: 'old' }),
      tokenRefused$,
      connect: vi.fn(() => Promise.resolve()),
      refused: () => Promise.resolve(refused),
      useToken: vi.fn()
    };
    const events = { start: vi.fn(), stop: vi.fn() };
    const loaded = { refresh: () => Promise.resolve() };
    TestBed.configureTestingModule({
      providers: [
        { provide: ConnectionService, useValue: connection },
        { provide: EventStream, useValue: events },
        { provide: CatalogStore, useValue: { load: () => Promise.resolve(), types: () => [{}] } },
        { provide: LiveStore, useValue: loaded },
        { provide: LibraryStore, useValue: loaded },
        { provide: GlobalStore, useValue: loaded }
      ]
    });
    return {
      startup: TestBed.inject(Startup),
      events,
      connection,
      tokenRefused$,
      refuse: (value: boolean) => (refused = value)
    };
  }

  it('asks for the token again, and stops the event stream, when the Server refuses it after the editor started', async () => {
    const { startup, events, tokenRefused$ } = setup();
    await startup.start();
    expect(startup.state()).toBe('ready');

    tokenRefused$.next();
    expect(startup.state()).toBe('token');
    expect(events.stop).toHaveBeenCalledTimes(1);

    tokenRefused$.next(); // every refused request says so; one prompt
    expect(events.stop).toHaveBeenCalledTimes(1);
  });

  it('starts again with the token typed at that prompt', async () => {
    const { startup, events, connection, tokenRefused$ } = setup();
    await startup.start();
    tokenRefused$.next();

    await startup.useToken('new');
    expect(connection.useToken).toHaveBeenCalledWith('new');
    expect(startup.state()).toBe('ready');
    expect(events.start).toHaveBeenCalledTimes(2);
  });

  it('leaves a refusal while it is still starting to its own check', async () => {
    const { startup, events, tokenRefused$, refuse } = setup();
    refuse(true);
    tokenRefused$.next();
    expect(startup.state()).toBe('starting');

    await startup.start();
    expect(startup.state()).toBe('token');
    expect(events.stop).not.toHaveBeenCalled();
  });
});

describe('startup failure', () => {
  it('reads without the wrapping of an error from the desktop app', () => {
    expect(
      failure(new Error("Error invoking remote method 'cathode:connection': Error: No answer from the Cathode server."))
    ).toBe('No answer from the Cathode server.');
    expect(failure(new Error('No answer from http://127.0.0.1:5180.'))).toBe('No answer from http://127.0.0.1:5180.');
    expect(failure('offline')).toBe('offline');
  });
});
