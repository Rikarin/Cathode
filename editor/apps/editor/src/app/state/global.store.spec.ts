import { TestBed } from '@angular/core/testing';
import type { GlobalSettings } from '@cathode/api-client';
import { Subject } from 'rxjs';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';
import { GlobalStore } from './global.store';

describe('GlobalStore.update', () => {
  it('runs quick updates one at a time, each on the answer to the one before, so both land', async () => {
    const puts: { body: GlobalSettings; answer(): void }[] = [];
    const PUT = (_path: string, init: { body: GlobalSettings }) =>
      new Promise(resolve =>
        puts.push({
          body: init.body,
          answer: () => resolve({ data: structuredClone(init.body), response: new Response('{}') })
        })
      );
    TestBed.configureTestingModule({
      providers: [
        { provide: ConnectionService, useValue: { client: { PUT } } },
        {
          provide: EventStream,
          useValue: { events$: new Subject(), connected$: new Subject(), midi$: new Subject() }
        },
        { provide: Notify, useValue: { run: (action: () => Promise<unknown>) => action(), error: () => undefined } }
      ]
    });
    const globals = TestBed.inject(GlobalStore);
    globals.settings.set({ metronome: false, tempo: 120 } as unknown as GlobalSettings);
    const settle = () => new Promise(resolve => setTimeout(resolve));

    const first = globals.update(g => (g.metronome = true));
    const second = globals.update(g => (g.tempo = 90));
    await settle();
    expect(puts).toHaveLength(1); // the second waits for the first answer

    puts[0].answer();
    await settle();
    expect(puts).toHaveLength(2);
    expect(puts[1].body).toMatchObject({ metronome: true, tempo: 90 });

    puts[1].answer();
    expect(await first).toBe(true);
    expect(await second).toBe(true);
    expect(globals.settings()).toMatchObject({ metronome: true, tempo: 90 });
  });
});
