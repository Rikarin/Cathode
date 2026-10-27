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

describe('LiveStore.tap', () => {
  it('reports a refused tap and resolves to false instead of rejecting', async () => {
    const { live, open, errors } = setup();
    const tapped = live.tap();
    open('/api/v1/live/tap')[0].reject(new ApiError(503, 'The engine is not connected.'));

    await expect(tapped).resolves.toBe(false);
    expect(errors).toHaveLength(1);
  });
});
