import { app, safeStorage } from 'electron';
import { join } from 'node:path';
import { type SecretCipher, type Settings, SettingsStore } from './settings-store';

export {
  type AudioSettings,
  type NetworkSettings,
  type ServerTarget,
  type Settings,
  defaultSettings
} from './settings-store';

/**
 * Electron's safeStorage, which works once the app is ready. On Linux without a keyring it falls back to a password
 * baked into Chromium ("basic_text"), which hides nothing from another local user: that counts as unavailable, and the
 * tokens stay in clear text in the owner-only file.
 */
const cipher: SecretCipher = {
  available: async () =>
    (await safeStorage.isAsyncEncryptionAvailable()) &&
    (process.platform !== 'linux' || safeStorage.getSelectedStorageBackend() !== 'basic_text'),
  encrypt: plain => safeStorage.encryptStringAsync(plain),
  decrypt: encrypted => safeStorage.decryptStringAsync(encrypted)
};

let store: SettingsStore | null = null;
const settingsStore = () => (store ??= new SettingsStore(join(app.getPath('userData'), 'settings.json'), { cipher }));

/**
 * The desktop app's own settings, in userData/settings.json. Presets and assets belong to the Server. Call it once the
 * app is ready: safeStorage cannot decrypt the tokens before that.
 */
export function loadSettings(): Promise<Settings> {
  return settingsStore().load();
}

/** Saves in the order called; each save replaces the file whole. */
export function saveSettings(settings: Settings): Promise<void> {
  return settingsStore().save(settings);
}
