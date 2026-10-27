import { execFileSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { createServer } from 'node:net';
import { networkInterfaces } from 'node:os';
import qrcode from 'qrcode-generator';
import type { NetworkSettings } from './settings';

/** What Settings ▸ Audio setup shows about control from other devices. */
export interface NetworkStatus {
  enabled: boolean;
  port: number;
  token: string;
  /** Addresses to open on another device, best first; empty when off. */
  urls: string[];
  /** The first URL with the token, for a tablet's camera: a QR code as SVG path data, `size` modules square. */
  qr: { path: string; size: number; url: string } | null;
  /** Why the Server is not listening on the network although the setting is on (the port is taken). */
  error: string | null;
}

/** A new access token for the network; long enough that it cannot be guessed. */
export function newToken(): string {
  return randomBytes(24).toString('base64url');
}

/** Whether a port is free on every interface, as the Server would take it. */
export function portFree(port: number): Promise<boolean> {
  return new Promise(done => {
    const probe = createServer();
    probe.once('error', () => done(false));
    probe.listen(port, '0.0.0.0', () => probe.close(() => done(true)));
  });
}

/**
 * Where another device finds this computer: its IPv4 addresses on the network, then its Bonjour name on macOS (Safari
 * on an iPad resolves `.local`, and the name survives a new address). Loopback, link-local and IPv6 addresses are left
 * out: a tablet cannot use them.
 */
export function networkUrls(port: number): string[] {
  const addresses = Object.values(networkInterfaces())
    .flatMap(list => list ?? [])
    .filter(a => a.family === 'IPv4' && !a.internal && !a.address.startsWith('169.254.'))
    .map(a => a.address);
  const name = bonjourName();
  return [...new Set([...addresses, ...(name ? [`${name}.local`] : [])])].map(host => `http://${host}:${port}`);
}

let bonjour: string | null | undefined;

/** The Mac's local host name (System Settings ▸ General ▸ Sharing), which Bonjour answers for as `<name>.local`. */
function bonjourName(): string | null {
  if (bonjour === undefined) {
    try {
      bonjour =
        process.platform === 'darwin'
          ? execFileSync('scutil', ['--get', 'LocalHostName'], { encoding: 'utf8', timeout: 2_000 }).trim() || null
          : null;
    } catch {
      bonjour = null;
    }
  }

  return bonjour;
}

export function networkStatus(network: NetworkSettings, error: string | null): NetworkStatus {
  const urls = network.enabled ? networkUrls(network.port) : [];
  const first = urls[0] ? `${urls[0]}/?token=${encodeURIComponent(network.token)}` : null;
  return { ...network, urls, qr: first ? qrPath(first) : null, error: network.enabled ? error : null };
}

/** A QR code (error correction M) as one SVG path: a 1×1 square per dark module, no quiet zone. */
function qrPath(text: string): { path: string; size: number; url: string } {
  const code = qrcode(0, 'M');
  code.addData(text);
  code.make();
  const size = code.getModuleCount();
  let path = '';
  for (let row = 0; row < size; row++) {
    for (let column = 0; column < size; column++) {
      if (code.isDark(row, column)) {
        path += `M${column} ${row}h1v1h-1z`;
      }
    }
  }

  return { path, size, url: text };
}
