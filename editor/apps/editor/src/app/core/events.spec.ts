import { computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { Telemetry } from '@cathode/api-client';
import { ConnectionService } from './connection';
import { EventStream, blockMeter, modulatedValue } from './events';

describe('blockMeter', () => {
  const frame = {
    type: 'telemetry',
    blocks: { in1: { peak: 0.8 }, cab1: { peak: 0.4, preampIn: -6, motorReduction: 1.5 } }
  } as unknown as Telemetry;

  it('reads a block peak and its own meters from the one blocks section', () => {
    expect(blockMeter(frame, 'in1', 'peak')).toBe(0.8);
    expect(blockMeter(frame, 'cab1', 'peak')).toBe(0.4);
    expect(blockMeter(frame, 'cab1', 'preampIn')).toBe(-6);
    expect(blockMeter(frame, 'cab1', 'motorReduction')).toBe(1.5);
  });

  it('is undefined before a frame, for an unknown block and for a meter the block does not have', () => {
    expect(blockMeter(null, 'cab1', 'peak')).toBeUndefined();
    expect(blockMeter(frame, 'delay1', 'peak')).toBeUndefined();
    expect(blockMeter(frame, 'in1', 'preampIn')).toBeUndefined();
  });
});

describe('modulatedValue', () => {
  const modulation = { delay1: { mix: 42.5, feedback: 10 }, amp1: { gain: 0 } };

  it('reads a modulated parameter by block and parameter id', () => {
    expect(modulatedValue(modulation, 'delay1', 'mix')).toBe(42.5);
    expect(modulatedValue(modulation, 'amp1', 'gain')).toBe(0);
  });

  it('is undefined when nothing modulates the parameter or the value is not finite', () => {
    expect(modulatedValue(null, 'delay1', 'mix')).toBeUndefined();
    expect(modulatedValue(modulation, 'delay1', 'time')).toBeUndefined();
    expect(modulatedValue(modulation, 'reverb1', 'mix')).toBeUndefined();
    expect(modulatedValue({ delay1: { mix: Number.NaN } }, 'delay1', 'mix')).toBeUndefined();
  });
});

describe('EventStream resync', () => {
  it('asks the stores to read their state again, as a (re)connection does, instead of passing it on as an event', () => {
    TestBed.configureTestingModule({ providers: [{ provide: ConnectionService, useValue: {} }] });
    const stream = TestBed.inject(EventStream);
    let resyncs = 0;
    const events: string[] = [];
    stream.connected$.subscribe(() => resyncs++);
    stream.events$.subscribe(e => events.push(e.type));

    (stream as unknown as { receive(data: unknown): void }).receive(JSON.stringify({ type: 'resync' }));

    expect(resyncs).toBe(1);
    expect(events).toEqual([]);
  });
});

describe('EventStream.modulation', () => {
  function receive(stream: EventStream, message: object): void {
    (stream as unknown as { receive(data: unknown): void }).receive(JSON.stringify(message));
  }

  it('follows the telemetry frames and clears when no parameter is modulated any more', () => {
    TestBed.configureTestingModule({ providers: [{ provide: ConnectionService, useValue: {} }] });
    const stream = TestBed.inject(EventStream);
    const mix = TestBed.runInInjectionContext(() => computed(() => stream.modulatedValue('delay1', 'mix')));

    receive(stream, { type: 'telemetry', modulation: { delay1: { mix: 30 } } });
    expect(mix()).toBe(30);
    receive(stream, { type: 'telemetry', modulation: { delay1: { mix: 31.5 } } });
    expect(mix()).toBe(31.5);
    receive(stream, { type: 'telemetry' });
    expect(mix()).toBeUndefined();
    expect(stream.modulation()).toBeNull();
  });
});

/** A WebSocket the test drives: it never touches the network, and closes only when the test says so. */
class FakeSocket {
  static readonly opened: FakeSocket[] = [];
  onmessage: ((message: { data: unknown }) => void) | null = null;
  onclose: (() => void) | null = null;
  closed = false;

  constructor(readonly url: string) {
    FakeSocket.opened.push(this);
  }

  close(): void {
    this.closed = true; // a browser fires close later, as `drop` does
  }

  /** The Server says hello: what it sends first on every connection. */
  hello(): void {
    this.send({ type: 'connected' });
  }

  send(message: object): void {
    this.onmessage?.({ data: JSON.stringify(message) });
  }

  /** The connection is lost (or the close asked for completes). */
  drop(): void {
    this.closed = true;
    this.onclose?.();
  }
}

describe('EventStream connection', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.stubGlobal('WebSocket', FakeSocket);
    FakeSocket.opened.length = 0;
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  function start() {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: ConnectionService,
          useValue: { connection: signal({ baseUrl: 'http://rig.local:5180', token: null }) }
        }
      ]
    });
    const stream = TestBed.inject(EventStream);
    let connections = 0;
    stream.connected$.subscribe(() => connections++);
    stream.start();
    /** The socket opened last; fails the test when none is. */
    const socket = () => {
      const last = FakeSocket.opened.at(-1);
      if (!last) {
        throw new Error('No socket was opened');
      }

      return last;
    };
    return { stream, socket, connections: () => connections };
  }

  it('opens the events socket with its client id and is open once the Server says connected', () => {
    const { stream, socket, connections } = start();
    expect(socket().url).toBe(`ws://rig.local:5180/ws?client=${stream.clientId}`);
    expect(stream.status()).toBe('connecting');
    expect(connections()).toBe(0);

    socket().hello();
    expect(stream.status()).toBe('open');
    expect(connections()).toBe(1);
  });

  it('opens nothing before there is a connection to the Server', () => {
    TestBed.configureTestingModule({
      providers: [{ provide: ConnectionService, useValue: { connection: signal(null) } }]
    });
    const stream = TestBed.inject(EventStream);
    stream.start();

    expect(FakeSocket.opened).toHaveLength(0);
    expect(stream.status()).toBe('closed');
  });

  it('goes closed when the socket drops and opens a new one after a backoff that doubles up to 5 s', () => {
    const { stream, socket } = start();
    socket().hello();

    const delays: number[] = [];
    for (let attempt = 0; attempt < 7; attempt++) {
      const count = FakeSocket.opened.length;
      socket().drop(); // never says connected again: the Server is down
      expect(stream.status()).toBe('closed');
      let waited = 0;
      while (FakeSocket.opened.length === count) {
        vi.advanceTimersByTime(50);
        waited += 50;
        if (waited > 10_000) {
          throw new Error('No reconnection within 10 s');
        }
      }

      expect(stream.status()).toBe('connecting');
      delays.push(waited);
    }

    expect(delays).toEqual([250, 500, 1000, 2000, 4000, 5000, 5000]);
  });

  it('backs off from the start again once a connection succeeds', () => {
    const { socket } = start();
    socket().drop();
    vi.advanceTimersByTime(250);
    socket().drop();
    vi.advanceTimersByTime(500);
    expect(FakeSocket.opened).toHaveLength(3);

    socket().hello();
    socket().drop();
    vi.advanceTimersByTime(249);
    expect(FakeSocket.opened).toHaveLength(3);
    vi.advanceTimersByTime(1);
    expect(FakeSocket.opened).toHaveLength(4);
  });

  it('signals every reconnection, so the stores read their state again, and keeps its client id', () => {
    const { stream, socket, connections } = start();
    socket().hello();
    const first = socket();

    first.drop();
    vi.advanceTimersByTime(250);
    const second = socket();
    expect(second).not.toBe(first);
    expect(connections()).toBe(1); // not before the new socket is actually connected

    second.hello();
    expect(connections()).toBe(2);
    expect(stream.status()).toBe('open');
    // The Server keys what this editor turned on (tuner, scope, RTA) by the id; it is the same after a reconnection.
    expect(second.url).toBe(first.url);
  });

  it('passes events on from the new socket after a reconnection, and nothing more from the old one', () => {
    const { stream, socket } = start();
    const seen: number[] = [];
    stream.events$.subscribe(e => seen.push(e.revision));
    const first = socket();
    first.hello();
    first.send({ type: 'paramChanged', revision: 1 });
    first.drop();
    vi.advanceTimersByTime(250);
    socket().hello();
    socket().send({ type: 'paramChanged', revision: 2 });

    expect(seen).toEqual([1, 2]);
  });

  it('forgets the modulated values when the socket drops: no knob keeps following a stale frame', () => {
    const { stream, socket } = start();
    socket().hello();
    socket().send({ type: 'telemetry', modulation: { delay1: { mix: 30 } } });
    expect(stream.modulatedValue('delay1', 'mix')).toBe(30);

    socket().drop();
    expect(stream.modulation()).toBeNull();
  });

  it('stops for good: closes the socket and opens no other, also when its close arrives later', () => {
    const { stream, socket } = start();
    socket().hello();
    const open = socket();

    stream.stop();
    expect(open.closed).toBe(true);
    open.drop();
    vi.advanceTimersByTime(10_000);
    expect(FakeSocket.opened).toHaveLength(1);
  });

  it('cancels a reconnection that is waiting when stopped', () => {
    const { stream, socket } = start();
    socket().drop();
    stream.stop();
    vi.advanceTimersByTime(10_000);

    expect(FakeSocket.opened).toHaveLength(1);
  });
});

describe('EventStream.receive', () => {
  it('skips a message that is not JSON and goes on reading the stream', () => {
    TestBed.configureTestingModule({ providers: [{ provide: ConnectionService, useValue: {} }] });
    const stream = TestBed.inject(EventStream);
    const receive = (data: string) => (stream as unknown as { receive(data: unknown): void }).receive(data);
    const seen: string[] = [];
    stream.events$.subscribe(e => seen.push(e.type));

    expect(() => receive('{"type":"paramCh')).not.toThrow();
    receive(JSON.stringify({ type: 'paramChanged', revision: 3 }));

    expect(seen).toEqual(['paramChanged']);
  });
});
