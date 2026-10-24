import type { Connection } from '@cathode/api-client';

/** An audio interface as the engine host reports it (`cathode-host --devices --json`). */
export interface AudioDevice {
  uid: string;
  name: string;
  inputChannels: number;
  outputChannels: number;
  sampleRate: number;
  /** The driver's name for each input channel (empty where it names none). */
  inputNames?: string[];
}

export interface AudioSettings {
  /** Device uid, or null to run without audio (the null backend). */
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
  /** Device input channels (1-based, up to two each) for Input 1–4, USB 5/6 and USB 7/8; empty leaves one silent. */
  inputs?: number[][];
}

export interface LatencyResult {
  ok: boolean;
  /** The host's report, as printed. */
  output: string;
  roundTripMs: number | null;
}

export interface IrCaptureResult {
  ok: boolean;
  output: string;
  /** The captured IR as a WAV file. */
  wav: Uint8Array | null;
}

/** Which Server the desktop app talks to: its own sidecars, or a remote one. */
export type ServerTarget = { mode: 'local' } | { mode: 'remote'; baseUrl: string; token: string };

export interface SidecarStatus {
  name: 'host' | 'server';
  state: 'starting' | 'running' | 'restarting' | 'stopped' | 'failed';
  detail: string | null;
}

/**
 * What the Electron preload exposes as `window.cathode` (apps/desktop/src/preload.ts). Absent when the editor runs in
 * a plain browser, where audio setup is not available.
 */
export interface CathodeDesktop {
  /** Node's `process.platform` of the desktop app ('darwin' on macOS). */
  platform: string;
  /** Called when the window enters or leaves full screen. */
  onFullScreen(listener: (fullScreen: boolean) => void): void;
  /** The Server to use; in local mode it resolves once the sidecars answer. */
  connection(): Promise<Connection>;
  target(): Promise<ServerTarget>;
  /** Switches Server and reloads the window. */
  setTarget(target: ServerTarget): Promise<void>;
  sidecars(): Promise<SidecarStatus[]>;
  audio: {
    devices(): Promise<{ backend: string; devices: AudioDevice[] }>;
    settings(): Promise<AudioSettings>;
    /** Restarts the engine host with these settings. */
    apply(settings: AudioSettings): Promise<void>;
    /** Stops the host, measures round-trip latency through a loopback cable, and restarts it. */
    latencyTest(): Promise<LatencyResult>;
    /** Stops the host, plays a sweep on outputs 1-2, records input 1 and returns the IR; restarts the host. */
    irCapture(options: { seconds: number; levelDb: number; lengthMs: number }): Promise<IrCaptureResult>;
  };
}

declare global {
  interface Window {
    cathode?: CathodeDesktop;
  }
}
