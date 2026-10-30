import { Injectable, signal } from '@angular/core';
import { type CathodeClient, type Connection, createCathodeClient } from '@cathode/api-client';
import type { CathodeDesktop } from './desktop';

const DefaultServer = 'http://127.0.0.1:5180';
const TokenKey = 'cathode.token:';

/**
 * Where the Server is. In the desktop app the preload says (its sidecars, or a remote Server). In a browser: the
 * `server` query parameter; else the page's own origin when the Server served the editor (a headless rig, which marks
 * the page with `<meta name="cathode-server" content="same-origin">`); else a development Server on this machine. The
 * token comes from the `token` query parameter or the one remembered for that Server in this browser, and the address
 * keeps it: a Home Screen shortcut or bookmark made from the page opens straight in (an iPad's Home Screen app has
 * storage of its own, so it cannot find the one Safari remembered).
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
    const connection = this.desktop
      ? await this.desktop.connection()
      : fromLocation(window.location, servedByServer(document), storedToken);
    if (!this.desktop && connection.token) {
      rememberToken(connection.baseUrl, connection.token);
      showTokenInAddress(window, connection.token);
    }

    this.api = createCathodeClient(connection);
    this.connection.set(connection);
    return connection;
  }

  /** Whether the Server refuses this connection's token (401): a browser then asks for one. */
  async refused(): Promise<boolean> {
    const connection = this.connection();
    if (!connection) {
      return false;
    }

    try {
      const response = await fetch(`${connection.baseUrl}/api/v1/catalog/rig`, {
        headers: connection.token ? { Authorization: `Bearer ${connection.token}` } : {}
      });
      return response.status === 401;
    } catch {
      return false; // unreachable: the caller reports that
    }
  }

  /** Keeps a token typed in the browser for this Server; the next {@link connect} uses it. */
  useToken(token: string): void {
    const baseUrl = this.connection()?.baseUrl;
    if (baseUrl) {
      rememberToken(baseUrl, token.trim());
      showTokenInAddress(window, token.trim()); // the address's token wins over the stored one
    }
  }
}

export function fromLocation(
  location: Pick<Location, 'search' | 'origin'>,
  sameOrigin = false,
  stored: (baseUrl: string) => string | null = () => null
): Connection {
  const query = new URLSearchParams(location.search);
  const baseUrl = (query.get('server') ?? (sameOrigin ? location.origin : DefaultServer)).replace(/\/+$/, '');
  return { baseUrl, token: query.get('token') || stored(baseUrl) };
}

/** The Server marks the editor it serves, so the page talks to the origin it came from. */
function servedByServer(document: Document): boolean {
  return document.querySelector('meta[name="cathode-server"]')?.getAttribute('content') === 'same-origin';
}

/**
 * `href` with `token` as its `token` query parameter, or null when it already has it. The token stays in the address
 * so a Home Screen shortcut or bookmark made from the page carries it; the hash (the editor's route) is kept.
 */
export function addressWithToken(href: string, token: string): string | null {
  const url = new URL(href);
  if (url.searchParams.get('token') === token) {
    return null;
  }

  url.searchParams.set('token', token);
  return url.toString();
}

function showTokenInAddress(window: Window, token: string): void {
  const address = addressWithToken(window.location.href, token);
  if (address) {
    window.history.replaceState(window.history.state, '', address);
  }
}

function storedToken(baseUrl: string): string | null {
  try {
    return localStorage.getItem(TokenKey + baseUrl);
  } catch {
    return null;
  }
}

function rememberToken(baseUrl: string, token: string): void {
  try {
    localStorage.setItem(TokenKey + baseUrl, token);
  } catch {
    // private window or blocked storage: the token works for this visit only
  }
}
