import type { components } from '../generated/schema';

type Schemas = components['schemas'];

export type Preset = Schemas['Preset'];
export type PresetBlock = Schemas['PresetBlock'];
export type PresetCable = Schemas['PresetCable'];
export type Scene = Schemas['Scene'];
export type SceneBlockState = Schemas['SceneBlockState'];
export type SceneMidiMessage = Schemas['SceneMidiMessage'];
export type BlockPreset = Schemas['BlockPreset'];
export type LiveState = Schemas['LiveState'];
export type PresetPosition = Schemas['PresetPosition'];
export type PresetStep = Schemas['PresetStep'];
export type PresetSummary = Schemas['PresetSummary'];
export type AssetInfo = Schemas['AssetInfo'];
export type AssetUsage = Schemas['AssetUsage'];
export type SystemInfo = Schemas['SystemInfo'];
export type EngineStatus = Schemas['EngineStatus'];
export type BlockTypeInfo = Schemas['BlockTypeInfo'];
export type ParameterInfo = Schemas['ParameterInfo'];
export type ImportResult = Schemas['ImportResult'];
export type PresetModifier = Schemas['PresetModifier'];
export type GlobalSettings = Schemas['GlobalSettings'];
export type OutputSettings = Schemas['OutputSettings'];
export type InputSettings = Schemas['InputSettings'];
export type LatencyInfo = Schemas['LatencyInfo'];
export type LatencyStepInfo = Schemas['LatencyStepInfo'];
export type SpeakerResonanceInfo = Schemas['SpeakerResonanceInfo'];
export type MidiMapping = Schemas['MidiMapping'];
export type MidiLearnRequest = Schemas['MidiLearnRequest'];
export type MidiOutputInfo = Schemas['MidiOutputInfo'];
export type FootswitchLayout = Schemas['FootswitchLayout'];
export type Footswitch = Schemas['Footswitch'];
export type SwitchAction = Schemas['SwitchAction'];
export type GlobalBlock = Schemas['GlobalBlock'];
export type IrMixRequest = Schemas['IrMixRequest'];
export type IrMixSource = Schemas['IrMixSource'];
export type ToneMatchRequest = Schemas['ToneMatchRequest'];
export type PerformControl = Schemas['PerformControl'];
export type FootswitchOverride = Schemas['FootswitchOverride'];
export type SnapshotSummary = Schemas['SnapshotSummary'];
export type BlockPresetFile = Schemas['BlockPresetFile'];
export type BatchResult = Schemas['BatchResult'];
export type Bank = Schemas['Bank'];
export type Setlist = Schemas['Setlist'];
export type SetlistEntry = Schemas['SetlistEntry'];
export type ScenePairRequest = Schemas['ScenePairRequest'];

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

export const BypassModes = ['thru', 'mute', 'muteIn', 'muteFxIn', 'muteOut', 'muteFxOut'] as const;

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
  'mute',
  'looper',
  'sceneLevelUp',
  'sceneLevelDown',
  'save',
  'layout',
  'view',
  'viewNext',
  'viewPrevious',
  'songNext',
  'songPrevious'
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

/**
 * Every MIDI message received (type midi), the one MIDI learn just bound (type midiLearned), or MIDI learn armed or
 * cancelled (type midiLearnChanged, with zeros: read `GET /midi/learn`).
 */
export interface MidiActivity {
  type: 'midi' | 'midiLearned' | 'midiLearnChanged';
  status: number;
  data1: number;
  data2: number;
}

/** Built-in grid cells (BuiltInTypes in Cathode.Contracts). */
export const BuiltInTypes = {
  input: 'input',
  output: 'output',
  output2: 'output2',
  output3: 'output3',
  output4: 'output4',
  shunt: 'shunt'
} as const;

/** Output 1 (the main output) and Outputs 2-4. */
export const OutputTypes: readonly string[] = ['output', 'output2', 'output3', 'output4'];

/** Output modes, as the global settings store them (GlobalValidation.OutputModes on the Server). */
export const OutputModes = ['stereo', 'sum', 'copyLR'] as const;

/** The global oversampling filters. */
export const OversamplingModes = ['minimum', 'linear'] as const;

/** What the outputs play while the tuner is on (GlobalValidation.TunerOutputs on the Server). */
export const TunerOutputs = ['mute', 'bypass', 'passThrough'] as const;

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
  /**
   * Every block meter since the last frame, by block id and then meter id (absent without shared-memory telemetry):
   * each block's `peak` (its output peak, linear) and the meters some blocks keep of their own, in dB: the Cab's
   * `preampIn` (the level into its mic preamp, 0 at the onset of clipping) and `motorReduction` (Motor Drive's gain
   * reduction). Each is the highest value since the previous frame.
   */
  blocks?: Record<string, Record<string, number>>;
  /** The output's BS.1770 momentary (400 ms) and short-term (3 s) loudness, LUFS. */
  loudnessMomentary?: number;
  loudnessShortTerm?: number;
  /** Peak of Outputs 1-4 (after their EQ and Boost/Pad), as the slower host telemetry last reported them. */
  outputPeaks?: number[];
  /**
   * The current value of every parameter a modifier drives, in the parameter's units, by block id and then parameter
   * id (the latest, not a peak). Absent when no modifier is driving a parameter, or without shared-memory telemetry.
   */
  modulation?: Modulation;
  /** Where the rig's sequencer, LFOs and ADSRs stand (absent without shared-memory telemetry). */
  controllers?: ControllerInfo;
}

/**
 * The rig's controllers as of a telemetry frame (`Telemetry.controllers`): the sequencer's stage playing, from 0, of
 * its `sequencerStages`, and LFO 1 and 2 and ADSR 1 and 2 at their current value, 0 to 1. `running` is false while
 * the audio is stopped or no preset is loaded; the values then hold where they stopped.
 */
export interface ControllerInfo {
  running: boolean;
  sequencerStage: number;
  sequencerStages: number;
  lfo1: number;
  lfo2: number;
  adsr1: number;
  adsr2: number;
}

/** Live modulated values by block id and then parameter id (`Telemetry.modulation`). */
export type Modulation = Record<string, Record<string, number>>;

/** Pushed about 30 times a second while the scope is on: recent output samples, oldest first. */
export interface ScopeFrame {
  type: 'scope';
  sampleRate: number;
  samples: number[];
}

/**
 * Pushed about 15 times a second while the RTA is on: fractional-octave bands, 0 dB = a full-scale sine, of the output
 * or of the preset's RTA block.
 */
export interface RtaFrame {
  type: 'rta';
  frequencies: number[];
  levels: number[];
  source: 'output' | 'block';
}

/**
 * `connected` opens every socket; `resync` replaces the events a client fell too far behind to receive: either way the
 * client reads its state again.
 */
export type ServerMessage =
  { type: 'connected' } | { type: 'resync' } | Telemetry | ScopeFrame | RtaFrame | MidiActivity | RigEvent;
