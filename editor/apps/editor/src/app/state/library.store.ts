import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import {
  ApiError,
  type AssetInfo,
  type AssetUsage,
  type Bank,
  type Connection,
  type LibraryKind,
  type PresetSummary,
  type RestoreResult,
  type Setlist,
  unwrap
} from '@cathode/api-client';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';

/** How an IR file is processed on import, as the Server's upload takes it (all optional: as-is by default). */
export interface IrImportOptions {
  processing?: 'asIs' | 'autoTrim' | 'minimumPhase';
  /** Invert the IR if its main peak is negative. */
  polarity?: boolean;
  /** For a stereo file. */
  channels?: 'both' | 'left' | 'right' | 'sum';
  maxLengthMs?: number;
}

/**
 * Where a preset is used, as people read it: “bank “Gig”, slot 3”, “setlist “Friday”, entry 2” (both counted from 1).
 * The Server refuses to delete a preset while a bank or setlist holds it: taking it out of them would leave a gap where
 * a program change or setlist entry used to load it.
 */
export function presetUses(id: string, banks: readonly Bank[], setlists: readonly Setlist[]): string[] {
  const where = (kind: string, name: string, place: string, at: number[]) =>
    `${kind} “${name}”, ${place}${at.length > 1 ? 's' : ''} ${at.map(i => i + 1).join(', ')}`;
  const indexes = <T>(items: readonly T[], match: (item: T) => boolean) =>
    items.flatMap((item, i) => (match(item) ? [i] : []));
  return [
    ...banks
      .map(b => ({ name: b.name, at: indexes(b.slots, slot => slot === id) }))
      .filter(b => b.at.length > 0)
      .map(b => where('bank', b.name, 'slot', b.at)),
    ...setlists
      .map(s => ({ name: s.name, at: indexes(s.entries, e => e.presetId === id) }))
      .filter(s => s.at.length > 0)
      .map(s => where('setlist', s.name, 'entry', s.at))
  ];
}

/** `list` with `setlist` stored in it (added, or in place of the one with its id), by name as the Server lists them. */
export function withSetlist(list: readonly Setlist[], setlist: Setlist): Setlist[] {
  return [...list.filter(s => s.id !== setlist.id), setlist].sort((a, b) =>
    a.name.localeCompare(b.name, undefined, { sensitivity: 'base' })
  );
}

/**
 * A setlist or bank as far as its revision goes: how many times the Server has stored it. The Server sets it on every
 * save and answers with it; an entry that has never been stored has none.
 */
export interface Revised {
  id: string;
  revision?: number | null;
}

/**
 * The `If-Match` header that guards a save of a setlist or bank read at `revision`: the Server refuses it (412) when
 * another editor stored or deleted the entry in between, instead of letting this save replace that change. No header
 * for an entry that has no revision yet: it is new, and there is nothing to overwrite.
 */
export function ifMatch(revision: number | null | undefined): Record<string, string> {
  return revision == null ? {} : { 'If-Match': `"${revision}"` };
}

/** Whether a failed save was refused because the entry was changed or deleted elsewhere since it was read (412). */
export function isStale(error: unknown): boolean {
  return error instanceof ApiError && error.status === 412;
}

/**
 * Whether an asset came with Cathode (the factory impulse responses): the Server keeps those read-only, so they are
 * not renamed or deleted. Their colour tag and favourite flag are the user's.
 */
export function isFactoryAsset(asset: { ref: string; factory?: boolean | null }): boolean {
  return asset.factory === true;
}

/** The file a library backup is saved as and restored from. */
export const BackupFileExtension = '.cthbackup';

