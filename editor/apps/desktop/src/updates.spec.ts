import { describe, expect, it } from 'vitest';
import { ReleasesUrl, checkForUpdate, compareVersions, newerRelease, parseVersion } from './updates';

const page = (tag: string) => `https://github.com/Rikarin/cathode/releases/tag/${tag}`;
const release = (tag: string, more: object = {}) => ({
  tag_name: tag,
  html_url: page(tag),
  draft: false,
  prerelease: tag.includes('-'),
  ...more
});

describe('parseVersion', () => {
  it('reads final and prerelease versions, with or without the v', () => {
    expect(parseVersion('1.2.3')).toEqual({ core: [1, 2, 3], pre: [] });
    expect(parseVersion('v1.0.0-rc.5')).toEqual({ core: [1, 0, 0], pre: ['rc', 5] });
    expect(parseVersion('1.0.0+build.7')).toEqual({ core: [1, 0, 0], pre: [] });
  });

  it('refuses what is not a version', () => {
    for (const text of ['', 'latest', '1.2', '1.2.3.4', 'v1.2.x']) {
      expect(parseVersion(text)).toBeNull();
    }
  });
});

describe('compareVersions', () => {
  const order = ['1.0.0-alpha', '1.0.0-rc.1', '1.0.0-rc.5', '1.0.0-rc.10', '1.0.0-rc.10.1', '1.0.0', '1.0.1', '1.1.0'];

  it('orders versions as semantic versioning does', () => {
    const versions = order.map(text => parseVersion(text) ?? { core: [0, 0, 0] as [number, number, number], pre: [] });
    for (const [i, a] of versions.entries()) {
      for (const [j, b] of versions.entries()) {
        expect(Math.sign(compareVersions(a, b)), `${order[i]} against ${order[j]}`).toBe(Math.sign(i - j));
      }
    }
  });
});

describe('newerRelease', () => {
  const feed = [release('v1.1.0-rc.1'), release('v1.0.1'), release('v1.0.0'), release('v1.0.0-rc.5')];

  it('tells a final release of the newest final release only', () => {
    expect(newerRelease('1.0.0', feed)).toEqual({ version: '1.0.1', url: page('v1.0.1') });
    expect(newerRelease('1.0.1', feed)).toBeNull();
  });

  it('tells a release candidate of newer candidates and of the final release', () => {
    expect(newerRelease('1.0.0-rc.4', [release('v1.0.0-rc.5')])).toEqual({
      version: '1.0.0-rc.5',
      url: page('v1.0.0-rc.5')
    });
    expect(newerRelease('1.0.0-rc.5', feed)?.version).toBe('1.1.0-rc.1');
    expect(newerRelease('1.0.0-rc.5', [release('v1.0.0'), release('v1.0.0-rc.5')])?.version).toBe('1.0.0');
  });

  it('says nothing when this is the newest, or newer than anything published', () => {
    expect(newerRelease('1.0.0-rc.5', [release('v1.0.0-rc.5'), release('v1.0.0-rc.4')])).toBeNull();
    expect(newerRelease('2.0.0', feed)).toBeNull();
  });

  it('skips drafts, releases marked as prereleases, and pages that are not this repository’s', () => {
    expect(newerRelease('1.0.0', [release('v1.2.0', { draft: true })])).toBeNull();
    expect(newerRelease('1.0.0', [release('v1.2.0', { prerelease: true })])).toBeNull();
    expect(newerRelease('1.0.0', [release('v1.2.0', { html_url: 'https://example.com/cathode' })])).toBeNull();
  });

  it('takes the page as GitHub spells the repository', () => {
    const url = 'https://github.com/Rikarin/Cathode/releases/tag/v1.0.1';
    expect(newerRelease('1.0.0', [release('v1.0.1', { html_url: url })])).toEqual({ version: '1.0.1', url });
    expect(
      newerRelease('1.0.0', [
        release('v1.0.1', { html_url: 'https://github.com.example.com/Rikarin/cathode/releases/' })
      ])
    ).toBeNull();
  });

  it('survives an answer that is not the feed', () => {
    expect(newerRelease('1.0.0', { message: 'API rate limit exceeded' })).toBeNull();
    expect(newerRelease('1.0.0', [null, 7, { tag_name: 'nightly', html_url: page('nightly') }, {}])).toBeNull();
    expect(newerRelease('not a version', feed)).toBeNull();
  });
});

describe('checkForUpdate', () => {
  it('asks the releases feed and returns the newer release', async () => {
    const asked: string[] = [];
    const fetcher = ((url: string) => {
      asked.push(url);
      return Promise.resolve(Response.json([release('v1.0.1')]));
    }) as typeof fetch;
    expect(await checkForUpdate('1.0.0', fetcher)).toEqual({ version: '1.0.1', url: page('v1.0.1') });
    expect(asked).toEqual([ReleasesUrl]);
  });

  it('fails when GitHub refuses, so the caller can say the check did not happen', async () => {
    const fetcher = (() => Promise.resolve(new Response('', { status: 403 }))) as typeof fetch;
    await expect(checkForUpdate('1.0.0', fetcher)).rejects.toThrow('GitHub answered 403.');
  });
});
