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
  init?: { headers?: Record<string, string>; body?: unknown } | undefined;
  resolve(state: LiveState): void;
  reject(error: Error): void;
}

/** The first of the unanswered calls; fails the test when there is none. */
function oldest(calls: readonly Call[]): Call {
  const call = calls[0];
  if (!call) {
    throw new Error('No call is waiting for an answer');
  }

  return call;
}

function state(revision: number, name = 'Preset', preset: Record<string, unknown> = {}): LiveState {
  return {
    revision,
    preset: { id: 'p1', name, blocks: [], modifiers: [], scenes: [], perform: [], footswitchOverrides: [], ...preset },
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

    oldest(open(live$)).resolve(state(5));
    await settle();
    expect(live.state()?.revision).toBe(5);
    expect(open(live$)).toHaveLength(1); // one trailing read for 6 and 7

    oldest(open(live$)).resolve(state(7));
    await first;
    expect(live.state()?.revision).toBe(7);
    expect(open(live$)).toHaveLength(0);
  });

  it('does not read again when the answer already covers the events heard meanwhile', async () => {
    const { live, events, open } = setup();
    const first = live.refresh();
    events.events$.next({ type: 'paramChanged', revision: 4 });
    oldest(open(live$)).resolve(state(4));
    await first;

    expect(open(live$)).toHaveLength(0);
  });

  it('queues one more read for refreshes asked while one is on the wire, however many', async () => {
    const { live, open, settle } = setup();
    void live.refresh();
    void live.refresh();
    const last = live.refresh();
    oldest(open(live$)).resolve(state(1));
    await settle();
    expect(open(live$)).toHaveLength(1);

    oldest(open(live$)).resolve(state(2));
    await last;
    expect(live.state()?.revision).toBe(2);
  });

  it('ignores an event for a revision it already holds', async () => {
    const { live, events, open } = setup();
    const first = live.refresh();
    oldest(open(live$)).resolve(state(3));
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
    oldest(store.open(live$)).resolve(state(revision, 'Before'));
    await first;
    return store;
  }

  it('takes the restarted Server state although its revision counter began again', async () => {
    const { live, events, open, settle } = await holding(500);

    events.connected$.next();
    oldest(open(live$)).resolve(state(1, 'After'));
    await settle();

    expect(live.state()?.revision).toBe(1);
    expect(live.preset()?.name).toBe('After');
  });

  it('follows the new count afterwards: events refetch and mutation answers are adopted', async () => {
    const { live, events, open, settle } = await holding(500);
    events.connected$.next();
    oldest(open(live$)).resolve(state(1));
    await settle();

    events.events$.next({ type: 'paramChanged', revision: 2 });
    oldest(open(live$)).resolve(state(2));
    await settle();
    expect(live.state()?.revision).toBe(2);

    const done = live.undo();
    oldest(open('/api/v1/live/undo')).resolve(state(3, 'Undone'));
    expect(await done).toBe(true);
    expect(live.preset()?.name).toBe('Undone');
  });

  it('reads again for an event of the new count that arrived during the resync read', async () => {
    const { live, events, open, settle } = await holding(500);
    events.connected$.next();
    events.events$.next({ type: 'paramChanged', revision: 2 });
    oldest(open(live$)).resolve(state(1));
    await settle();

    expect(open(live$)).toHaveLength(1);
    oldest(open(live$)).resolve(state(2));
    await settle();
    expect(live.state()?.revision).toBe(2);
  });

  it('still drops an older answer that overtakes a newer one outside a resync', async () => {
    const { live, open } = await holding(10);
    const done = live.undo();
    oldest(open('/api/v1/live/undo')).resolve(state(9, 'Stale'));
    await done;

    expect(live.state()?.revision).toBe(10);
  });
});