/** What a restore put into the library, as a sentence: “Restored 12 presets, 3 IRs and captures and the global settings.” */
export function restoreSummary(result: RestoreResult): string {
  const count = (n: number, one: string, many: string) => (n > 0 ? [`${n} ${n === 1 ? one : many}`] : []);
  const parts = [
    ...count(result.presets, 'preset', 'presets'),
    ...count(result.blockPresets, 'block preset', 'block presets'),
    ...count(result.templates, 'template', 'templates'),
    ...count(result.snapshots, 'snapshot', 'snapshots'),
    ...count(result.banks, 'bank', 'banks'),
    ...count(result.setlists, 'setlist', 'setlists'),
    ...count(result.assets, 'new IR or capture', 'new IRs and captures'),
    ...(result.settings ? ['the global settings'] : [])
  ];
  if (parts.length === 0) {
    return 'The backup held nothing to restore.';
  }

  const last = parts.pop();
  return `Restored ${parts.length > 0 ? `${parts.join(', ')} and ${last}` : last}.`;
}

/** The preset library and the asset store (captures and impulse responses). */
@Injectable({ providedIn: 'root' })
export class LibraryStore {
  private readonly connection = inject(ConnectionService);
  private readonly notify = inject(Notify);

  readonly presets = signal<readonly PresetSummary[]>([]);
  readonly assets = signal<readonly AssetInfo[]>([]);
  readonly banks = signal<readonly Bank[]>([]);
  /** The setlists, by name (as the Server lists them). */
  readonly setlists = signal<readonly Setlist[]>([]);
  readonly models = computed(() => this.assets().filter(a => a.kind === 'nam' || a.kind === 'aidax'));
  readonly impulseResponses = computed(() => this.assets().filter(a => a.kind === 'ir'));
  /** What a Cab slot playing an IR Pack takes: a multi-position pack (.cabgrid) or a single IR as a one-point pack. */
  readonly cabGrids = computed(() => this.assets().filter(a => a.kind === 'cabgrid' || a.kind === 'ir'));
  /** Asset reference to the stored presets that use it, by name. */
  readonly usage = signal<ReadonlyMap<string, readonly string[]>>(new Map());

  /** The setlist save in progress; the next waits for it (see {@link saveSetlist}). */
  private savingSetlists: Promise<unknown> = Promise.resolve();
  /** How many setlist saves the Server has refused. */
  private setlistRefusals = 0;
  /**
   * The newest revision of each setlist this editor knows of, from the lists it has read and the answers to its own
   * saves. A list read while a save was on its way can be older than that save's answer, so a list only ever raises
   * a revision; the list read after a refusal replaces them all.
   */
  private readonly setlistRevisions = new Map<string, number>();
  /** The revision each setlist's last answered save left it at, for the save queued behind it. */
  private readonly savedRevisions = new Map<string, number>();
  /** How many saves of each setlist are queued or on their way. */
  private readonly setlistSaves = new Map<string, number>();

  constructor() {
    const events = inject(EventStream);
    const subscriptions = [
      // Another editor, a footswitch or this one changed what is stored: show it without a reload.
      events.library$.subscribe(change => void this.changed(change.kind)),
      // Presets, assets and banks may have changed while the stream was down (or the Server restarted): resync.
      events.connected$.subscribe(() => void this.resync())
    ];
    inject(DestroyRef).onDestroy(() => subscriptions.forEach(s => s.unsubscribe()));
  }

  /** Reads everything the library holds again; asset usage only if something has read it. */
  async resync(): Promise<void> {
    await Promise.all([
      this.refreshPresets(),
      this.refreshAssets(),
      this.refreshBanks(),
      this.refreshSetlists(),
      this.usage().size > 0 ? this.refreshUsage() : undefined
    ]);
  }

  async refresh(): Promise<void> {
    await Promise.all([this.refreshPresets(), this.refreshAssets()]);
  }

  /**
   * Reads again the list the Server says has changed. Which presets use an asset depends on both the presets and the
   * assets, and is read again only if something has read it.
   */
  private async changed(kind: LibraryKind): Promise<void> {
    const usage = () => (this.usage().size > 0 ? this.refreshUsage() : undefined);
    switch (kind) {
      case 'presets':
        await Promise.all([this.refreshPresets(), usage()]);
        break;
      case 'assets':
        await Promise.all([this.refreshAssets(), usage()]);
        break;
      case 'banks':
        await this.refreshBanks();
        break;
      case 'setlists':
        await this.refreshSetlists();
        break;
      default:
        break; // block presets, snapshots and templates have their own readers
    }
  }

