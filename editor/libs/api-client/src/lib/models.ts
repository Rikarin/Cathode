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
export type PresetModifier = Schemas['PresetModifier'];
export type GlobalSettings = Schemas['GlobalSettings'];
export type MidiMapping = Schemas['MidiMapping'];
export type FootswitchLayout = Schemas['FootswitchLayout'];
export type Footswitch = Schemas['Footswitch'];
export type SwitchAction = Schemas['SwitchAction'];
export type GlobalBlock = Schemas['GlobalBlock'];
export type IrMixRequest = Schemas['IrMixRequest'];
export type IrMixSource = Schemas['IrMixSource'];
export type ToneMatchRequest = Schemas['ToneMatchRequest'];

/** Modifier sources (Cathode.Engine.Rig.ModNames). */
export const ModifierSources = [
    'lfo1',
    'lfo2',
    'adsr1',
    'adsr2',
    'seq',
    'env',
    'pitch',
    'manualA',
    'manualB',
    'manualC',
    'manualD',
    'scene1',
    'scene2',
    'scene3',
    'scene4',
    'ext1',
    'ext2',
    'ext3',
    'ext4',
    'ext5',
    'ext6',
    'ext7',
    'ext8'
] as const;

export const ModifierCurves = ['linear', 'exp', 'log', 'scurve'] as const;

export const BypassModes = ['thru', 'mute', 'muteIn', 'muteFxIn'] as const;

/** Actions MIDI mappings and footswitches can trigger (GlobalValidation on the Server). */
export const MidiActions = [
    'scene',
    'sceneNext',
    'scenePrevious',
    'presetSlot',
    'presetNext',
    'presetPrevious',
    'bypass',
    'channel',
    'external',
    'manual',
    'tap',
    'tuner',
    'metronome',
    'looper'
] as const;

export const LooperActions = ['record', 'play', 'stop', 'overdub', 'undo', 'reverse', 'halfSpeed', 'clear'] as const;

/** The looper as telemetry reports it. */
export interface LooperInfo {
    state: 'empty' | 'recording' | 'playing' | 'overdubbing' | 'stopped';
    lengthSeconds: number;
    positionSeconds: number;
    reversed: boolean;
    halfSpeed: boolean;
}

/** Every MIDI message received (type midi), or the one MIDI learn just bound (type midiLearned). */
export interface MidiActivity {
    type: 'midi' | 'midiLearned';
    status: number;
    data1: number;
    data2: number;
}

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
    tempo: number;
    looper?: LooperInfo;
}

/** Pushed about 30 times a second while the scope is on: recent output samples, oldest first. */
export interface ScopeFrame {
    type: 'scope';
    sampleRate: number;
    samples: number[];
}

/** Pushed about 15 times a second while the RTA is on: the output in sixth-octave bands, 0 dB = a full-scale sine. */
export interface RtaFrame {
    type: 'rta';
    frequencies: number[];
    levels: number[];
}

export type ServerMessage = { type: 'connected' } | Telemetry | ScopeFrame | RtaFrame | MidiActivity | RigEvent;
