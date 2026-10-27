import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import {
  type BlockPreset,
  type BlockPresetFile,
  type ChannelName,
  type PresetBlock,
  type PresetModifier,
  unwrap
} from '@cathode/api-client';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';
import { LiveStore } from './live.store';

/** The extension of an exported Block Library file (BlockPresetFile on the Server). */
export const BlockFileExtension = '.cthblock';

/** A folder of the Library and its entries; the unfiled ones under ''. */
export interface LibraryFolder {
  name: string;
  entries: BlockPreset[];
}

/** The user's entries by folder, folders by name with the unfiled ones first, entries by name. */
export function libraryFolders(presets: readonly BlockPreset[]): LibraryFolder[] {
  const byFolder = new Map<string, BlockPreset[]>();
  for (const preset of presets) {
    const folder = preset.folder ?? '';
    byFolder.set(folder, [...(byFolder.get(folder) ?? []), preset]);
  }

  return [...byFolder.entries()]
    .sort(([a], [b]) => (a === '' ? -1 : b === '' ? 1 : a.localeCompare(b)))
    .map(([name, entries]) => ({ name, entries: entries.sort((a, b) => a.name.localeCompare(b.name)) }));
}

/** A whole block as a Library entry: every channel, its modifiers (without the block id) and assets. */
export function wholeBlock(
  block: PresetBlock,
  channel: ChannelName,
  modifiers: readonly PresetModifier[]
): Pick<BlockPreset, 'values' | 'channels' | 'modifiers' | 'assets'> {
  return {
    values: { ...(block.channels[channel] ?? {}) },
    channels: structuredClone(block.channels),
    modifiers: modifiers.filter(m => m.block === block.id).map(m => ({ ...m, block: '' })),
    assets: { ...block.assets }
  };
}

/**
 * The Block Library: factory type presets (the guide's chorus, flanger, reverb… types as starting points) and the
 * user's own entries, one channel or a whole block (channels, modifiers, assets), in folders. Recalling one sets the
 * current channel of the selected block, or all of them.
 */
@Injectable({ providedIn: 'root' })
export class BlockPresetsStore {
  private readonly connection = inject(ConnectionService);
  private readonly notify = inject(Notify);
  private readonly live = inject(LiveStore);

  readonly presets = signal<readonly BlockPreset[]>([]);
  readonly user = computed(() => this.presets().filter(p => !p.factory));
  readonly folders = computed(() => libraryFolders(this.user()));
  private loaded = false;

  constructor() {
    // Entries may have changed while the event stream was down (or the Server restarted): read them again, once the
    // Library has been read at all.
    const subscription = inject(EventStream).connected$.subscribe(() => this.loaded && void this.refresh());
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }

  /** Presets for one block type, factory first. */
  forType(type: string): readonly BlockPreset[] {
    return this.presets().filter(p => p.type === type);
  }

  async refresh(): Promise<void> {
    this.loaded = true;
    await this.notify.run(async () =>
      this.presets.set(unwrap(await this.connection.client.GET('/api/v1/block-presets')))
    );
  }

  /** Recalls an entry into one channel (default: the active scene's) or, with `all`, into every channel. */
  apply(block: string, preset: string, options: { channel?: ChannelName; all?: boolean } = {}): Promise<boolean> {
    return this.live.mutate(c =>
      c.POST('/api/v1/live/blocks/{id}/preset', {
        params: { path: { id: block } },
        body: { preset, channel: options.channel ?? null, allChannels: options.all ?? false }
      })
    );
  }

  /** Saves a new user entry: one channel's values, or (with `channels`) a whole block. */
  async save(
    type: string,
    name: string,
    entry: Pick<BlockPreset, 'values'> & Partial<Pick<BlockPreset, 'channels' | 'modifiers' | 'assets' | 'folder'>>
  ): Promise<void> {
    await this.notify.run(async () => {
      const id = crypto.randomUUID().replaceAll('-', '');
      unwrap(
        await this.connection.client.PUT('/api/v1/block-presets/{id}', {
          params: { path: { id } },
          body: {
            id,
            type,
            name,
            factory: false,
            values: entry.values,
            folder: entry.folder || null,
            channels: entry.channels ?? null,
            modifiers: entry.modifiers ?? null,
            assets: entry.assets ?? null
          }
        })
      );
      await this.refresh();
    });
  }

  /** Every block of the working copy, as whole-block entries in one folder. */
  async saveAll(folder: string | null): Promise<number> {
    const saved = await this.notify.run(async () =>
      unwrap(await this.connection.client.POST('/api/v1/live/block-presets', { body: { folder } }))
    );
    await this.refresh();
    return saved?.length ?? 0;
  }

  async delete(id: string): Promise<void> {
    await this.notify.run(async () => {
      unwrap(await this.connection.client.DELETE('/api/v1/block-presets/{id}', { params: { path: { id } } }));
      await this.refresh();
    });
  }

  /** Moves an entry to another folder (saved again under its id). */
  async moveTo(preset: BlockPreset, folder: string): Promise<void> {
    await this.notify.run(async () => {
      unwrap(
        await this.connection.client.PUT('/api/v1/block-presets/{id}', {
          params: { path: { id: preset.id } },
          body: { ...preset, folder: folder || null }
        })
      );
      await this.refresh();
    });
  }

  /** Downloads entries as a .cthblock file. */
  async export(ids: readonly string[], fileName: string): Promise<void> {
    await this.notify.run(async () => {
      const file = unwrap(
        await this.connection.client.POST('/api/v1/block-presets/export', { body: { ids: [...ids] } })
      );
      const link = document.createElement('a');
      link.href = URL.createObjectURL(new Blob([JSON.stringify(file, null, 2)], { type: 'application/json' }));
      link.download = fileName.endsWith(BlockFileExtension) ? fileName : fileName + BlockFileExtension;
      link.click();
      URL.revokeObjectURL(link.href);
    });
  }

  /** Imports a .cthblock file; resolves to the entries added. */
  async import(file: File): Promise<BlockPreset[]> {
    const added = await this.notify.run(async () => {
      const content = JSON.parse(await file.text()) as BlockPresetFile;
      return unwrap(await this.connection.client.POST('/api/v1/block-presets/import', { body: content }));
    });
    await this.refresh();
    if (added) {
      this.notify.success(`Imported ${added.length} Library entr${added.length === 1 ? 'y' : 'ies'}.`);
    }

    return added ?? [];
  }
}