  async refreshPresets(): Promise<void> {
    await this.notify.run(async () => this.presets.set(unwrap(await this.connection.client.GET('/api/v1/presets'))));
  }

  async refreshAssets(): Promise<void> {
    await this.notify.run(async () => this.assets.set(unwrap(await this.connection.client.GET('/api/v1/assets'))));
  }

  async refreshUsage(): Promise<void> {
    await this.notify.run(async () => {
      const usage: AssetUsage[] = unwrap(await this.connection.client.GET('/api/v1/assets/usage'));
      this.usage.set(new Map(usage.map(u => [u.ref, u.presets])));
    });
  }

  /** Deletes assets from the store; presets that use one keep the reference and play nothing in that slot. */
  async deleteAssets(references: readonly string[]): Promise<void> {
    await this.notify.run(async () => {
      for (const reference of references) {
        unwrap(await this.connection.client.DELETE('/api/v1/assets/{reference}', { params: { path: { reference } } }));
      }
    });
    await this.refreshAssets();
  }

  async refreshBanks(): Promise<void> {
    await this.notify.run(async () => this.banks.set(unwrap(await this.connection.client.GET('/api/v1/banks'))));
  }

  /**
   * Reads the setlists again. `replaceRevisions` takes their revisions as the Server has them now, whatever this
   * editor believed (after a refused save); otherwise a revision is only ever raised (see {@link setlistRevisions}).
   */
  async refreshSetlists(replaceRevisions = false): Promise<void> {
    await this.notify.run(async () => {
      const setlists: (Setlist & Revised)[] = unwrap(await this.connection.client.GET('/api/v1/setlists'));
      if (replaceRevisions) {
        this.setlistRevisions.clear();
      }

      const stored = new Set(setlists.map(s => s.id));
      for (const id of [...this.setlistRevisions.keys()].filter(known => !stored.has(known))) {
        this.setlistRevisions.delete(id); // deleted (here or elsewhere): one made again under its id starts over
      }

      for (const setlist of setlists) {
        this.sawRevision(setlist.id, setlist.revision);
      }

      this.setlists.set(setlists);
    });
  }

  private sawRevision(id: string, revision: number | null | undefined): void {
    if (revision != null && revision > (this.setlistRevisions.get(id) ?? 0)) {
      this.setlistRevisions.set(id, revision);
    }
  }

  /**
   * Stores a setlist, new or changed; false (the refusal reported, and the stored setlists read again) if the Server
   * refused it, for example for a new entry whose preset does not exist.
   *
   * {@link setlists} shows the change before the Server has answered, so an edit made straight after this one is
   * built from it, not from the setlist as it was. The requests go out one at a time, in the order the edits were
   * made: each carries the whole setlist, and the last one must be the one that stays stored.
   *
   * Each save is guarded with `If-Match` on the revision the edit was made on: the one this editor knew when the edit
   * was made, or, for an edit made while an earlier save of the same setlist was still on its way, the one that save
   * was answered with. When another editor stored or deleted the setlist in between, the Server refuses the save
   * (412) and it is handled like any refusal: reported, and the stored setlists shown, the other editor's change
   * among them. Without the guard this save would have put the old setlist back over that change.
   */
  saveSetlist(setlist: Setlist): Promise<boolean> {
    const id = setlist.id;
    const queued = this.setlistSaves.get(id) ?? 0;
    const madeOn = queued > 0 ? undefined : this.setlistRevisions.get(id);
    this.setlistSaves.set(id, queued + 1);
    this.setlists.update(list => withSetlist(list, setlist));
    const refusals = this.setlistRefusals;
    const result = this.savingSetlists
      .then(() => this.putSetlist(setlist, refusals, queued > 0 ? this.savedRevisions.get(id) : madeOn))
      .finally(() => this.setlistSaves.set(id, (this.setlistSaves.get(id) ?? 1) - 1));
    this.savingSetlists = result.catch(() => undefined);
    return result;
  }

