import type { components } from '../generated/schema';

type Schemas = components['schemas'];

export type Preset = Schemas['Preset'];
export type PresetBlock = Schemas['PresetBlock'];
export type PresetCable = Schemas['PresetCable'];
export type Scene = Schemas['Scene'];
export type SceneBlockState = Schemas['SceneBlockState'];
export type LiveState = Schemas['LiveState'];
export type PresetSummary = Schemas['PresetSummary'];
export type AssetInfo = Schemas['AssetInfo'];
export type SystemInfo = Schemas['SystemInfo'];
export type EngineStatus = Schemas['EngineStatus'];
export type BlockTypeInfo = Schemas['BlockTypeInfo'];
export type ParameterInfo = Schemas['ParameterInfo'];
export type ImportResult = Schemas['ImportResult'];

/** Built-in grid cells (BuiltInTypes in Cathode.Contracts). */
export const BuiltInTypes = { input: 'input', output: 'output', shunt: 'shunt' } as const;

export const ChannelNames = ['A', 'B', 'C', 'D'] as const;
export type ChannelName = (typeof ChannelNames)[number];

// WebSocket messages (/ws). Not part of the OpenAPI document; they mirror RigEvent and Telemetry in Cathode.Contracts.

/** Pushed after every change to the live rig, whatever its source. */
export interface RigEvent {
    type: string;
    revision: number;
    block?: string;
    channel?: string;
    param?: string;
    value?: number;
    scene?: number;
}

export interface TunerInfo {
    frequency: number;
    note: string;
    octave: number;
    cents: number;
}

/** Pushed about 30 times a second. */
export interface Telemetry {
    type: 'telemetry';
    inputPeak: number;
    outputPeakLeft: number;
    outputPeakRight: number;
    cpuP999: number;
    overloads: number;
    tuner?: TunerInfo;
}

export type ServerMessage = { type: 'connected' } | Telemetry | RigEvent;
