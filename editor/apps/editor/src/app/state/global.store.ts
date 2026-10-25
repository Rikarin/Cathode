import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { type GlobalSettings, unwrap } from '@cathode/api-client';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';

const GlobalBlockEvents = new Set(['globalLinked', 'globalUnlinked', 'globalDeleted']);

/** The rig's global settings: tempo mode, metronome, global EQ, Manual A-D, MIDI, footswitches, global blocks. */
@Injectable({ providedIn: 'root' })
export class GlobalStore {
  private readonly connection = inject(ConnectionService);
  private readonly notify = inject(Notify);

  readonly settings = signal<GlobalSettings | null>(null);

  constructor() {
    const events = inject(EventStream);
    const subscriptions = [
      events.connected$.subscribe(() => void this.refresh()),
      events.midi$.subscribe(m => m.type === 'midiLearned' && void this.refresh()),
      // A footswitch can toggle the metronome or tap a global tempo: reflect it.
      events.events$.subscribe(e => e.type === 'tempoChanged' && void this.refresh()),
      // Making, linking and deleting global blocks changes the library of them.
      events.events$.subscribe(e => GlobalBlockEvents.has(e.type) && void this.refresh())
    ];
    inject(DestroyRef).onDestroy(() => subscriptions.forEach(s => s.unsubscribe()));
  }

  async refresh(): Promise<void> {
    await this.notify.run(async () => this.settings.set(unwrap(await this.connection.client.GET('/api/v1/global'))));
  }

  /** Applies a change to a copy and saves it; the Server validates and answers with what it stored. */
  async update(change: (settings: GlobalSettings) => void): Promise<boolean> {
    const current = this.settings();
    if (!current) {
      return false;
    }

    const next = structuredClone(current);
    change(next);
    const saved = await this.notify.run(async () =>
      unwrap(await this.connection.client.PUT('/api/v1/global', { body: next }))
    );
    if (saved) {
      this.settings.set(saved);
    }

    return saved !== undefined;
  }
}