  private async putSetlist(setlist: Setlist, refusals: number, madeOn: number | undefined): Promise<boolean> {
    // An edit made while a save that was then refused was still on its way was built on the refused change, and what
    // is shown has since been read back from the Server: it is dropped with it.
    if (refusals !== this.setlistRefusals) {
      return false;
    }

    const done = await this.notify.run(async () => {
      const stored: Revised = unwrap(
        await this.connection.client.PUT('/api/v1/setlists/{id}', {
          params: { path: { id: setlist.id } },
          body: setlist,
          headers: ifMatch(madeOn)
        })
      );
      if (stored.revision != null) {
        this.savedRevisions.set(setlist.id, stored.revision);
        this.sawRevision(setlist.id, stored.revision);
      }

      return true;
    });
    if (!done) {
      this.setlistRefusals++;
      await this.refreshSetlists(true); // show what is stored, not the refused change
    }

    return done ?? false;
  }

  /** Deletes a setlist (its presets stay in the library); the Server stops playing it if it was the active one. */
  async deleteSetlist(id: string): Promise<boolean> {
    await this.savingSetlists; // a save still on its way would store the setlist again
    const done = await this.notify.run(async () => {
      unwrap(await this.connection.client.DELETE('/api/v1/setlists/{id}', { params: { path: { id } } }));
      return true;
    });
    await this.refreshSetlists();
    return done ?? false;
  }

  /** Renames a preset or sets its colour tag or favourite flag (stored, and live when it is loaded). */
  async setPresetMetadata(id: string, change: { name?: string; color?: string; favorite?: boolean }): Promise<boolean> {
    const done = await this.notify.run(async () => {
      unwrap(
        await this.connection.client.PATCH('/api/v1/presets/{id}/metadata', {
          params: { path: { id } },
          body: { name: change.name ?? null, color: change.color ?? null, favorite: change.favorite ?? null }
        })
      );
      return true;
    });
    await this.refreshPresets();
    return done ?? false;
  }

  async setAssetMetadata(
    reference: string,
    change: { name?: string; color?: string; favorite?: boolean }
  ): Promise<boolean> {
    const done = await this.notify.run(async () => {
      unwrap(
        await this.connection.client.PATCH('/api/v1/assets/{reference}/metadata', {
          params: { path: { reference } },
          body: { name: change.name ?? null, color: change.color ?? null, favorite: change.favorite ?? null }
        })
      );
      return true;
    });
    await this.refreshAssets();
    return done ?? false;
  }

  /** The library's presets by name. */
  readonly ordered = computed(() => [...this.presets()].sort((a, b) => a.name.localeCompare(b.name)));

