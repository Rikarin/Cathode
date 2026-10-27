import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { type GlobalSettings, type RigEvent, unwrap } from '@cathode/api-client';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';

/**
 * The rig events after which the global settings may differ: any change to them (`globalChanged`, whatever made it: a
 * footswitch's metronome, layout or view, a Manual knob, another editor), a tempo change, and making, linking and
 * deleting global blocks.
 */
const RefreshEvents = new Set(['globalChanged', 'tempoChanged', 'globalLinked', 'globalUnlinked', 'globalDeleted']);

/** Whether `event` means the global settings should be fetched again. */
export function refreshesGlobals(event: RigEvent): boolean {
  return RefreshEvents.has(event.type);
}

/** The rig's global settings: tempo mode, metronome, global EQ, Manual A-D, MIDI, footswitches, global blocks. */
@Injectable({ providedIn: 'root' })
export class GlobalStore {
  private readonly connection = inject(ConnectionService);
  private readonly notify = inject(Notify);

  readonly settings = signal<GlobalSettings | null>(null);
  /** The update in progress; the next waits for it (see {@link update}). */
  private updating: Promise<unknown> = Promise.resolve();
  private refreshing: Promise<void> | null = null;

  constructor() {
    const events = inject(EventStream);
    const subscriptions = [
      events.connected$.subscribe(() => void this.refresh()),
      events.midi$.subscribe(m => m.type === 'midiLearned' && void this.refresh()),
      events.events$.subscribe(e => refreshesGlobals(e) && void this.refresh())
    ];
    inject(DestroyRef).onDestroy(() => subscriptions.forEach(s => s.unsubscribe()));
  }

  async refresh(): Promise<void> {
    // Coalesce: a burst of events (a Manual knob swept from a pedal) needs one refetch, not one each.
    this.refreshing ??= (async () => {
      try {
        await this.notify.run(async () =>
          this.settings.set(unwrap(await this.connection.client.GET('/api/v1/global')))
        );
      } finally {
        this.refreshing = null;
      }
    })();
    await this.refreshing;
  }

  /**
   * Applies a change to a copy and saves it; the Server validates and answers with what it stored. The whole document
   * is sent, so updates run one at a time, each on the answer to the one before: two quick changes both land, where
   * the second, made on the state before the first was answered, used to put the first back.
   */
  update(change: (settings: GlobalSettings) => void): Promise<boolean> {
    const result = this.updating.then(() => this.put(change));
    this.updating = result.catch(() => undefined);
    return result;
  }

  private async put(change: (settings: GlobalSettings) => void): Promise<boolean> {
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
