import { TestBed } from '@angular/core/testing';
import { ApiError, type LiveState, type RigEvent } from '@cathode/api-client';
import { Subject } from 'rxjs';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';
import { LiveStore } from './live.store';

/** A request the fake client has received and not yet answered. */
interface Call {
  method: string;
  path: string;
  init?: { headers?: Record<string, string>; body?: unknown };
  resolve(state: LiveState): void;
  reject(error: unknown): void;
}

function state(revision: number, name = 'Preset'): LiveState {
  return {
    revision,
    preset: { id: 'p1', name, blocks: [], modifiers: [], scenes: [] },
    activeScene: 0
  } as unknown as LiveState;
}

function setup() {
  const calls: Call[] = [];
  const request = (method: string) => (path: string, init?: Call['init']) =>
    new Promise((resolve, reject) => {
      const call: Call = {
        method,
        path,
        init,
        resolve: data => {
          calls.splice(calls.indexOf(call), 1);
          resolve({ data, response: new Response(null, { status: 200 }) });
        },
        reject: error => {
          calls.splice(calls.indexOf(call), 1);
          reject(error);
        }
      };
      calls.push(call);
    });
  const client = {
    GET: request('GET'),
    POST: request('POST'),
    PUT: request('PUT'),
    PATCH: request('PATCH'),
    DELETE: request('DELETE')
  };
  const events = { events$: new Subject<RigEvent>(), connected$: new Subject<void>() };
  const errors: unknown[] = [];
  const notify = {
    error: (error: unknown) => errors.push(error),
    run: async <T>(action: () => Promise<T>) => {
      try {
        return await action();
      } catch (error) {
        errors.push(error);
        return undefined;
      }
    }
  };
  TestBed.configureTestingModule({
    providers: [
      { provide: ConnectionService, useValue: { client } },
      { provide: EventStream, useValue: events },
      { provide: Notify, useValue: notify }
    ]
  });
  const live = TestBed.inject(LiveStore);
  /** The unanswered calls to `path`, oldest first. */
  const open = (path: string) => calls.filter(c => c.path === path);
  /** Lets every settled promise run its continuations. */
  const settle = () => new Promise(resolve => setTimeout(resolve));
  return { live, calls, events, errors, open, settle };
}

describe('LiveStore.refresh', () => {
  const live$ = '/api/v1/live';

  it('reads again when an event announces a revision newer than the read on the wire brings back', async () => {
    const { live, events, open, settle } = setup();
    const first = live.refresh();
    events.events$.next({ type: 'paramChanged', revision: 6 });
    events.events$.next({ type: 'paramChanged', revision: 7 });
    expect(open(live$)).toHaveLength(1); // coalesced while on the wire

    open(live$)[0].resolve(state(5));
    await settle();
    expect(live.state()?.revision).toBe(5);
    expect(open(live$)).toHaveLength(1); // one trailing read for 6 and 7

    open(live$)[0].resolve(state(7));
    await first;
    expect(live.state()?.revision).toBe(7);
    expect(open(live$)).toHaveLength(0);
  });

  it('does not read again when the answer already covers the events heard meanwhile', async () => {
    const { live, events, open } = setup();
    const first = live.refresh();
    events.events$.next({ type: 'paramChanged', revision: 4 });
    open(live$)[0].resolve(state(4));
    await first;

    expect(open(live$)).toHaveLength(0);
  });

  it('queues one more read for refreshes asked while one is on the wire, however many', async () => {
    const { live, open, settle } = setup();
    void live.refresh();
    void live.refresh();
    const last = live.refresh();
    open(live$)[0].resolve(state(1));
    await settle();
    expect(open(live$)).toHaveLength(1);

    open(live$)[0].resolve(state(2));
    await last;
    expect(live.state()?.revision).toBe(2);
  });

  it('ignores an event for a revision it already holds', async () => {
    const { live, events, open } = setup();
    const first = live.refresh();
    open(live$)[0].resolve(state(3));
    await first;

    events.events$.next({ type: 'paramChanged', revision: 3 });
    expect(open(live$)).toHaveLength(0);
    events.events$.next({ type: 'paramChanged', revision: 4 });
    expect(open(live$)).toHaveLength(1);
  });
});

describe('LiveStore after a reconnection', () => {
  const live$ = '/api/v1/live';

  async function holding(revision: number) {
    const store = setup();
    const first = store.live.refresh();
    store.open(live$)[0].resolve(state(revision, 'Before'));
    await first;
    return store;
  }

  it('takes the restarted Server state although its revision counter began again', async () => {
    const { live, events, open, settle } = await holding(500);

    events.connected$.next();
    open(live$)[0].resolve(state(1, 'After'));
    await settle();

    expect(live.state()?.revision).toBe(1);
    expect(live.preset()?.name).toBe('After');
  });

  it('follows the new count afterwards: events refetch and mutation answers are adopted', async () => {
    const { live, events, open, settle } = await holding(500);
    events.connected$.next();
    open(live$)[0].resolve(state(1));
    await settle();

    events.events$.next({ type: 'paramChanged', revision: 2 });
    open(live$)[0].resolve(state(2));
    await settle();
    expect(live.state()?.revision).toBe(2);

    const done = live.undo();
    open('/api/v1/live/undo')[0].resolve(state(3, 'Undone'));
    expect(await done).toBe(true);
    expect(live.preset()?.name).toBe('Undone');
  });

  it('reads again for an event of the new count that arrived during the resync read', async () => {
    const { live, events, open, settle } = await holding(500);
    events.connected$.next();
    events.events$.next({ type: 'paramChanged', revision: 2 });
    open(live$)[0].resolve(state(1));
    await settle();

    expect(open(live$)).toHaveLength(1);
    open(live$)[0].resolve(state(2));
    await settle();
    expect(live.state()?.revision).toBe(2);
  });

  it('still drops an older answer that overtakes a newer one outside a resync', async () => {
    const { live, open } = await holding(10);
    const done = live.undo();
    open('/api/v1/live/undo')[0].resolve(state(9, 'Stale'));
    await done;

    expect(live.state()?.revision).toBe(10);
  });
});

describe('LiveStore.tap', () => {
  it('reports a refused tap and resolves to false instead of rejecting', async () => {
    const { live, open, errors } = setup();
    const tapped = live.tap();
    open('/api/v1/live/tap')[0].reject(new ApiError(503, 'The engine is not connected.'));

    await expect(tapped).resolves.toBe(false);
    expect(errors).toHaveLength(1);
  });
});
