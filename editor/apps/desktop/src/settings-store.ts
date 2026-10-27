import { randomBytes } from 'node:crypto';
import {
  chmodSync,
  closeSync,
  fsyncSync,
  mkdirSync,
  openSync,
  readFileSync,
  renameSync,
  statSync,
  unlinkSync,
  writeSync
} from 'node:fs';
import { basename, dirname, join } from 'node:path';

export interface AudioSettings {
  /** Device uid, or null to run the engine on the null backend (no interface). */
  device: string | null;
  sampleRate: number;
  bufferFrames: number;
  /** Device input channels (1-based, up to two) an external effects loop returns on; empty for none. */
  loopIn: number[];
  /** Device output channels (1-based, up to two) that feed an external effects loop; empty for none. */
  loopOut: number[];
  /**
   * The first device channel (1-based) of Outputs 1-4, each a pair; 0 leaves an output unmapped. Missing entries are
   * unmapped, and an empty list keeps the default: Output 1 on channels 1-2.
   */
  outputs: number[];
  /** Device output channels (1-based, one or two) for the dry DI out, the input before Input 1 Gain; empty for none. */
  diOut: number[];
  /**
   * Device input channels (1-based, up to two each) for the hardware input sources in order: Input 1–4, USB 5/6,
   * USB 7/8. One channel feeds both sides; an empty list leaves the source silent. Input 1 defaults to channel 1.
   */
  inputs: number[][];
}

export type ServerTarget = { mode: 'local' } | { mode: 'remote'; baseUrl: string; token: string };

/**
 * Control from other devices: the local Server also listens on every network interface at `port`, serves the web
 * editor there and wants `token`, which is kept so a tablet that has it stays signed in. Off by default.
 */
export interface NetworkSettings {
  enabled: boolean;
  port: number;
  /** The access token, made the first time the setting is turned on; empty until then. */
  token: string;
}

export interface Settings {
  audio: AudioSettings;
  target: ServerTarget;
  network: NetworkSettings;
}

export function defaultSettings(): Settings {
  return {
    audio: {
      device: null,
      sampleRate: 48_000,
      bufferFrames: 64,
      loopIn: [],
      loopOut: [],
      outputs: [1],
      diOut: [],
      inputs: [[1]]
    },
    target: { mode: 'local' },
    network: { enabled: false, port: 5180, token: '' }
  };
}

/** The part of Electron's safeStorage the store uses (OS keychain, DPAPI or the Linux secret service). */
export interface SecretCipher {
  /** False where secrets cannot be kept from other local users (no keyring on Linux): tokens are then clear text. */
  available(): Promise<boolean>;
  encrypt(plain: string): Promise<Buffer>;
  /** `shouldReEncrypt` when the key has been rotated since: the token is then written again with the new one. */
  decrypt(cipher: Buffer): Promise<{ result: string; shouldReEncrypt: boolean }>;
}

export interface SettingsStoreOptions {
  cipher: SecretCipher;
  platform?: NodeJS.Platform;
  log?: (message: string) => void;
  /** Replaces the file atomically; injectable so the Windows sharing-violation retry can be tested. */
  rename?: (from: string, to: string) => void;
  /** Blocks for a while between rename attempts. */
  sleep?: (ms: number) => void;
}

/**
 * On disk the file stays plain JSON in the shape of `Settings`, except that a token is kept as `tokenEncrypted`
 * (base64 of the safeStorage cipher text) instead of `token` wherever safeStorage works. A file with clear-text tokens
 * (older versions, or no keyring) still reads, and is rewritten encrypted the first time it is read with a keyring.
 */
interface StoredSecret {
  token?: unknown;
  tokenEncrypted?: unknown;
}

interface Stored {
  audio?: Partial<AudioSettings>;
  target?: { mode?: unknown; baseUrl?: unknown } & StoredSecret;
  network?: Partial<Omit<NetworkSettings, 'token'>> & StoredSecret;
}

