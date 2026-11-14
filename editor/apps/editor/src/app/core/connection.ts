import { Injectable, signal } from '@angular/core';
import { type CathodeClient, type Connection, createCathodeClient } from '@cathode/api-client';
import { Subject } from 'rxjs';
import type { CathodeDesktop } from './desktop';

const DefaultServer = 'http://127.0.0.1:5180';
const TokenKey = 'cathode.token:';

/**
 * Where the Server is. In the desktop app the preload says (its sidecars, or a remote Server). In a browser: the
 * `server` query parameter; else the page's own origin when the Server served the editor (a headless rig, which marks
 * the page with `<meta name="cathode-server" content="same-origin">`); else a development Server on this machine. The
 * token comes from the `token` query parameter or the one remembered for that Server in this browser. Once it is
 * remembered it leaves the address: there it would be on screen, in the history and in the request line of every page
 * load. {@link showTokenInAddress} puts it back on request, for a Home Screen shortcut or bookmark that opens signed in
 * (an iPad's Home Screen app has storage of its own, so it cannot find the one Safari remembered).
 */
@Injectable({ providedIn: 'root' })
export class ConnectionService {
  readonly desktop: CathodeDesktop | null = typeof window !== 'undefined' ? (window.cathode ?? null) : null;
  readonly connection = signal<Connection | null>(null);
  private api: CathodeClient | null = null;
  /** The address shows the token because {@link showTokenInAddress} was asked to. */
  readonly tokenInAddress = signal(false);

  /**
   * In a browser, the Server refused this connection's token after the editor had started: the token was replaced (the
   * desktop app's New token signs the other devices out). A REST call answered 401, or the events socket was refused.
   * Startup then asks for the new token.
   */
  readonly tokenRefused$ = new Subject<void>();

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
    if (!this.desktop && connection.token && rememberToken(connection.baseUrl, connection.token)) {
      // Not when the browser keeps nothing (a private window): the address is then the only place that has it.
      replaceAddress(window, addressWithoutToken(window.location.href));
      this.tokenInAddress.set(false);
    }

    this.api = createCathodeClient(connection, async request => {
      const response = await fetch(request);
      if (response.status === 401 && !this.desktop) {
        this.tokenRefused$.next();
      }

      return response;
    });
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
    if (!baseUrl) {
      return;
    }

    const kept = rememberToken(baseUrl, token.trim());
    replaceAddress(window, addressAfterToken(window.location.href, token.trim(), kept));
    if (kept) {
      this.tokenInAddress.set(false);
    }
  }

  /**
   * Puts this connection's token in the page's address, in a browser, so a Home Screen shortcut or bookmark made now
   * opens signed in. It stays until the page loads again.
   */
  showTokenInAddress(): void {
    const token = this.connection()?.token;
    if (!this.desktop && token) {
      replaceAddress(window, addressWithToken(window.location.href, token));
      this.tokenInAddress.set(true);
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
 * What the address should become once `token` has been typed: without a token when the browser kept it (the address
 * wins over storage in {@link fromLocation}, so a stale one left there by {@link ConnectionService.showTokenInAddress}
 * would be used instead of the new token, and written back over it), and with it when nothing is kept here, because
 * the address is then the only place that has it. Null leaves the address alone.
 */
export function addressAfterToken(href: string, token: string, kept: boolean): string | null {
  return kept ? addressWithoutToken(href) : addressWithToken(href, token);
}

/** `href` with `token` as its `token` query parameter, or null when it already has it; the hash (the route) is kept. */
export function addressWithToken(href: string, token: string): string | null {
  const url = new URL(href);
  if (url.searchParams.get('token') === token) {
    return null;
  }

  url.searchParams.set('token', token);
  return url.toString();
}

/** `href` without its `token` query parameter, or null when it has none; the rest of the address is kept. */
export function addressWithoutToken(href: string): string | null {
  const url = new URL(href);
  if (!url.searchParams.has('token')) {
    return null;
  }

  url.searchParams.delete('token');
  return url.toString();
}

function replaceAddress(window: Window, address: string | null): void {
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

/** Whether the browser kept it: a private window or blocked storage keeps nothing. */
function rememberToken(baseUrl: string, token: string): boolean {
  try {
    localStorage.setItem(TokenKey + baseUrl, token);
    return localStorage.getItem(TokenKey + baseUrl) === token;
  } catch {
    return false;
  }
}
