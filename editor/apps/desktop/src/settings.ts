import { app } from 'electron';
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

export interface AudioSettings {
    /** Device uid, or null to run the engine on the null backend (no interface). */
    device: string | null;
    sampleRate: number;
    bufferFrames: number;
}

export type ServerTarget = { mode: 'local' } | { mode: 'remote'; baseUrl: string; token: string };

export interface Settings {
    audio: AudioSettings;
    target: ServerTarget;
}

const Defaults: Settings = {
    audio: { device: null, sampleRate: 48_000, bufferFrames: 64 },
    target: { mode: 'local' }
};

const file = () => join(app.getPath('userData'), 'settings.json');

/** The desktop app's own settings, in userData/settings.json. Presets and assets belong to the Server. */
export function loadSettings(): Settings {
    try {
        const stored = JSON.parse(readFileSync(file(), 'utf8')) as Partial<Settings>;
        return { audio: { ...Defaults.audio, ...stored.audio }, target: stored.target ?? Defaults.target };
    } catch {
        return structuredClone(Defaults);
    }
}

export function saveSettings(settings: Settings): void {
    writeFileSync(file(), JSON.stringify(settings, null, 2));
}
