import { Injectable, computed, inject, signal } from '@angular/core';
import { type BlockPreset, type ChannelName, unwrap } from '@cathode/api-client';
import { ConnectionService } from '../core/connection';
import { Notify } from '../core/notify';
import { LiveStore } from './live.store';

/**
 * The Block Library: factory type presets (the guide's chorus, flanger, reverb… types as starting points) and the
 * user's own block presets. Applying one sets a channel of the selected block to it.
 */
@Injectable({ providedIn: 'root' })
export class BlockPresetsStore {
  private readonly connection = inject(ConnectionService);
  private readonly notify = inject(Notify);
  private readonly live = inject(LiveStore);

  readonly presets = signal<readonly BlockPreset[]>([]);
  readonly user = computed(() => this.presets().filter(p => !p.factory));

  /** Presets for one block type, factory first. */
  forType(type: string): readonly BlockPreset[] {
    return this.presets().filter(p => p.type === type);
  }

  async refresh(): Promise<void> {
    await this.notify.run(async () =>
      this.presets.set(unwrap(await this.connection.client.GET('/api/v1/block-presets')))
    );
  }

  apply(block: string, preset: string, channel?: ChannelName): Promise<boolean> {
    return this.live.mutate(c =>
      c.POST('/api/v1/live/blocks/{id}/preset', {
        params: { path: { id: block } },
        body: { preset, channel: channel ?? null }
      })
    );
  }

  /** Saves a block's channel as a new user preset. */
  async save(type: string, name: string, values: Record<string, number>): Promise<void> {
    await this.notify.run(async () => {
      const id = crypto.randomUUID().replaceAll('-', '');
      unwrap(
        await this.connection.client.PUT('/api/v1/block-presets/{id}', {
          params: { path: { id } },
          body: { id, type, name, factory: false, values }
        })
      );
      await this.refresh();
    });
  }

  async delete(id: string): Promise<void> {
    await this.notify.run(async () => {
      unwrap(await this.connection.client.DELETE('/api/v1/block-presets/{id}', { params: { path: { id } } }));
      await this.refresh();
    });
  }
}