/** A token as read: decrypted, and whether the file should be written again (clear text, or an old key). */
interface ReadSecret {
  token: string;
  rewrite: boolean;
}

/** Owner-only: the file holds the tokens that open this rig (and a remote one) to the network. */
const FileMode = 0o600;
const FolderMode = 0o700;
/** Windows refuses to replace a file someone holds open without delete sharing (an indexer, an antivirus scan). */
const RenameRetries = 10;
const RetryableRename = new Set(['EPERM', 'EACCES', 'EBUSY']);

const blockFor = (ms: number) => Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);

/** Reads and writes the desktop app's settings.json: atomically, owner-only, and with the tokens encrypted at rest. */
export class SettingsStore {
  private readonly cipher: SecretCipher;
  private readonly platform: NodeJS.Platform;
  private readonly log: (message: string) => void;
  private readonly rename: (from: string, to: string) => void;
  private readonly sleep: (ms: number) => void;
  private warnedClearText = false;
  /** One save at a time, in the order asked: encrypting is asynchronous, and an earlier save must not win. */
  private saving: Promise<void> = Promise.resolve();

  constructor(
    private readonly file: string,
    options: SettingsStoreOptions
  ) {
    this.cipher = options.cipher;
    this.platform = options.platform ?? process.platform;
    this.log = options.log ?? (message => console.warn(`Cathode: ${message}`));
    this.rename = options.rename ?? renameSync;
    this.sleep = options.sleep ?? blockFor;
  }

  async load(): Promise<Settings> {
    let text: string;
    try {
      text = readFileSync(this.file, 'utf8');
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== 'ENOENT') {
        this.log(`could not read ${this.file} (${String(error)}); using the defaults.`);
      }

      return defaultSettings();
    }

    let stored: Stored;
    try {
      stored = JSON.parse(text) as Stored;
    } catch (error) {
      this.log(`${this.file} is not valid JSON (${String(error)}); using the defaults.`);
      return defaultSettings();
    }

    const defaults = defaultSettings();
    const available = await this.cipher.available();
    const loose = this.tightenMode();
    const network = await this.secret(stored.network, 'network', available);
    const target = await this.target(stored.target, available);
    const settings: Settings = {
      audio: { ...defaults.audio, ...stored.audio },
      target: target.value,
      network: { ...defaults.network, ...withoutSecret(stored.network), token: network.token }
    };

    // A clear-text token from an older version (or one under a rotated key), now that it can be kept encrypted.
    if (available && (loose || network.rewrite || target.rewrite)) {
      try {
        await this.save(settings);
      } catch (error) {
        this.log(`could not rewrite ${this.file} with encrypted tokens (${String(error)}).`);
      }
    }

