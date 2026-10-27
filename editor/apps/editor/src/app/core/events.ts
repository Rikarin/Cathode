import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import {
  eventsUrl,
  type MidiActivity,
  type Modulation,
  type RigEvent,
  type RtaFrame,
  type ScopeFrame,
  type ServerMessage,
  type Telemetry
} from '@cathode/api-client';
import { Subject } from 'rxjs';
import { ConnectionService } from './connection';

const MaxBackoffMs = 5_000;

/** Block `block`'s meter `meter` in a telemetry frame: every block has `peak`; some have their own meters too. */
export function blockMeter(frame: Telemetry | null | undefined, block: string, meter: string): number | undefined {
  return frame?.blocks?.[block]?.[meter];
}

/**
 * The live value of block `block`'s parameter `param` while a modifier drives it, or undefined when none does (or
 * before telemetry arrives).
 */
export function modulatedValue(modulation: Modulation | null | undefined, block: string, param: string) {
  const value = modulation?.[block]?.[param];
  return value !== undefined && Number.isFinite(value) ? value : undefined;
}

/**
 * The Server's WebSocket: rig events (signalled to the stores) and telemetry. Telemetry deliberately bypasses
 * change detection: meters read {@link latest} from their own animation frames, about 30 updates a second.
 * Reconnects with backoff; every (re)connection emits on {@link connected$} so the stores resync rather than trust
 * that no event was missed. So does a `resync` message: the Server sends it in place of events this client fell too
 * far behind to receive.
 */
@Injectable({ providedIn: 'root' })
export class EventStream {
  private readonly connection = inject(ConnectionService);
  private socket: WebSocket | null = null;
  private retry: ReturnType<typeof setTimeout> | null = null;
  private attempts = 0;
  private stopped = false;

  readonly status = signal<'connecting' | 'open' | 'closed'>('closed');
  readonly events$ = new Subject<RigEvent>();
  readonly connected$ = new Subject<void>();
  readonly midi$ = new Subject<MidiActivity>();

  /** The most recent telemetry frame (null until the first arrives). Not a signal, on purpose. */
  latest: Telemetry | null = null;
  latestAt = 0;

  /**
   * Block `block`'s meter `meter` from the latest telemetry frame (`peak`, its output peak, linear; or one of the
   * block's own, in dB), or undefined before one arrives. The one place the editor reads block meters.
   */
  blockMeter(block: string, meter: string): number | undefined {
    return blockMeter(this.latest, block, meter);
  }

  /**
   * The modulated parameters of the latest telemetry frame (null when none is modulated). A signal, unlike
   * {@link latest}: knobs follow their modifier through it. It changes only while something is modulated, about 30
   * times a second, and a control's `computed` over it only marks its view when its own value moved.
   */
  readonly modulation = signal<Modulation | null>(null);

  /** The live value of `block`'s parameter `param` while a modifier drives it; reactive (see {@link modulation}). */
  modulatedValue(block: string, param: string): number | undefined {
    return modulatedValue(this.modulation(), block, param);
  }

  /** The most recent scope frame, while the scope is on. Not a signal either. */
  scope: ScopeFrame | null = null;

  /** The most recent RTA frame, while the RTA is on. Not a signal either. */
  rta: RtaFrame | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stop());
  }

  start(): void {
    this.stopped = false;
    this.open();
  }

  stop(): void {
    this.stopped = true;
    if (this.retry) {
      clearTimeout(this.retry);
    }

    this.socket?.close();
    this.socket = null;
  }

  private open(): void {
    const connection = this.connection.connection();
    if (!connection || this.stopped) {
      return;
    }

    this.status.set('connecting');
    const socket = new WebSocket(eventsUrl(connection));
    this.socket = socket;
    socket.onmessage = message => this.receive(message.data);
    socket.onclose = () => {
      if (this.socket !== socket) {
        return;
      }

      this.status.set('closed');
      this.socket = null;
      this.modulation.set(null);
      if (!this.stopped) {
        const delay = Math.min(MaxBackoffMs, 250 * 2 ** this.attempts++);
        this.retry = setTimeout(() => this.open(), delay);
      }
    };
  }

  private receive(data: unknown): void {
    if (typeof data !== 'string') {
      return;
    }

    const message = JSON.parse(data) as ServerMessage;
    if (message.type === 'connected') {
      this.attempts = 0;
      this.status.set('open');
      this.connected$.next();
    } else if (message.type === 'resync') {
      this.connected$.next();
    } else if (message.type === 'midi' || message.type === 'midiLearned') {
      this.midi$.next(message as MidiActivity);
    } else if (message.type === 'scope') {
      this.scope = message as ScopeFrame;
    } else if (message.type === 'rta') {
      this.rta = message as RtaFrame;
    } else if (message.type === 'telemetry') {
      this.latest = message as Telemetry;
      this.latestAt = performance.now();
      const modulation = this.latest.modulation ?? null;
      if (modulation || this.modulation()) {
        this.modulation.set(modulation);
      }
    } else {
      this.events$.next(message as RigEvent);
    }
  }
}
