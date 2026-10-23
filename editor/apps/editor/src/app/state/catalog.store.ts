import { Injectable, computed, inject, signal } from '@angular/core';
import { type BlockTypeInfo, BuiltInTypes, unwrap } from '@cathode/api-client';
import { ConnectionService } from '../core/connection';
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

  /** What the block palette offers: everything but the grid's input and output cells. */
  readonly placeable = computed(() =>
    this.types().filter(
      t => t.typeId !== BuiltInTypes.input && t.typeId !== BuiltInTypes.output && t.category !== 'System'
    )
  );

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
