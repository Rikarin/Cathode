import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { type AssetInfo, type AssetUsage, type Bank, type PresetSummary, unwrap } from '@cathode/api-client';
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

/** The preset library and the asset store (captures and impulse responses). */
@Injectable({ providedIn: 'root' })
export class LibraryStore {
  private readonly connection = inject(ConnectionService);
  private readonly notify = inject(Notify);

  readonly presets = signal<readonly PresetSummary[]>([]);
  readonly assets = signal<readonly AssetInfo[]>([]);
  readonly banks = signal<readonly Bank[]>([]);
  readonly models = computed(() => this.assets().filter(a => a.kind === 'nam' || a.kind === 'aidax'));
  readonly impulseResponses = computed(() => this.assets().filter(a => a.kind === 'ir'));
  /** What a Cab slot playing an IR Pack takes: a multi-position pack (.cabgrid) or a single IR as a one-point pack. */
  readonly cabGrids = computed(() => this.assets().filter(a => a.kind === 'cabgrid' || a.kind === 'ir'));
  /** Asset reference to the stored presets that use it, by name. */
  readonly usage = signal<ReadonlyMap<string, readonly string[]>>(new Map());

  constructor() {
    const events = inject(EventStream);
    const subscription = events.events$.subscribe(event => {
      if (event.type === 'presetSaved') {
        void this.refreshPresets();
      }
    });
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }

  async refresh(): Promise<void> {
    await Promise.all([this.refreshPresets(), this.refreshAssets()]);
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

  /** The library's presets in "next preset" order (by name, as the MIDI router steps through them). */
  readonly ordered = computed(() => [...this.presets()].sort((a, b) => a.name.localeCompare(b.name)));

  async deletePreset(id: string): Promise<void> {
    await this.notify.run(async () => {
      unwrap(await this.connection.client.DELETE('/api/v1/presets/{id}', { params: { path: { id } } }));
      await this.refreshPresets();
    });
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

  /** Downloads a URL with the Server's credentials (a plain link cannot carry the bearer token). */
  async download(path: string, fallbackName: string): Promise<void> {
    await this.notify.run(async () => {
      const connection = this.connection.connection()!;
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
    const connection = this.connection.connection()!;
    const response = await fetch(`${connection.baseUrl}/api/v1/assets/${encodeURIComponent(reference)}/content`, {
      headers: connection.token ? { Authorization: `Bearer ${connection.token}` } : {}
    });
    if (!response.ok) {
      throw new Error(`Could not read asset: ${response.status}`);
    }

    return response.arrayBuffer();
  }
}
