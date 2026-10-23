import type { Connection } from '@cathode/api-client';

/** An audio interface as the engine host reports it (`cathode-host --devices --json`). */
export interface AudioDevice {
    uid: string;
    name: string;
    inputChannels: number;
    outputChannels: number;
    sampleRate: number;
}

export interface AudioSettings {
    /** Device uid, or null to run without audio (the null backend). */
    device: string | null;
    sampleRate: number;
    bufferFrames: number;
}

export interface LatencyResult {
    ok: boolean;
    /** The host's report, as printed. */
    output: string;
    roundTripMs: number | null;
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
    };
}

declare global {
    interface Window {
        cathode?: CathodeDesktop;
    }
}