    return settings;
  }

  save(settings: Settings): Promise<void> {
    const next = this.saving.then(() => this.write(settings));
    this.saving = next.catch(() => undefined);
    return next;
  }

  private async write(settings: Settings): Promise<void> {
    const encrypt = await this.cipher.available();
    if (!encrypt && !this.warnedClearText) {
      this.warnedClearText = true;
      this.log(
        `no OS keyring for safeStorage; the access tokens in ${this.file} are kept in clear text (owner-only file).`
      );
    }

    const target =
      settings.target.mode === 'remote'
        ? {
            mode: 'remote',
            baseUrl: settings.target.baseUrl,
            ...(await this.storeSecret(settings.target.token, encrypt))
          }
        : settings.target;
    const network = {
      enabled: settings.network.enabled,
      port: settings.network.port,
      ...(await this.storeSecret(settings.network.token, encrypt))
    };
    this.writeAtomically(`${JSON.stringify({ audio: settings.audio, target, network }, null, 2)}\n`);
  }

  private async storeSecret(token: string, encrypt: boolean): Promise<{ token: string } | { tokenEncrypted: string }> {
    return encrypt && token !== ''
      ? { tokenEncrypted: (await this.cipher.encrypt(token)).toString('base64') }
      : { token };
  }

  private async target(
    stored: Stored['target'],
    available: boolean
  ): Promise<{ value: ServerTarget; rewrite: boolean }> {
    if (stored?.mode !== 'remote') {
      return { value: { mode: 'local' }, rewrite: false };
    }

    const secret = await this.secret(stored, 'remote Server', available);
    const baseUrl = typeof stored.baseUrl === 'string' ? stored.baseUrl : '';
    return { value: { mode: 'remote', baseUrl, token: secret.token }, rewrite: secret.rewrite };
  }

  private async secret(stored: StoredSecret | undefined, what: string, available: boolean): Promise<ReadSecret> {
    if (typeof stored?.tokenEncrypted === 'string' && stored.tokenEncrypted !== '') {
      if (!available) {
        this.log(`the ${what} token in ${this.file} is encrypted, but safeStorage is unavailable; it is dropped.`);
        return { token: '', rewrite: false };
      }

      try {
        const plain = await this.cipher.decrypt(Buffer.from(stored.tokenEncrypted, 'base64'));
        return { token: plain.result, rewrite: plain.shouldReEncrypt };
      } catch (error) {
        this.log(`could not decrypt the ${what} token in ${this.file} (${String(error)}); it is dropped.`);
        return { token: '', rewrite: false };
      }
    }

    const token = typeof stored?.token === 'string' ? stored.token : '';
    return { token, rewrite: token !== '' };
  }

  /** An older version wrote the file world-readable: make it owner-only. True when it was not. */
  private tightenMode(): boolean {
    if (this.platform === 'win32') {
      return false;
    }

    try {
      if ((statSync(this.file).mode & 0o077) !== 0) {
        chmodSync(this.file, FileMode);
        return true;
      }
    } catch {
      // Read a moment ago; if it has gone since, the next save makes it right.
    }

    return false;
  }

  /**
   * Writes a temporary file beside the real one (same folder, so the same volume), flushes it to disk and renames it
   * over the real one: a crash leaves the old settings or the new, never half a file.
   */
  private writeAtomically(text: string): void {
    const folder = dirname(this.file);
    mkdirSync(folder, { recursive: true, mode: FolderMode });
    const temp = join(folder, `.${basename(this.file)}.${randomBytes(6).toString('hex')}.tmp`);
    try {
      const fd = openSync(temp, 'wx', FileMode);
      try {
        writeSync(fd, text);
        fsyncSync(fd);
      } finally {
        closeSync(fd);
      }

      this.replace(temp);
    } catch (error) {
      try {
        unlinkSync(temp);
      } catch {
        // Never made, or already renamed.
      }

      throw error;
    }

    this.syncFolder(folder);
  }

  private replace(temp: string): void {
    for (let attempt = 0; ; attempt++) {
      try {
        this.rename(temp, this.file);
        return;
      } catch (error) {
        const code = (error as NodeJS.ErrnoException).code ?? '';
        if (this.platform !== 'win32' || !RetryableRename.has(code) || attempt + 1 >= RenameRetries) {
          throw error;
        }

        this.sleep(10 * (attempt + 1));
      }
    }
  }

  /** Makes the rename itself durable (POSIX; Windows has no directory handles to flush). */
  private syncFolder(folder: string): void {
    if (this.platform === 'win32') {
      return;
    }

    try {
      const fd = openSync(folder, 'r');
      try {
        fsyncSync(fd);
      } finally {
        closeSync(fd);
      }
    } catch {
      // Some file systems refuse to sync a directory; the file itself is already on disk.
    }
  }
}

function withoutSecret<T extends StoredSecret>(stored: T | undefined): Omit<T, 'token' | 'tokenEncrypted'> {
  if (!stored) {
    return {} as Omit<T, 'token' | 'tokenEncrypted'>;
  }

  const { token: _token, tokenEncrypted: _tokenEncrypted, ...rest } = stored;
  return rest;
}
