import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import {
  eventsUrl,
  type LibraryChanged,
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
import { StaleTelemetryMs } from './engine-health';
import { newId } from './new-id';

const MaxBackoffMs = 5_000;

/** The events URL with this editor's client id, which the Server uses to release what the editor left on. */
export function clientEventsUrl(url: string, client: string): string {
  const withClient = new URL(url);
  withClient.searchParams.set('client', client);
  return withClient.toString();
}

/** Block `block`'s meter `meter` in a telemetry frame: every block has `peak`; some have their own meters too. */
export function blockMeter(frame: Telemetry | null | undefined, block: string, meter: string): number | undefined {
  return frame?.blocks?.[block]?.[meter];
}

/**
 * A block's measured CPU share (its `cpu` meter: percent of real time on the machine that runs the engine, smoothed)
 * as text, "0.4 %" or "12 %"; empty before telemetry has one.
 */
export function blockCpuText(percent: number | undefined): string {
  if (percent === undefined || !Number.isFinite(percent)) {
    return '';
  }

  return `${percent < 10 ? percent.toFixed(1) : Math.round(percent).toString()} %`;
}

/**
 * What a block's circuit solvers have given up on since the block was made, as text for the block editor: the Amp,
 * Drive and Wah blocks' `solverUnconverged`, `solverNonFinite` and `solverFailedRecomputes` meters, which are counts
 * (0 on a healthy rig), for example "12 unconverged · 1 failed recompute". Empty while every count is 0, for a block
 * without the meters and before telemetry arrives; a count that is 0 is left out.
 */
export function solverFaultsText(frame: Telemetry | null | undefined, block: string): string {
  const count = (meter: string) => {
    const value = blockMeter(frame, block, meter);
    return value !== undefined && Number.isFinite(value) && value > 0 ? Math.round(value) : 0;
  };
  const parts: string[] = [];
  const unconverged = count('solverUnconverged');
  const nonFinite = count('solverNonFinite');
  const failed = count('solverFailedRecomputes');
  if (unconverged > 0) {
    parts.push(`${unconverged} unconverged`);
  }

  if (nonFinite > 0) {
    parts.push(`${nonFinite} non-finite`);
  }

  if (failed > 0) {
    parts.push(`${failed} failed ${failed === 1 ? 'recompute' : 'recomputes'}`);
  }

  return parts.join(' · ');
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
 * far behind to receive. In a browser, a socket that closes before the Server said `connected` may have been refused
 * for its token: the stream then asks the Server, and when the token is refused it stops retrying and says so
 * ({@link ConnectionService.tokenRefused$}).
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
  /** Something stored changed (presets, setlists, banks, assets…), here or from another editor. */
  readonly library$ = new Subject<LibraryChanged>();

  /**
   * Who this editor is to the Server: the tuner, scope and RTA requests carry it (`?client=`), and so does the socket.
   * They are rig-wide and run while any client has them on; when this editor's socket closes, what it left on is
   * released, so it turns them on again after a reconnect.
   */
  readonly clientId = newId();

  private frame: Telemetry | null = null;

  /**
   * The most recent telemetry frame. Null until the first arrives, and again when there is nothing current to show:
   * the connection dropped, or the newest frame is older than {@link StaleTelemetryMs} (the engine stopped behind a
   * Server that still answers). Meters, clip marks and the tuner then read silence rather than their last frame.
   * Not a signal, on purpose.
   */
  get latest(): Telemetry | null {
    return this.frame !== null && performance.now() - this.latestAt <= StaleTelemetryMs ? this.frame : null;
  }

  /** When the newest frame arrived (`performance.now()`), and when the socket last connected: telemetry is late after. */
  latestAt = 0;
  connectedAt = 0;

  /**
   * Block `block`'s meter `meter` from the latest telemetry frame (`peak`, its output peak, linear; or one of the
   * block's own, in dB), or undefined before one arrives. The one place the editor reads block meters.
   */
  blockMeter(block: string, meter: string): number | undefined {
    return blockMeter(this.latest, block, meter);
  }

  /** Block `block`'s solver fault counts from the latest telemetry frame as text; empty when it has none. */
  solverFaults(block: string): string {
    return solverFaultsText(this.latest, block);
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
    this.status.set('closed');
    this.forgetFrames();
  }

  /** Nothing the Server sent before is current once the connection is gone. */
  private forgetFrames(): void {
    this.frame = null;
    this.scope = null;
    this.rta = null;
    this.modulation.set(null);
  }

  private open(): void {
    const connection = this.connection.connection();
    if (!connection || this.stopped) {
      return;
    }

    this.status.set('connecting');
    const socket = new WebSocket(clientEventsUrl(eventsUrl(connection), this.clientId));
    this.socket = socket;
    socket.onmessage = message => this.receive(message.data);
    socket.onclose = () => {
      if (this.socket !== socket) {
        return;
      }

      const neverConnected = this.status() !== 'open';
      this.status.set('closed');
      this.socket = null;
      this.forgetFrames();
      if (!this.stopped) {
        const delay = Math.min(MaxBackoffMs, 250 * 2 ** this.attempts++);
        this.retry = setTimeout(() => this.open(), delay);
        if (neverConnected && !this.connection.desktop) {
          void this.stopIfRefused();
        }
      }
    };
  }

  /** A refused handshake looks like any other close, so a REST request finds out whether the token is the reason. */
  private async stopIfRefused(): Promise<void> {
    if ((await this.connection.refused()) && !this.stopped) {
      this.stop();
      this.connection.tokenRefused$.next();
    }
  }

  private receive(data: unknown): void {
    if (typeof data !== 'string') {
      return;
    }

    let message: ServerMessage;
    try {
      message = JSON.parse(data) as ServerMessage;
    } catch {
      return; // not a message this editor can read: skip it rather than stop reading the stream
    }

    if (message.type === 'connected') {
      this.attempts = 0;
      this.connectedAt = performance.now();
      this.status.set('open');
      this.connected$.next();
    } else if (message.type === 'resync') {
      this.connected$.next();
    } else if (message.type === 'midi' || message.type === 'midiLearned' || message.type === 'midiLearnChanged') {
      this.midi$.next(message as MidiActivity);
    } else if (message.type === 'libraryChanged') {
      this.library$.next(message as LibraryChanged);
    } else if (message.type === 'scope') {
      this.scope = message as ScopeFrame;
    } else if (message.type === 'rta') {
      this.rta = message as RtaFrame;
    } else if (message.type === 'telemetry') {
      this.frame = message as Telemetry;
      this.latestAt = performance.now();
      const modulation = this.frame.modulation ?? null;
      if (modulation || this.modulation()) {
        this.modulation.set(modulation);
      }
    } else {
      this.events$.next(message as RigEvent);
    }
  }
}
