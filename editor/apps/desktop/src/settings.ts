import { app } from 'electron';
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

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

const Defaults: Settings = {
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

const file = () => join(app.getPath('userData'), 'settings.json');

/** The desktop app's own settings, in userData/settings.json. Presets and assets belong to the Server. */
export function loadSettings(): Settings {
  try {
    const stored = JSON.parse(readFileSync(file(), 'utf8')) as Partial<Settings>;
    return {
      audio: { ...Defaults.audio, ...stored.audio },
      target: stored.target ?? Defaults.target,
      network: { ...Defaults.network, ...stored.network }
    };
  } catch {
    return structuredClone(Defaults);
  }
}

export function saveSettings(settings: Settings): void {
  writeFileSync(file(), JSON.stringify(settings, null, 2));
}
