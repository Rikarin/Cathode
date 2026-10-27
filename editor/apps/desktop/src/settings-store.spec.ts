import { chmodSync, mkdtempSync, readFileSync, readdirSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import {
  type SecretCipher,
  type Settings,
  SettingsStore,
  type SettingsStoreOptions,
  defaultSettings
} from './settings-store';

const Prefix = Buffer.from('fake:');

/** Stands in for safeStorage: reversible, and visibly not the clear text. */
class FakeCipher implements SecretCipher {
  constructor(
    public isAvailable = true,
    public rotated = false
  ) {}

  available = () => Promise.resolve(this.isAvailable);
  encrypt = (plain: string) => Promise.resolve(Buffer.concat([Prefix, Buffer.from(plain, 'utf8').reverse()]));
  decrypt = (cipher: Buffer) => {
    if (!cipher.subarray(0, Prefix.length).equals(Prefix)) {
      return Promise.reject(new Error('Not this keyring'));
    }

    const result = Buffer.from(cipher.subarray(Prefix.length)).reverse().toString('utf8');
    return Promise.resolve({ result, shouldReEncrypt: this.rotated });
  };
}

type OnDisk = Record<string, Record<string, unknown> | undefined>;

const posix = process.platform !== 'win32';
const encrypted = expect.any(String) as unknown;

function sample(): Settings {
  const settings = defaultSettings();
  settings.audio.device = 'interface-1';
  settings.network = { enabled: true, port: 5181, token: 'network-secret' };
  settings.target = { mode: 'remote', baseUrl: 'http://rig.local:5180', token: 'remote-secret' };
  return settings;
}

describe('SettingsStore', () => {
  let folder: string;
  let file: string;
  let logs: string[];
  const store = (cipher: SecretCipher, extra: Partial<SettingsStoreOptions> = {}) =>
    new SettingsStore(file, { cipher, log: message => logs.push(message), sleep: () => undefined, ...extra });
  const onDisk = () => JSON.parse(readFileSync(file, 'utf8')) as OnDisk;

  beforeEach(() => {
    folder = mkdtempSync(join(tmpdir(), 'cathode-settings-'));
    file = join(folder, 'user-data', 'settings.json');
    logs = [];
  });

  afterEach(() => rmSync(folder, { recursive: true, force: true }));

  it('reads the defaults when there is no file, without complaint', async () => {
    expect(await store(new FakeCipher()).load()).toEqual(defaultSettings());
    expect(logs).toEqual([]);
  });

  it('keeps the tokens encrypted at rest and reads them back', async () => {
    const cipher = new FakeCipher();
    await store(cipher).save(sample());

    const text = readFileSync(file, 'utf8');
    expect(text).not.toContain('network-secret');
    expect(text).not.toContain('remote-secret');
    const stored = onDisk();
    expect(stored['network']).toEqual({ enabled: true, port: 5181, tokenEncrypted: encrypted });
    expect(stored['target']).toEqual({ mode: 'remote', baseUrl: 'http://rig.local:5180', tokenEncrypted: encrypted });
    // Everything that is not a secret stays readable.
    expect(stored['audio']).toEqual(sample().audio);

    expect(await store(cipher).load()).toEqual(sample());
  });

  it('keeps clear text, and says so, where safeStorage is unavailable', async () => {
    const cipher = new FakeCipher(false);
    await store(cipher).save(sample());

    expect(onDisk()['network']).toEqual({ enabled: true, port: 5181, token: 'network-secret' });
    expect(logs.join('\n')).toContain('clear text');
    expect(await store(cipher).load()).toEqual(sample());
  });

  it('rewrites a clear-text file from an older version encrypted on first read', async () => {
    const old = sample();
    await store(new FakeCipher()).save(defaultSettings());
    writeFileSync(file, JSON.stringify(old));

    const cipher = new FakeCipher();
    expect(await store(cipher).load()).toEqual(old);

    expect(readFileSync(file, 'utf8')).not.toContain('secret');
    expect(await store(cipher).load()).toEqual(old);
  });

  it('rewrites tokens under a rotated key', async () => {
    await store(new FakeCipher()).save(sample());
    writeFileSync(file, readFileSync(file, 'utf8').replace('"port": 5181', '"port": 5182'));

    await store(new FakeCipher(true, true)).load();
    // Rewritten: the formatting is the store's own again.
    expect(readFileSync(file, 'utf8')).toContain('"port": 5182');
    expect(onDisk()['network']?.['tokenEncrypted']).toEqual(encrypted);
  });

  it('reads a partial file from an older version over the defaults', async () => {
    await store(new FakeCipher()).save(defaultSettings());
    writeFileSync(file, JSON.stringify({ audio: { device: 'x' }, network: { enabled: true, token: 't' } }));

    const settings = await store(new FakeCipher()).load();
    expect(settings.audio).toEqual({ ...defaultSettings().audio, device: 'x' });
    expect(settings.network).toEqual({ enabled: true, port: 5180, token: 't' });
    expect(settings.target).toEqual({ mode: 'local' });
  });

  it('drops a token it cannot decrypt, and keeps the rest', async () => {
    await store(new FakeCipher()).save(sample());
    const stored = onDisk();
    stored['network'] = { ...stored['network'], tokenEncrypted: Buffer.from('another keyring').toString('base64') };
    writeFileSync(file, JSON.stringify(stored));

    const settings = await store(new FakeCipher()).load();
    expect(settings.network).toEqual({ enabled: true, port: 5181, token: '' });
    expect(settings.target).toEqual(sample().target);
    expect(logs.join('\n')).toContain('could not decrypt');
  });

  it('replaces the file in one step and leaves no temporary file behind', async () => {
    const cipher = new FakeCipher();
    await store(cipher).save(defaultSettings());
    await store(cipher).save(sample());

    expect(readdirSync(join(folder, 'user-data'))).toEqual(['settings.json']);
    expect(await store(cipher).load()).toEqual(sample());
  });

  it('writes saves in the order asked', async () => {
    const cipher = new FakeCipher();
    const one = store(cipher);
    const later = sample();
    later.network.port = 6000;
    await Promise.all([one.save(sample()), one.save(later)]);

    expect((await store(cipher).load()).network.port).toBe(6000);
  });

  it('leaves the old file whole when the replacement fails', async () => {
    const cipher = new FakeCipher();
    await store(cipher).save(sample());
    const failing = store(cipher, {
      rename: () => {
        throw Object.assign(new Error('disk full'), { code: 'ENOSPC' });
      }
    });

    await expect(failing.save(defaultSettings())).rejects.toThrow('disk full');
    expect(readdirSync(join(folder, 'user-data'))).toEqual(['settings.json']);
    expect(await store(cipher).load()).toEqual(sample());
  });

  it('retries a rename Windows refuses while someone holds the file open', async () => {
    let refusals = 2;
    const sharing = store(new FakeCipher(), {
      platform: 'win32',
      rename: (from, to) => {
        if (refusals-- > 0) {
          throw Object.assign(new Error('sharing violation'), { code: 'EPERM' });
        }

        renameSync(from, to);
      }
    });

    await sharing.save(sample());
    expect(refusals).toBe(-1);
    expect(await store(new FakeCipher()).load()).toEqual(sample());
  });

  it.skipIf(!posix)('creates the file and its folder owner-only', async () => {
    await store(new FakeCipher()).save(sample());

    expect(statSync(file).mode & 0o777).toBe(0o600);
    expect(statSync(join(folder, 'user-data')).mode & 0o777).toBe(0o700);
  });

  it.skipIf(!posix)('makes a world-readable file from an older version owner-only', async () => {
    await store(new FakeCipher(false)).save(sample());
    chmodSync(file, 0o644);

    await store(new FakeCipher(false)).load();
    expect(statSync(file).mode & 0o777).toBe(0o600);
  });
});