describe('LiveStore list replacements (optimistic concurrency)', () => {
  const live$ = '/api/v1/live';
  const modifiers$ = '/api/v1/live/modifiers';
  const lfo = { block: 'amp1', param: 'gain', source: 'lfo1' };
  const other = { block: 'delay1', param: 'mix', source: 'exp1' };
  const changed = new ApiError(412, 'The rig changed (revision 6, expected 5). Reload and try again.');

  async function holding(revision: number, preset: Record<string, unknown> = {}) {
    const store = setup();
    const first = store.live.refresh();
    oldest(store.open(live$)).resolve(state(revision, 'Preset', preset));
    await first;
    return store;
  }

  it('sends the revision the list was made from as If-Match', async () => {
    const { live, open } = await holding(5);
    const done = live.putModifier('amp1', 'gain', lfo as never);
    const put = oldest(open(modifiers$));

    expect(put.init?.headers).toEqual({ 'If-Match': '"5"' });
    expect(put.init?.body).toEqual([lfo]);
    put.resolve(state(6, 'Preset', { modifiers: [lfo] }));
    expect(await done).toBe(true);
  });

  it('sends the change again on the new revision when something else changed but the list did not', async () => {
    const { live, open, settle, errors } = await holding(5);
    const done = live.putModifier('amp1', 'gain', lfo as never);
    oldest(open(modifiers$)).reject(changed);
    await settle();
    oldest(open(live$)).resolve(state(6)); // a knob moved elsewhere: modifiers as they were
    await settle();

    const again = oldest(open(modifiers$));
    expect(again.init?.headers).toEqual({ 'If-Match': '"6"' });
    again.resolve(state(7, 'Preset', { modifiers: [lfo] }));
    expect(await done).toBe(true);
    expect(errors).toEqual([]);
  });

  it('does not delete what another client added: the change is refused and the user told', async () => {
    const { live, open, settle, errors } = await holding(5);
    const done = live.putModifier('amp1', 'gain', lfo as never);
    oldest(open(modifiers$)).reject(changed);
    await settle();
    oldest(open(live$)).resolve(state(6, 'Preset', { modifiers: [other] })); // another editor added one
    await settle();

    expect(open(modifiers$)).toHaveLength(0);
    expect(await done).toBe(false);
    expect(errors).toHaveLength(1);
    expect(live.preset()?.modifiers).toEqual([other]);
  });

  it('guards the perform page and the footswitch overrides the same way', async () => {
    const { live, open } = await holding(9);
    void live.setPerform([]);
    void live.setFootswitchOverrides([]);

    expect(oldest(open('/api/v1/live/perform')).init?.headers).toEqual({ 'If-Match': '"9"' });
    expect(oldest(open('/api/v1/live/footswitch-overrides')).init?.headers).toEqual({ 'If-Match': '"9"' });
  });
});

describe('LiveStore knob drags across a preset change', () => {
  const live$ = '/api/v1/live';
  const params$ = '/api/v1/live/blocks/{id}/params';

  it('drops values dragged on a preset that is no longer loaded instead of sending them to the new one', async () => {
    const { live, open, settle } = setup();
    const first = live.refresh();
    oldest(open(live$)).resolve(state(1));
    await first;

    live.setParam('amp1', 'A', 'gain', 3);
    live.setParam('amp1', 'A', 'gain', 4); // queued behind the PATCH in flight
    expect(open(params$)).toHaveLength(1);

    // A program change loads another preset while the first PATCH is on the wire.
    void live.load('p2');
    oldest(open('/api/v1/live/preset/{id}')).resolve(state(2, 'Other', { id: 'p2' }));
    await settle();
    oldest(open(params$)).resolve(state(3, 'Other', { id: 'p2' }));
    await settle();

    expect(open(params$)).toHaveLength(0);
    expect(live.overrides().size).toBe(0);
  });
});