  /** Whether the library holds a preset with this id: false for a new one that has never been saved. */
  async isStored(id: string): Promise<boolean> {
    try {
      await this.connection.client.GET('/api/v1/presets/{id}', { params: { path: { id } } });
      return true;
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        return false;
      }

      throw error;
    }
  }

  /** Where a stored preset is used (see {@link presetUses}), read fresh from the Server; undefined if that failed. */
  async usesOf(id: string): Promise<string[] | undefined> {
    return this.notify.run(async () => {
      const [banks, setlists] = await Promise.all([
        this.connection.client.GET('/api/v1/banks').then(unwrap),
        this.connection.client.GET('/api/v1/setlists').then(unwrap)
      ]);
      return presetUses(id, banks, setlists);
    });
  }

  /** Deletes a preset no bank or setlist uses (the Server refuses one that is used); false if it failed. */
  async deletePreset(id: string): Promise<boolean> {
    const done = await this.notify.run(async () => {
      unwrap(await this.connection.client.DELETE('/api/v1/presets/{id}', { params: { path: { id } } }));
      return true;
    });
    await this.refreshPresets();
    return done ?? false;
  }

  /**
   * Uploads a capture (.nam, .aidax) or impulse response (.wav); the same file twice is stored once. An IR can be
   * processed on import (`options`): trimmed or made minimum phase, its polarity corrected, a channel chosen, cut.
   */
  async upload(file: File, options: IrImportOptions = {}): Promise<AssetInfo | undefined> {
    return this.notify.run(async () => {
      const asset = unwrap(
        await this.connection.client.POST('/api/v1/assets', {
          params: { query: { name: file.name, ...options } },
          body: file as unknown as string,
          bodySerializer: body => body as unknown as BodyInit,
          headers: { 'Content-Type': 'application/octet-stream' }
        })
      );
      await this.refreshAssets();
      return asset;
    });
  }

  /** Imports a .cthpreset package into the library. */
  async import(file: File): Promise<void> {
    await this.notify.run(async () => {
      const result = unwrap(
        await this.connection.client.POST('/api/v1/presets/import', {
          body: file as unknown as never,
          bodySerializer: body => body as unknown as BodyInit,
          headers: { 'Content-Type': 'application/zip' }
        })
      );
      await this.refresh();
      this.notify.success(
        `Imported "${result.preset.name}"${result.renamed ? ' (renamed)' : ''} with ${result.assetsAdded} new asset(s).`
      );
    });
  }

  /** Saves the whole library (settings, presets, banks, setlists, IRs and captures) as one .cthbackup file. */
  async backup(): Promise<void> {
    await this.download('/api/v1/library/backup', `Cathode backup${BackupFileExtension}`);
  }

  /**
   * Puts a .cthbackup file into the library (an entry with the id of one already there replaces it; the global
   * settings are replaced) and reads the library again. Resolves to what was restored, or undefined if it failed.
   */
  async restore(file: File): Promise<RestoreResult | undefined> {
    return this.notify.run(async () => {
      const result = unwrap(
        await this.connection.client.POST('/api/v1/library/restore', {
          body: file as unknown as never,
          bodySerializer: body => body as unknown as BodyInit,
          headers: { 'Content-Type': 'application/zip' }
        })
      );
      await this.resync();
      return result;
    });
  }

  /** Downloads a URL with the Server's credentials (a plain link cannot carry the bearer token). */
  async download(path: string, fallbackName: string): Promise<void> {
    await this.notify.run(async () => {
      const connection = this.serverConnection();
      const response = await fetch(connection.baseUrl + path, {
        headers: connection.token ? { Authorization: `Bearer ${connection.token}` } : {}
      });
      if (!response.ok) {
        throw new Error(`Download failed: ${response.status}`);
      }

      const name = /filename="?([^";]+)"?/.exec(response.headers.get('Content-Disposition') ?? '')?.[1] ?? fallbackName;
      const link = document.createElement('a');
      link.href = URL.createObjectURL(await response.blob());
      link.download = name;
      link.click();
      URL.revokeObjectURL(link.href);
    });
  }

  /** An asset's bytes (an impulse response for the Cab panel's plot). */
  async content(reference: string): Promise<ArrayBuffer> {
    const connection = this.serverConnection();
    const response = await fetch(`${connection.baseUrl}/api/v1/assets/${encodeURIComponent(reference)}/content`, {
      headers: connection.token ? { Authorization: `Bearer ${connection.token}` } : {}
    });
    if (!response.ok) {
      throw new Error(`Could not read asset: ${response.status}`);
    }

    return response.arrayBuffer();
  }

  /** Where the Server is and its token; a download before the editor has connected is refused, not sent nowhere. */
  private serverConnection(): Connection {
    const connection = this.connection.connection();
    if (!connection) {
      throw new Error('Not connected to a Server yet.');
    }

    return connection;
  }
}
