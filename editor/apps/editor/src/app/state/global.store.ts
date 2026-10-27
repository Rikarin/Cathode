import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { ApiError, type GlobalSettings, type RigEvent, unwrap } from '@cathode/api-client';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';

/**
 * The rig events after which the global settings may differ: any change to them (`globalChanged`, whatever made it: a
 * footswitch's metronome, layout or view, a Manual knob, another editor), a tempo change, and making, linking and
 * deleting global blocks.
 */
const RefreshEvents = new Set(['globalChanged', 'tempoChanged', 'globalLinked', 'globalUnlinked', 'globalDeleted']);

/** How often an update is made again on fresh settings after 412 (the rig kept changing them meanwhile). */
const Retries = 3;

interface GlobalResult {
  data?: GlobalSettings;
  error?: unknown;
  response: Response;
}

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
  /** The revision of {@link settings}, as the ETag the Server answered with; sent back as `If-Match`. */
  private etag: string | null = null;
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
        await this.notify.run(() => this.read());
      } finally {
        this.refreshing = null;
      }
    })();
    await this.refreshing;
  }

  /**
   * Applies a change to a copy and saves it; the Server validates and answers with what it stored. The whole document
   * is sent, so updates run one at a time, each on the answer to the one before: two quick changes both land, where
   * the second, made on the state before the first was answered, used to put the first back. Each is guarded with
   * `If-Match` on the revision it was made on: when another editor or the rig (a footswitch, MIDI learn, a Manual knob)
   * changed the settings meanwhile (412), they are read again and the change is made again on them, so it keeps the
   * other change instead of putting the old settings back.
   */
  update(change: (settings: GlobalSettings) => void): Promise<boolean> {
    const result = this.updating.then(() => this.put(change));
    this.updating = result.catch(() => undefined);
    return result;
  }

  private async put(change: (settings: GlobalSettings) => void): Promise<boolean> {
    for (let attempt = 1; ; attempt++) {
      const current = this.settings();
      if (!current) {
        return false;
      }

      try {
        // The change runs inside the try too: one that throws is reported, not left to reject a voided update().
        const next = structuredClone(current);
        change(next);
        const headers: Record<string, string> = this.etag ? { 'If-Match': this.etag } : {};
        this.adopt(await this.connection.client.PUT('/api/v1/global', { body: next, headers }));
        return true;
      } catch (error) {
        if (!(error instanceof ApiError && error.status === 412)) {
          this.notify.error(error);
          return false;
        }

        if (attempt >= Retries) {
          this.notify.error(new Error('The global settings kept changing; your change was not applied. Try again.'));
          return false;
        }
      }

      // Not the coalesced refresh: one already on the wire may have left before the change that made this one stale.
      if (!(await this.notify.run(async () => (await this.read(), true)))) {
        return false;
      }
    }
  }

  private async read(): Promise<void> {
    this.adopt(await this.connection.client.GET('/api/v1/global'));
  }

  /** Takes the settings a GET or PUT answered with, and their revision. */
  private adopt(result: GlobalResult): void {
    const settings = unwrap(result);
    this.etag = result.response.headers.get('ETag');
    this.settings.set(settings);
  }
}