describe('LiveStore knob drags (the optimistic path)', () => {
  const live$ = '/api/v1/live';
  const params$ = '/api/v1/live/blocks/{id}/params';
  const amp = { id: 'amp1', type: 'amp', row: 0, column: 1, channels: { A: { gain: 1 } }, assets: {} };

  async function holding() {
    const store = setup();
    const first = store.live.refresh();
    oldest(store.open(live$)).resolve(state(1, 'Preset', { blocks: [amp] }));
    await first;
    return store;
  }

  const shown = (live: LiveStore, param = 'gain') => {
    const block = live.preset()?.blocks[0];
    if (!block) {
      throw new Error('No block');
    }

    return live.valueOf(block, 'A', param);
  };

  it('shows a dragged value at once, before the Server has answered', async () => {
    const { live, open } = await holding();
    live.setParam('amp1', 'A', 'gain', 7);

    expect(shown(live)).toBe(7);
    expect(live.overrides().get('amp1/A/gain')).toBe(7);
    const patch = oldest(open(params$));
    expect(patch.method).toBe('PATCH');
    expect(patch.init?.body).toEqual({ gain: 7 });
  });

  it('keeps one PATCH per block and channel in flight and merges what arrives meanwhile into the next', async () => {
    const { live, open, settle } = await holding();
    live.setParam('amp1', 'A', 'gain', 2);
    live.setParam('amp1', 'A', 'gain', 3);
    live.setParam('amp1', 'A', 'bass', 4);
    live.setParam('amp1', 'A', 'gain', 5);
    expect(open(params$)).toHaveLength(1);
    expect(shown(live)).toBe(5);

    oldest(open(params$)).resolve(state(2, 'Preset', { blocks: [{ ...amp, channels: { A: { gain: 2 } } }] }));
    await settle();
    const next = open(params$);
    expect(next).toHaveLength(1);
    expect(oldest(next).init?.body).toEqual({ gain: 5, bass: 4 }); // the last value of each, in one request
    // The first answer is adopted, but the values still on their way keep showing.
    expect(live.state()?.revision).toBe(2);
    expect(shown(live)).toBe(5);
    expect(shown(live, 'bass')).toBe(4);
  });

  it('drops the overrides only when everything for the block and channel is confirmed, so the stored value shows', async () => {
    const { live, open, settle } = await holding();
    live.setParam('amp1', 'A', 'gain', 2);
    live.setParam('amp1', 'A', 'gain', 3);
    oldest(open(params$)).resolve(state(2, 'Preset', { blocks: [{ ...amp, channels: { A: { gain: 2 } } }] }));
    await settle();
    expect(live.overrides().size).toBe(1);

    oldest(open(params$)).resolve(state(3, 'Preset', { blocks: [{ ...amp, channels: { A: { gain: 3 } } }] }));
    await settle();
    expect(open(params$)).toHaveLength(0);
    expect(live.overrides().size).toBe(0);
    expect(shown(live)).toBe(3);
  });

  it('sends drags on different blocks or channels side by side', async () => {
    const { live, open } = await holding();
    live.setParam('amp1', 'A', 'gain', 2);
    live.setParam('amp1', 'B', 'gain', 3);
    live.setParam('delay1', 'A', 'mix', 40);

    const sent = open(params$).map(c => c.init?.body);
    expect(sent).toEqual([{ gain: 2 }, { gain: 3 }, { mix: 40 }]);
  });

  it('reports a refused drag and goes back to the stored value', async () => {
    const { live, open, settle, errors } = await holding();
    live.setParam('amp1', 'A', 'gain', 9);
    oldest(open(params$)).reject(new ApiError(400, 'gain is out of range'));
    await settle();

    expect(errors).toHaveLength(1);
    expect(live.overrides().size).toBe(0);
    expect(shown(live)).toBe(1);
  });

  it('sends what was queued behind a refused drag all the same', async () => {
    const { live, open, settle } = await holding();
    live.setParam('amp1', 'A', 'gain', 9);
    live.setParam('amp1', 'A', 'gain', 8);
    oldest(open(params$)).reject(new ApiError(400, 'gain is out of range'));
    await settle();

    expect(oldest(open(params$)).init?.body).toEqual({ gain: 8 });
    expect(shown(live)).toBe(8);
  });
});

