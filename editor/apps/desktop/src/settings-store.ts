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

/** Whether the app asks GitHub now and then for a newer release (updates.ts). On by default. */
export interface UpdateSettings {
  check: boolean;
}

export interface Settings {
  audio: AudioSettings;
  target: ServerTarget;
  network: NetworkSettings;
  updates: UpdateSettings;
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
    network: { enabled: false, port: 5180, token: '' },
    updates: { check: true }
  };
}

/** A value read from the renderer or from disk, for a `T`: an object whose fields are all still to be checked. */
export type Unchecked<T> = { readonly [K in keyof T]?: unknown };

/** Neither the renderer nor the file is trusted: everything from them is checked, starting with its being an object. */
export function fields(value: unknown): Record<string, unknown> {
  return typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : {};
}

export function isWholeIn(value: unknown, min: number, max: number): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= min && value <= max;
}

/** Audio settings out of anything: each field is the given one when it is usable, else the default. */
export function validAudio(value: unknown): AudioSettings {
  const audio: Unchecked<AudioSettings> = fields(value);
  const oneOf = (option: unknown, options: readonly number[], fallback: number) =>
    options.find(o => o === option) ?? fallback;
  const device = typeof audio.device === 'string' && audio.device.length < 512 ? audio.device : null;
  const sampleRate = oneOf(audio.sampleRate, [44_100, 48_000, 88_200, 96_000], 48_000);
  const bufferFrames = oneOf(audio.bufferFrames, [32, 64, 128, 256, 512], 64);
  const channels = (list: unknown): number[] =>
    Array.isArray(list) ? (list as unknown[]).filter(c => isWholeIn(c, 1, 64)).slice(0, 2) : [];
  // Outputs 1-4: the first channel of each pair, 0 for unmapped.
  const outputs = Array.isArray(audio.outputs)
    ? (audio.outputs as unknown[]).map(c => (isWholeIn(c, 0, 63) ? c : 0)).slice(0, 4)
    : [1];
  const inputs = Array.isArray(audio.inputs) ? (audio.inputs as unknown[]).slice(0, 6).map(channels) : [];
  const [first = [], ...rest] = inputs;
  return {
    device,
    sampleRate,
    bufferFrames,
    loopIn: channels(audio.loopIn),
    loopOut: channels(audio.loopOut),
    outputs,
    diOut: channels(audio.diOut),
    inputs: first.length > 0 ? inputs : [[1], ...rest]
  };
}

/** The network port, or null when it is not a whole number from 1024 to 65535. */
export function validPort(value: unknown): number | null {
  return isWholeIn(value, 1024, 65_535) ? value : null;
}

/**
 * The Server to use out of anything. A remote one needs an http or https URL, kept as its origin: this throws when
 * `baseUrl` is not one. Anything that is not remote is the local engine.
 */
export function validTarget(value: unknown): ServerTarget {
  const target: Unchecked<{ mode: string; baseUrl: string; token: string }> = fields(value);
  if (target.mode === 'remote') {
    const url = new URL(typeof target.baseUrl === 'string' ? target.baseUrl : '');
    if (url.protocol !== 'http:' && url.protocol !== 'https:') {
      throw new Error('The Server URL must be http or https.');
    }

    return { mode: 'remote', baseUrl: url.origin, token: typeof target.token === 'string' ? target.token : '' };
  }

  return { mode: 'local' };
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
 *
 * What is read is checked like what the renderer sends: the file may have been edited by hand, written by another
 * version or half restored from a backup, and the app has to start whatever it holds.
 */
interface StoredSecret {
  token?: unknown;
  tokenEncrypted?: unknown;
}

interface Stored {
  audio: unknown;
  target: ({ mode?: unknown; baseUrl?: unknown } & StoredSecret) | undefined;
  network: ({ enabled?: unknown; port?: unknown } & StoredSecret) | undefined;
  /** Absent from files written before the update check existed: the default applies. */
  updates: { check?: unknown } | undefined;
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

    let parsed: unknown;
    try {
      parsed = JSON.parse(text);
    } catch (error) {
      this.log(`${this.file} is not valid JSON (${String(error)}); using the defaults.`);
      return defaultSettings();
    }

    if (!isObject(parsed)) {
      this.log(`${this.file} does not hold a settings object; using the defaults.`);
      return defaultSettings();
    }

    const stored: Stored = {
      audio: parsed['audio'],
      target: this.section(parsed, 'target'),
      network: this.section(parsed, 'network'),
      updates: this.section(parsed, 'updates')
    };
    const available = await this.cipher.available();
    const loose = this.tightenMode();
    const network = await this.secret(stored.network, 'network', available);
    const target = await this.target(stored.target, available);
    const settings: Settings = {
      audio: this.audio(stored.audio),
      target: target.value,
      network: { ...this.network(stored.network), token: network.token },
      updates: {
        check: typeof stored.updates?.check === 'boolean' ? stored.updates.check : defaultSettings().updates.check
      }
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
    this.writeAtomically(
      `${JSON.stringify({ audio: settings.audio, target, network, updates: settings.updates }, null, 2)}\n`
    );
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
    try {
      const value = validTarget({ mode: 'remote', baseUrl: stored.baseUrl, token: secret.token });
      return { value, rewrite: secret.rewrite };
    } catch {
      // Without a usable address there is no Server to show, and the editor is where one switches back: go local.
      this.log(
        `the remote Server address in ${this.file} (${JSON.stringify(stored.baseUrl)}) is not an http or https URL; ` +
          'using the local engine.'
      );
      return { value: { mode: 'local' }, rewrite: false };
    }
  }

  /** A section of the file when it is an object, as it should be; otherwise as if it were missing, with a log line. */
  private section(
    parsed: Record<string, unknown>,
    name: 'target' | 'network' | 'updates'
  ): Record<string, unknown> | undefined {
    const value = parsed[name];
    if (value === undefined || isObject(value)) {
      return value;
    }

    this.log(`"${name}" in ${this.file} is not an object; using the defaults for it.`);
    return undefined;
  }

  /** The stored audio settings, each field checked; the fields that were not usable are named in the log. */
  private audio(stored: unknown): AudioSettings {
    const audio = validAudio(stored);
    if (stored === undefined) {
      return audio;
    }

    if (!isObject(stored)) {
      this.log(`"audio" in ${this.file} is not an object; using the defaults for it.`);
      return audio;
    }

    // A file from an older version lacks the newer fields: those take their defaults quietly.
    const replaced = (Object.keys(audio) as (keyof AudioSettings)[]).filter(
      key => stored[key] !== undefined && JSON.stringify(stored[key]) !== JSON.stringify(audio[key])
    );
    if (replaced.length > 0) {
      this.log(`the audio settings in ${this.file} had unusable values (${replaced.join(', ')}); they were replaced.`);
    }

    return audio;
  }

  /** Whether the network is on and its port. A port that is not one turns the setting off, at the default port. */
  private network(stored: Stored['network']): Omit<NetworkSettings, 'token'> {
    const defaults = defaultSettings().network;
    const port = stored?.port === undefined ? defaults.port : validPort(stored.port);
    if (port === null) {
      this.log(
        `the network port in ${this.file} (${JSON.stringify(stored?.port)}) is not a whole number from 1024 to ` +
          '65535; control from other devices is off.'
      );
      return { enabled: false, port: defaults.port };
    }

    return { enabled: stored?.enabled === true, port };
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

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
