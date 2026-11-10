import type { ServerTarget } from './settings-store';

/** The local Server: loopback, on a port chosen at each start. */
const LocalServers = 'http://127.0.0.1:* ws://127.0.0.1:*';

/**
 * Allows the renderer to reach exactly the Server it uses, and nothing else. It never throws: it runs for every file
 * the editor loads, and a remote address that is no URL must not keep the editor, the one place to change it, from
 * loading. Such an address gets the local policy, which reaches no remote Server at all.
 */
export function contentSecurityPolicy(target: ServerTarget): string {
  return [
    "default-src 'self'",
    "script-src 'self'",
    // Angular and the overlay CDK set inline styles.
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data: blob:",
    "font-src 'self' data:",
    `connect-src 'self' ${servers(target)}`,
    "object-src 'none'",
    "base-uri 'self'",
    "frame-ancestors 'none'"
  ].join('; ');
}

function servers(target: ServerTarget): string {
  if (target.mode !== 'remote') {
    return LocalServers;
  }

  let remote: URL;
  try {
    remote = new URL(target.baseUrl);
  } catch {
    return LocalServers;
  }

  // Only http and https have an origin to allow; anything else would put "null" in the policy.
  if (remote.protocol !== 'http:' && remote.protocol !== 'https:') {
    return LocalServers;
  }

  return `${remote.origin} ${remote.protocol === 'https:' ? 'wss' : 'ws'}://${remote.host}`;
}
