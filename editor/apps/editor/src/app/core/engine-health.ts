import { Injectable, computed, inject, signal } from '@angular/core';
import { ConnectionService } from './connection';
import type { SidecarStatus } from './desktop';

/** Telemetry comes about 30 times a second; this long without a frame means the engine has stopped. */
export const StaleTelemetryMs = 2_000;

export interface EngineHealthState {
  presence: 'online' | 'idle' | 'dnd';
  text: string;
}

/**
 * What the connection light says. The WebSocket being open only means the Server answers: the engine behind it can be
 * down while the desktop app restarts its host (`host`, the sidecar's status there), or quiet for another reason
 * (`stale`: no telemetry for {@link StaleTelemetryMs}), which a remote Server shows the same way.
 */
export function engineHealth(
  socket: 'connecting' | 'open' | 'closed',
  host: SidecarStatus | undefined,
  stale: boolean
): EngineHealthState {
  if (socket === 'closed') {
    return { presence: 'dnd', text: 'Disconnected: retrying' };
  }

  if (socket === 'connecting') {
    return { presence: 'idle', text: 'Connecting…' };
  }

  if (host?.state === 'failed') {
    return { presence: 'dnd', text: `The engine failed to start${host.detail ? `: ${host.detail}` : ''}` };
  }

  if (host?.state === 'restarting') {
    return { presence: 'idle', text: `The engine stopped${host.detail ? `: ${host.detail}` : '; restarting'}` };
  }

  if (host?.state === 'starting') {
    return { presence: 'idle', text: 'The engine is starting…' };
  }

  if (stale) {
    return { presence: 'idle', text: 'Connected, but the engine sends no meters: it may have stopped' };
  }

  return { presence: 'online', text: 'Connected to the engine' };
}

/**
 * The desktop app's engine processes (cathode-host and cathode-server) as it supervises them, kept current; empty in a
 * browser, or with a remote Server.
 */
@Injectable({ providedIn: 'root' })
export class SidecarStatuses {
  private readonly desktop = inject(ConnectionService).desktop;
  readonly statuses = signal<SidecarStatus[]>([]);
  readonly host = computed(() => this.statuses().find(s => s.name === 'host'));

  constructor() {
    this.desktop?.onSidecars?.(statuses => this.statuses.set(statuses));
    void this.desktop
      ?.sidecars()
      .then(statuses => this.statuses.set(statuses))
      .catch(() => undefined);
  }

  /** The last lines each process printed: why it stopped, usually. */
  logs(): Promise<Record<'host' | 'server', string[]>> {
    return this.desktop?.logs?.() ?? Promise.resolve({ host: [], server: [] });
  }
}
