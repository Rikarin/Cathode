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
    readonly byId = computed(() => new Map(this.types().map(t => [t.typeId, t])));

    /** What the block palette offers: everything but the grid's input and output cells. */
    readonly placeable = computed(() =>
        this.types().filter(t => t.typeId !== BuiltInTypes.input && t.typeId !== BuiltInTypes.output)
    );

    async load(): Promise<void> {
        await this.notify.run(async () =>
            this.types.set(unwrap(await this.connection.client.GET('/api/v1/catalog/blocks')))
        );
    }

    info(typeId: string): BlockTypeInfo | undefined {
        return this.byId().get(typeId);
    }
}
