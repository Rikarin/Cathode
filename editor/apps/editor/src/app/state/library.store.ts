import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { type AssetInfo, type PresetSummary, unwrap } from '@cathode/api-client';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';

/** The preset library and the asset store (captures and impulse responses). */
@Injectable({ providedIn: 'root' })
export class LibraryStore {
  private readonly connection = inject(ConnectionService);
  private readonly notify = inject(Notify);

  readonly presets = signal<readonly PresetSummary[]>([]);
  readonly assets = signal<readonly AssetInfo[]>([]);
  readonly models = computed(() => this.assets().filter(a => a.kind === 'nam' || a.kind === 'aidax'));
  readonly impulseResponses = computed(() => this.assets().filter(a => a.kind === 'ir'));

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

  async deletePreset(id: string): Promise<void> {
    await this.notify.run(async () => {
      unwrap(await this.connection.client.DELETE('/api/v1/presets/{id}', { params: { path: { id } } }));
      await this.refreshPresets();
    });
  }

  /** Uploads a capture (.nam, .aidax) or impulse response (.wav); the same file twice is stored once. */
  async upload(file: File): Promise<AssetInfo | undefined> {
    return this.notify.run(async () => {
      const asset = unwrap(
        await this.connection.client.POST('/api/v1/assets', {
          params: { query: { name: file.name } },
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

  /** An asset's bytes (an impulse response for the cab panel's plot). */
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