describe('LiveStore revision guard', () => {
  const live$ = '/api/v1/live';

  async function holding(revision: number, preset: Record<string, unknown> = {}) {
    const store = setup();
    const first = store.live.refresh();
    oldest(store.open(live$)).resolve(state(revision, 'Before', preset));
    await first;
    return store;
  }

  it('adopts answers in revision order when two mutations come back the other way round', async () => {
    const { live, open, settle } = await holding(1);
    const first = live.setTempo(100);
    const second = live.setTempo(120);
    const [older, newer] = open('/api/v1/live/tempo');
    if (!older || !newer) {
      throw new Error('Expected two tempo requests');
    }

    newer.resolve(state(3, 'Newer'));
    await settle();
    older.resolve(state(2, 'Older'));
    expect(await first).toBe(true); // the mutation worked; its answer is just not the latest
    expect(await second).toBe(true);
    expect(live.preset()?.name).toBe('Newer');
  });

  it('drops a read that comes back older than a mutation answer adopted meanwhile', async () => {
    const { live, open, settle } = await holding(1);
    void live.refresh();
    const done = live.undo();
    oldest(open('/api/v1/live/undo')).resolve(state(4, 'Undone'));
    await done;
    oldest(open(live$)).resolve(state(3, 'Read'));
    await settle();

    expect(live.preset()?.name).toBe('Undone');
  });

  it('clears the selection when the adopted state no longer has the selected block', async () => {
    const drive = { id: 'drive1', type: 'drive', row: 0, column: 1, channels: {}, assets: {} };
    const { live, open } = await holding(1, { blocks: [drive] });
    live.selectedId.set('drive1');
    expect(live.selected()?.id).toBe('drive1');

    const done = live.removeBlock('drive1');
    oldest(open('/api/v1/live/blocks/{id}')).resolve(state(2, 'Before', { blocks: [] }));
    await done;
    expect(live.selectedId()).toBeNull();
  });

  it('takes a restarted Server state after a reconnection that happened while a read was on the wire', async () => {
    const { live, events, open, settle } = await holding(500);
    void live.refresh();
    events.connected$.next(); // the Server restarted under the read
    expect(open(live$)).toHaveLength(1);

    oldest(open(live$)).reject(new TypeError('Failed to fetch')); // the old Server went away mid-read
    await settle();
    expect(live.state()?.revision).toBe(500);

    // The next event (the new Server's first) still reads, and the answer is taken despite its low revision.
    events.events$.next({ type: 'paramChanged', revision: 1 });
    oldest(open(live$)).resolve(state(1, 'After'));
    await settle();
    expect(live.preset()?.name).toBe('After');
  });

  it('reads the new Server after the read that was on the wire during the reconnection returns', async () => {
    const { live, events, open, settle } = await holding(500);
    void live.refresh();
    events.connected$.next();
    oldest(open(live$)).resolve(state(501, 'Old Server')); // answered by the old Server just before it stopped
    await settle();

    expect(open(live$)).toHaveLength(1); // the resync read
    oldest(open(live$)).resolve(state(2, 'New Server'));
    await settle();
    expect(live.state()?.revision).toBe(2);
    expect(live.preset()?.name).toBe('New Server');
  });
});

describe('LiveStore under a burst of events', () => {
  const live$ = '/api/v1/live';

  it('reads once for a burst heard while idle, and once more only if the answer is behind it', async () => {
    const { live, events, open, settle } = setup();
    const first = live.refresh();
    oldest(open(live$)).resolve(state(1));
    await first;

    for (let revision = 2; revision <= 40; revision++) {
      events.events$.next({ type: 'paramChanged', revision });
    }

    expect(open(live$)).toHaveLength(1);
    oldest(open(live$)).resolve(state(25)); // read while the burst was still being applied
    await settle();
    expect(open(live$)).toHaveLength(1);
    oldest(open(live$)).resolve(state(40));
    await settle();

    expect(open(live$)).toHaveLength(0);
    expect(live.state()?.revision).toBe(40);
  });

  it('keeps reading after a failed read: the next event starts a new one', async () => {
    const { live, events, open, settle, errors } = setup();
    const first = live.refresh();
    oldest(open(live$)).reject(new TypeError('Failed to fetch'));
    await first;
    expect(errors).toHaveLength(1);

    events.events$.next({ type: 'paramChanged', revision: 3 });
    expect(open(live$)).toHaveLength(1);
    oldest(open(live$)).resolve(state(3));
    await settle();
    expect(live.state()?.revision).toBe(3);
  });

  it('ignores events from before a reconnection once the new Server has been read', async () => {
    const { live, events, open, settle } = setup();
    const first = live.refresh();
    oldest(open(live$)).resolve(state(300));
    await first;
    events.events$.next({ type: 'paramChanged', revision: 301 });
    events.connected$.next(); // the Server restarted while the read for 301 was on the wire
    oldest(open(live$)).resolve(state(300)); // the old Server answered without 301
    await settle();
    oldest(open(live$)).resolve(state(1)); // the resync read
    await settle();

    // 301 belonged to the Server that is gone: the store does not chase it.
    expect(open(live$)).toHaveLength(0);
    expect(live.state()?.revision).toBe(1);
  });
});

describe('LiveStore.tap', () => {
  it('reports a refused tap and resolves to false instead of rejecting', async () => {
    const { live, open, errors } = setup();
    const tapped = live.tap();
    oldest(open('/api/v1/live/tap')).reject(new ApiError(503, 'The engine is not connected.'));

    await expect(tapped).resolves.toBe(false);
    expect(errors).toHaveLength(1);
  });
});
