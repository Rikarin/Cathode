import { Injectable, signal } from '@angular/core';
import { type CathodeClient, type Connection, createCathodeClient } from '@cathode/api-client';
import type { CathodeDesktop } from './desktop';

const DefaultServer = 'http://127.0.0.1:5180';

/**
 * Where the Server is. In the desktop app the preload says (its sidecars, or a remote Server); in a browser, the
 * `server` and `token` query parameters do, defaulting to a development Server on this machine.
 */
@Injectable({ providedIn: 'root' })
export class ConnectionService {
    readonly desktop: CathodeDesktop | null = typeof window !== 'undefined' ? (window.cathode ?? null) : null;
    readonly connection = signal<Connection | null>(null);
    private api: CathodeClient | null = null;

    /** The REST client; valid once {@link connect} has resolved. */
    get client(): CathodeClient {
        if (!this.api) {
            throw new Error('Not connected to a Cathode server yet.');
        }

        return this.api;
    }

    async connect(): Promise<Connection> {
        const connection = this.desktop ? await this.desktop.connection() : fromLocation(window.location);
        this.api = createCathodeClient(connection);
        this.connection.set(connection);
        return connection;
    }
}

export function fromLocation(location: Pick<Location, 'search'>): Connection {
    const query = new URLSearchParams(location.search);
    return {
        baseUrl: (query.get('server') ?? DefaultServer).replace(/\/+$/, ''),
        token: query.get('token')
    };
}
