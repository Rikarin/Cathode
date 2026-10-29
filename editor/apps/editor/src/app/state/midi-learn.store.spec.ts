import { TestBed } from '@angular/core/testing';
import type { MidiActivity, MidiLearnRequest } from '@cathode/api-client';
import { XuiToastService } from '@xui/toast';
import { Subject } from 'rxjs';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';
import { MidiLearnStore, learnText } from './midi-learn.store';

const knob: MidiLearnRequest = {
  action: 'external',
  target: null,
  value: 2,
  modifier: {
    block: 'amp1',
    param: 'gain',
    source: 'ext2',
    min: 0,
    max: 10,
    curve: 'linear',
    attackMs: 0,
    releaseMs: 0
  }
};

describe('learnText', () => {
  it('names the action, or the parameter a knob learn will drive', () => {
    expect(learnText({ action: 'bypass', target: 'delay1', modifier: null })).toBe('bypass delay1');
    expect(learnText({ action: 'scene', value: 2, modifier: null })).toBe('scene 3');
    expect(learnText(knob)).toBe('amp1 gain (External 2)');
  });
});

describe('MidiLearnStore', () => {
  function setup(armedOnServer: MidiLearnRequest | null) {
    const midi$ = new Subject<MidiActivity>();
    const connected$ = new Subject<void>();
    const calls: string[] = [];
    const toasts: { message: string; actionText?: string; onAction?: () => void }[] = [];
    const dismissed: number[] = [];
    let server = armedOnServer;
    const reply = (data: unknown) => Promise.resolve({ data, response: new Response(null, { status: 200 }) });
    const client = {
      GET: () => {
        calls.push('GET');
        return reply(server ?? undefined);
      },
      PUT: (_path: string, init: { body: MidiLearnRequest }) => {
        calls.push('PUT');
        server = init.body;
        return reply(undefined);
      },
      DELETE: () => {
        calls.push('DELETE');
        server = null;
        return reply(undefined);
      }
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: ConnectionService, useValue: { client } },
        { provide: EventStream, useValue: { connected$, midi$ } },
        { provide: Notify, useValue: { run: (action: () => Promise<unknown>) => action(), error: () => undefined } },
        {
          provide: XuiToastService,
          useValue: {
            show: (toast: (typeof toasts)[number]) => toasts.push(toast),
            dismiss: (id: number) => dismissed.push(id)
          }
        }
      ]
    });
    const store = TestBed.inject(MidiLearnStore);
    connected$.next(); // the event stream's first `connected`
    return { store, midi$, calls, toasts, dismissed };
  }

  const settle = async () => {
    await new Promise(resolve => setTimeout(resolve));
    TestBed.tick();
  };

  it('reads what the Server has armed, so a reload still shows it with Cancel', async () => {
    const { store, calls, toasts } = setup(knob);
    await settle();

    expect(calls).toEqual(['GET']);
    expect(store.armed()).toEqual(knob);
    expect(toasts).toHaveLength(1);
    expect(toasts[0]?.message).toContain('amp1 gain (External 2)');
    expect(toasts[0]?.actionText).toBe('Cancel');
  });

  it('cancels from the toast, and clears once something is learnt', async () => {
    const { store, midi$, calls, toasts, dismissed } = setup(knob);
    await settle();

    toasts[0]?.onAction?.();
    await settle();
    expect(calls).toEqual(['GET', 'DELETE']);
    expect(store.armed()).toBeNull();
    expect(dismissed).toEqual([1]);

    expect(await store.arm({ action: 'tuner', modifier: null })).toBe(true);
    await settle();
    expect(store.armed()).toEqual({ action: 'tuner', modifier: null });
    midi$.next({ type: 'midiLearned', status: 0xb0, data1: 1, data2: 127 });
    await settle();
    expect(store.armed()).toBeNull();
    expect(dismissed).toHaveLength(2);
  });

  it('reads again when another editor arms or cancels', async () => {
    const { midi$, calls } = setup(null);
    await settle();

    midi$.next({ type: 'midiLearnChanged', status: 0, data1: 0, data2: 0 });
    await settle();
    expect(calls).toEqual(['GET', 'GET']);
  });
});
