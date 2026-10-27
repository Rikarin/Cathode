import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { type BlockTypeInfo, unwrap } from '@cathode/api-client';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';

/** Block types the Server offers, from the same descriptors the engine runs. */
@Injectable({ providedIn: 'root' })
export class CatalogStore {
  private readonly connection = inject(ConnectionService);
  private readonly notify = inject(Notify);

  readonly types = signal<readonly BlockTypeInfo[]>([]);

  /** The rig parameters (controllers, sources, scene level, tempo). */
  readonly rig = signal<BlockTypeInfo | null>(null);
  readonly rigParams = computed(() => new Map((this.rig()?.params ?? []).map(p => [p.id, p])));
  readonly byId = computed(() => new Map(this.types().map(t => [t.typeId, t])));

  /** What the block palette offers: every block type, Input and Output included, so a deleted one can come back. */
  readonly placeable = computed(() => this.types().filter(t => t.category !== 'System'));

  constructor() {
    // A reconnection can be to a restarted, possibly updated, Server: read its catalogue again.
    const subscription = inject(EventStream).connected$.subscribe(() => void this.load());
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }

  async load(): Promise<void> {
    await this.notify.run(async () => {
      const [types, rig] = await Promise.all([
        this.connection.client.GET('/api/v1/catalog/blocks'),
        this.connection.client.GET('/api/v1/catalog/rig')
      ]);
      this.types.set(unwrap(types));
      this.rig.set(unwrap(rig));
    });
  }

  info(typeId: string): BlockTypeInfo | undefined {
    return this.byId().get(typeId);
  }
}
