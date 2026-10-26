import { Injectable, inject, signal } from '@angular/core';
import { CatalogStore } from '../state/catalog.store';
import { GlobalStore } from '../state/global.store';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';
import { ConnectionService } from './connection';
import type { ServerTarget } from './desktop';
import { EventStream } from './events';

/** Connects to the Server, then loads the catalogue, the live rig and the library, and opens the event stream. */
@Injectable({ providedIn: 'root' })
export class Startup {
  private readonly connection = inject(ConnectionService);
  private readonly events = inject(EventStream);
  private readonly catalog = inject(CatalogStore);
  private readonly live = inject(LiveStore);
  private readonly library = inject(LibraryStore);
  private readonly globals = inject(GlobalStore);

  /** `token`: a browser reached a Server that wants an access token it does not have (or has a wrong one). */
  readonly state = signal<'starting' | 'ready' | 'failed' | 'token'>('starting');
  readonly error = signal<string | null>(null);
  /** In the desktop app, which Server it uses: the startup screen offers a way back to this computer's engine. */
  readonly target = signal<ServerTarget | null>(null);

  async start(): Promise<void> {
    this.state.set('starting');
    try {
      this.target.set((await this.connection.desktop?.target()) ?? null);
      await this.connection.connect();
      if (!this.connection.desktop && (await this.connection.refused())) {
        this.state.set('token');
        return;
      }

      await this.catalog.load();
      if (this.catalog.types().length === 0) {
        throw new Error(`No answer from ${this.connection.connection()?.baseUrl}. Is cathode-server running?`);
      }

      await Promise.all([this.live.refresh(), this.library.refresh(), this.globals.refresh()]);
      this.events.start();
      this.state.set('ready');
    } catch (error) {
      this.error.set(failure(error));
      this.state.set('failed');
    }
  }

  /** Connects again with a token typed in the browser. */
  async useToken(token: string): Promise<void> {
    this.connection.useToken(token);
    await this.start();
  }
}

/** What went wrong, without the wrapping Electron adds to an error from the desktop app's main process. */
export function failure(error: unknown): string {
  const message = error instanceof Error ? error.message : String(error);
  return message.replace(/^Error invoking remote method '[^']*': (?:Error: )?/, '');
}
