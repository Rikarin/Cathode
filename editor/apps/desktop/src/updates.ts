/** A published release newer than this app. */
export interface Release {
  /** Without the tag's `v`: `1.2.3` or `1.2.3-rc.1`. */
  version: string;
  /** Its page on GitHub, where the installers are. */
  url: string;
}

/** The releases feed: public, no account, and it returns the newest first. */
export const ReleasesUrl = 'https://api.github.com/repos/Rikarin/cathode/releases?per_page=30';
/**
 * A release's page must be one of this repository's on GitHub, whatever the feed says: the app opens it in the
 * browser. GitHub spells the owner and the repository as they were created, so their case is not compared.
 */
function isReleasePage(url: string): boolean {
  const page = URL.parse(url);
  return (
    page?.protocol === 'https:' &&
    page.host === 'github.com' &&
    page.pathname.toLowerCase().startsWith('/rikarin/cathode/releases/')
  );
}

interface Version {
  core: [number, number, number];
  /** The identifiers after `-` (`rc`, `1`); empty for a final release. */
  pre: (string | number)[];
}

/** Reads `1.2.3`, `v1.2.3` and `1.2.3-rc.1` (build metadata after `+` is ignored); null for anything else. */
export function parseVersion(text: string): Version | null {
  const match = /^v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$/.exec(text.trim());
  if (!match) {
    return null;
  }

  const pre = (match[4]?.split('.') ?? []).map(part => (/^\d+$/.test(part) ? Number(part) : part));
  return { core: [Number(match[1]), Number(match[2]), Number(match[3])], pre };
}

/** Semantic-version order: negative when `a` is older than `b`. A prerelease is older than its final release. */
export function compareVersions(a: Version, b: Version): number {
  const core = a.core.map((part, i) => part - (b.core[i] ?? 0)).find(difference => difference !== 0);
  if (core !== undefined) {
    return core;
  }

  if (a.pre.length === 0 || b.pre.length === 0) {
    return b.pre.length - a.pre.length;
  }

  for (const [i, x] of a.pre.entries()) {
    const y = b.pre[i];
    if (y === undefined) {
      break;
    }

    if (x === y) {
      continue;
    }

    // Numbers sort by value and before words; words sort as text.
    if (typeof x === 'number' && typeof y === 'number') {
      return x - y;
    }

    if (typeof x !== typeof y) {
      return typeof x === 'number' ? -1 : 1;
    }

    return x < y ? -1 : 1;
  }

  return a.pre.length - b.pre.length;
}

/**
 * The newest release in GitHub's answer that is newer than `current`, or null. Drafts never count; prereleases count
 * only for someone who runs one (a release candidate is told of the next candidate and of the final release, a final
 * release only of final releases). Entries that are not what the feed documents are skipped.
 */
export function newerRelease(current: string, releases: unknown): Release | null {
  const running = parseVersion(current);
  if (!running || !Array.isArray(releases)) {
    return null;
  }

  let best: { version: Version; release: Release } | null = null;
  for (const entry of releases as unknown[]) {
    const { tag_name, html_url, draft, prerelease } = (entry ?? {}) as Record<string, unknown>;
    if (typeof tag_name !== 'string' || typeof html_url !== 'string' || draft === true) {
      continue;
    }

    const version = parseVersion(tag_name);
    const wanted = version && (running.pre.length > 0 || (prerelease !== true && version.pre.length === 0));
    if (!version || !wanted || !isReleasePage(html_url)) {
      continue;
    }

    if (compareVersions(version, best?.version ?? running) > 0) {
      best = { version, release: { version: tag_name.trim().replace(/^v/, ''), url: html_url } };
    }
  }

  return best?.release ?? null;
}

/** Asks GitHub for the releases and returns the one to tell the user about, or null. Throws when GitHub cannot be asked. */
export async function checkForUpdate(current: string, fetcher: typeof fetch = fetch): Promise<Release | null> {
  const response = await fetcher(ReleasesUrl, {
    headers: { Accept: 'application/vnd.github+json', 'User-Agent': 'Cathode' },
    signal: AbortSignal.timeout(15_000)
  });
  if (!response.ok) {
    throw new Error(`GitHub answered ${response.status}.`);
  }

  return newerRelease(current, await response.json());
}
