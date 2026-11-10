import { describe, expect, it } from 'vitest';
import { contentSecurityPolicy } from './content-security-policy';

const connect = (policy: string) => policy.split('; ').find(part => part.startsWith('connect-src'));

describe('contentSecurityPolicy', () => {
  it('reaches the local Server on loopback', () => {
    expect(connect(contentSecurityPolicy({ mode: 'local' }))).toBe(
      "connect-src 'self' http://127.0.0.1:* ws://127.0.0.1:*"
    );
  });

  it('reaches exactly the remote Server, over its own scheme', () => {
    expect(connect(contentSecurityPolicy({ mode: 'remote', baseUrl: 'http://rig.local:5180', token: '' }))).toBe(
      "connect-src 'self' http://rig.local:5180 ws://rig.local:5180"
    );
    expect(connect(contentSecurityPolicy({ mode: 'remote', baseUrl: 'https://rig.example', token: '' }))).toBe(
      "connect-src 'self' https://rig.example wss://rig.example"
    );
  });

  it.each(['', 'rig.local:5180', 'not a url', 'file:///etc/passwd'])(
    'falls back to the local policy for the address %j instead of throwing',
    baseUrl => {
      expect(contentSecurityPolicy({ mode: 'remote', baseUrl, token: '' })).toBe(
        contentSecurityPolicy({ mode: 'local' })
      );
    }
  );
});
