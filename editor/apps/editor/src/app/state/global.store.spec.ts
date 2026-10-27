import { TestBed } from '@angular/core/testing';
import type { GlobalSettings } from '@cathode/api-client';
import { Subject } from 'rxjs';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';
import { GlobalStore, refreshesGlobals } from './global.store';

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

    puts[0]?.answer();
    await settle();
    expect(puts).toHaveLength(2);
    expect(puts[1]?.body).toMatchObject({ metronome: true, tempo: 90 });

    puts[1]?.answer();
    expect(await first).toBe(true);
    expect(await second).toBe(true);
    expect(globals.settings()).toMatchObject({ metronome: true, tempo: 90 });
  });
});

describe('GlobalStore revision', () => {
  it('sends the revision it holds and, when another client changed the settings first, makes the change on theirs', async () => {
    const reply = (body: unknown, status: number, etag?: string) =>
      new Response(JSON.stringify(body), {
        status,
        headers: { 'Content-Type': 'application/json', ...(etag ? { ETag: etag } : {}) }
      });
    // The Server: another client turned the metronome on at revision 8, after this editor read revision 7.
    let stored = { metronome: true, tempo: 120 };
    let revision = 8;
    const sent: { body: GlobalSettings; ifMatch: string | undefined }[] = [];
    const GET = () => Promise.resolve({ data: structuredClone(stored), response: reply(stored, 200, `"${revision}"`) });
    const PUT = (_path: string, init: { body: GlobalSettings; headers: Record<string, string> }) => {
      sent.push({ body: init.body, ifMatch: init.headers['If-Match'] });
      if (init.headers['If-Match'] !== `"${revision}"`) {
        const response = reply({ status: 412, detail: 'The global settings changed.' }, 412);
        return Promise.resolve({ error: { status: 412, detail: 'The global settings changed.' }, response });
      }

      stored = structuredClone(init.body);
      revision++;
      return Promise.resolve({ data: structuredClone(stored), response: reply(stored, 200, `"${revision}"`) });
    };
    const error = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        { provide: ConnectionService, useValue: { client: { GET, PUT } } },
        {
          provide: EventStream,
          useValue: { events$: new Subject(), connected$: new Subject(), midi$: new Subject() }
        },
        { provide: Notify, useValue: { run: (action: () => Promise<unknown>) => action(), error } }
      ]
    });
    const globals = TestBed.inject(GlobalStore);
    stored = { metronome: false, tempo: 120 };
    revision = 7;
    await globals.refresh(); // holds revision 7
    stored = { metronome: true, tempo: 120 };
    revision = 8;

    expect(await globals.update(g => (g.tempo = 90))).toBe(true);

    expect(sent.map(s => s.ifMatch)).toEqual(['"7"', '"8"']);
    expect(sent[1]?.body).toMatchObject({ metronome: true, tempo: 90 }); // the other change is kept
    expect(globals.settings()).toMatchObject({ metronome: true, tempo: 90 });
    expect(error).not.toHaveBeenCalled();

    // The next update goes out on the revision the last answer carried.
    await globals.update(g => (g.tempo = 100));
    expect(sent.at(-1)?.ifMatch).toBe('"9"');
  });
});

describe('refreshesGlobals', () => {
  it('refetches after any change to the global settings, whatever made it', () => {
    expect(refreshesGlobals({ type: 'globalChanged', revision: 3 })).toBe(true);
    expect(refreshesGlobals({ type: 'tempoChanged', revision: 4 })).toBe(true);
    expect(refreshesGlobals({ type: 'globalLinked', revision: 5, block: 'amp1' })).toBe(true);
  });

  it('leaves the global settings alone for edits of the working copy', () => {
    expect(refreshesGlobals({ type: 'paramChanged', revision: 6, block: 'amp1' })).toBe(false);
    expect(refreshesGlobals({ type: 'sceneChanged', revision: 7, scene: 1 })).toBe(false);
  });
});
