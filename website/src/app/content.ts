/*
 * What the page says about Cathode. Numbers and names come from the README and the design docs (docs/07 for the amps,
 * docs/08 for the blocks); keep them in step when those change. No benchmarks, users or reviews: there are none on
 * hand, and the page does not invent them.
 */

import { AMP_COUNT, BLOCK_COUNT, DRIVE_COUNT } from './catalog/catalog';

export const FIGURES = [
  { value: String(AMP_COUNT), label: 'amp models from schematics' },
  { value: String(BLOCK_COUNT), label: 'block types' },
  { value: '6 × 14', label: 'cells on the grid' },
  { value: '8 + 4', label: 'scenes per preset, channels per block' }
];

/** The amp families, with a few of the amps in each that a model is based on. */
export const AMP_FAMILIES = [
  {
    name: 'American clean',
    description: 'Tweed, brownface and blackface: chime, sparkle and spring reverb, with the vibrato as it was built.',
    based: ['Deluxe Reverb AB763', 'Twin Reverb', 'Bassman 5F6-A', 'Princeton Reverb', 'Tweed Deluxe 5E3']
  },
  {
    name: 'British',
    description:
      'From the JTM45 and the Plexi to the JCM800 and Jubilee: the crunch that the rest of rock was built on.',
    based: ['Plexi 1959 Super Lead', 'JTM45', 'JCM800 2203', 'Silver Jubilee 2555', 'Hiwatt DR103']
  },
  {
    name: 'Class A and boutique',
    description: 'Cathode-biased power amps that compress as you dig in, and the hand-wired amps that followed them.',
    based: ['AC30 Top Boost', 'AC15', 'Matchless DC-30', 'Dumble Overdrive Special', 'Trainwreck Express']
  },
  {
    name: 'High gain',
    description: 'Cascaded preamps, tight low end and the saturated leads of the eighties to now.',
    based: ['Soldano SLO-100', 'Mark IIC+', '5150', 'Dual Rectifier', 'Friedman BE-100']
  },
  {
    name: 'Bass and solid state',
    description: 'The bass rigs and transistor amps, for the parts of a song that are not a tube guitar amp.',
    based: ['Ampeg SVT', 'B-15-N Portaflex', 'Roland JC-120', 'Valvestate 8100']
  }
];

/** What sits behind an amp's front panel. */
export const AMP_EXPERT = [
  'Tube types, bias and bias excursion',
  'Supply sag and screen supply',
  'Output transformer and speaker load',
  'Boost, saturation and input dynamics',
  'Bias tremolo and output compression'
];

export const CAB_FIGURES = [
  { value: '10', label: 'speakers' },
  { value: '11', label: 'microphones' },
  { value: '7', label: 'cabinets' }
];

export const CAB_FEATURES = [
  {
    title: 'Place the microphone',
    body: 'Position across the cone, distance up to 3.4 m with auto-align, and angle: the response is built from the three.'
  },
  {
    title: 'Bring your own IRs',
    body: 'Load impulse responses as .wav or .cabgrid, mix two of them, trim and smooth them, export what you made.'
  },
  {
    title: 'Capture your cab',
    body: 'Play a sine sweep through your own speaker and microphone and keep the impulse response it measures.'
  }
];

/** Every block type, grouped the way the block picker groups them. */
export const BLOCK_GROUPS = [
  {
    name: 'Drive',
    blocks: ['Drive', 'Dynamic distortion', 'Enhancer'],
    note: `${DRIVE_COUNT} overdrive, distortion, fuzz and boost types, most of them pedal circuits`
  },
  {
    name: 'Delay',
    blocks: ['Delay', 'Multitap delay', 'Ten-tap delay', 'Plex delay', 'Megatap'],
    note: 'Digital, analogue, tape, ping-pong, reverse and sweep; up to 16 seconds'
  },
  {
    name: 'Reverb',
    blocks: ['Reverb'],
    note: 'Room, hall, plate, chamber, cathedral and spring, with one to six springs'
  },
  {
    name: 'Modulation',
    blocks: ['Chorus', 'Flanger', 'Phaser', 'Vibe', 'Rotary', 'Tremolo and panner', 'Ring modulator'],
    note: 'Thru-zero flanging, barberpole phasing, a rotary with brake'
  },
  {
    name: 'Pitch and synth',
    blocks: ['Pitch', 'Synth', 'Vocoder', 'Resonator', 'Formant'],
    note: 'Whammy, harmonies in key, detune, a 16-step arpeggiator'
  },
  {
    name: 'Dynamics',
    blocks: ['Compressor', 'Multiband compressor', 'Gate and expander'],
    note: 'Optical, JFET, tube and studio compressors, sidechains'
  },
  {
    name: 'EQ and filter',
    blocks: ['Parametric EQ', 'Graphic EQ', 'Filter', 'Wah', 'Crossover', 'Tone match'],
    note: 'Ten graphic layouts, ten wah voicings, matching a reference tone'
  },
  {
    name: 'Routing and tools',
    blocks: ['Looper', 'Tuner', 'Mixer', 'Multiplexer', 'Volume and pan', 'Send and return', 'FX loop', 'IR player'],
    note: 'Plus an analyser, and an FX loop with its latency compensated'
  }
];

/** The preset shown on the routing diagram: a cell's column and row, its label, and what it connects to. */
export const GRID_BLOCKS = [
  { id: 'in', label: 'IN', col: 0, row: 2 },
  { id: 'cmp', label: 'CMP', col: 1, row: 2 },
  { id: 'drv', label: 'DRV', col: 2, row: 2 },
  { id: 'amp', label: 'AMP', col: 3, row: 2 },
  { id: 'cab', label: 'CAB', col: 4, row: 2 },
  { id: 'peq', label: 'PEQ', col: 5, row: 2 },
  { id: 'cho', label: 'CHO', col: 7, row: 1 },
  { id: 'dly', label: 'DLY', col: 8, row: 1 },
  { id: 'pit', label: 'PIT', col: 7, row: 3 },
  { id: 'rev', label: 'REV', col: 8, row: 3 },
  { id: 'mix', label: 'MIX', col: 10, row: 2 },
  { id: 'vol', label: 'VOL', col: 11, row: 2 },
  { id: 'out', label: 'OUT', col: 13, row: 2 }
] as const;

export const GRID_WIRES: readonly (readonly [string, string])[] = [
  ['in', 'cmp'],
  ['cmp', 'drv'],
  ['drv', 'amp'],
  ['amp', 'cab'],
  ['cab', 'peq'],
  ['peq', 'cho'],
  ['cho', 'dly'],
  ['peq', 'pit'],
  ['pit', 'rev'],
  ['peq', 'mix'],
  ['dly', 'mix'],
  ['rev', 'mix'],
  ['mix', 'vol'],
  ['vol', 'out']
];

export const GRID_FEATURES = [
  {
    title: 'Parallel paths',
    body: 'Split anywhere and run a dry path beside a wet one, or two amps side by side into one cab.'
  },
  {
    title: 'Mixers and multiplexers',
    body: 'Merge rows with their own level and balance, or pick one row of several with a switch.'
  },
  {
    title: 'Feedback loops',
    body: 'Two send and return pairs carry a signal back up the grid, for delays that feed a reverb that feeds the delay.'
  }
];

/** The scene demo's blocks, engaged or bypassed and on which channel, per scene. */
export const SCENE_BLOCKS = ['DRV', 'AMP', 'DLY', 'REV'] as const;

export const SCENES: readonly { name: string; blocks: readonly (string | null)[] }[] = [
  { name: 'Clean', blocks: [null, 'A', null, 'A'] },
  { name: 'Chime', blocks: [null, 'A', 'A', 'B'] },
  { name: 'Crunch', blocks: [null, 'B', null, 'A'] },
  { name: 'Rhythm', blocks: ['A', 'C', null, null] },
  { name: 'Lead', blocks: ['B', 'C', 'A', 'A'] },
  { name: 'Solo', blocks: ['B', 'D', 'B', 'A'] },
  { name: 'Ambient', blocks: [null, 'A', 'C', 'C'] },
  { name: 'Outro', blocks: ['A', 'D', 'C', 'C'] }
];

export const MODIFIER_SOURCES = ['LFO', 'Envelope', 'Sequencer', 'MIDI controller'];

export const LIVE_FEATURES = [
  {
    title: 'Perform page',
    body: 'Big scene buttons and the handful of controls you picked for the song, readable from where you stand.'
  },
  {
    title: 'MIDI and footswitches',
    body: 'Map program changes and CCs, lay out your footswitches, and send MIDI to other gear on every scene change.'
  },
  {
    title: 'Metronome and tap tempo',
    body: 'Delays and modulation follow the tempo; tap it in, or let the preset bring its own.'
  }
];

export const LIBRARY_ITEMS = [
  'Presets, in banks and setlists',
  'Snapshots and templates',
  'Block presets, to reuse a drive or a delay',
  'NAM amps and IR cabs, in the same place',
  'Preset info: a name, an author and tags to find it by'
];

export const IO_FEATURES = [
  { value: '6', title: 'Inputs', body: 'Guitar, a second instrument, a microphone for the vocoder, USB channels.' },
  {
    value: '4',
    title: 'Stereo outputs',
    body: 'Each with its own EQ: one to the front of house, one to your monitor.'
  },
  {
    value: '1',
    title: 'FX loop',
    body: 'Put a pedal or a rack unit in the grid, with its round-trip latency taken out.'
  },
  {
    value: 'DI',
    title: 'Out for reamping',
    body: 'Record the dry guitar beside the processed one and dial it in later.'
  }
];

export const PLATFORMS = [
  {
    name: 'macOS',
    package: 'Cathode-<version>-mac-arm64.dmg',
    for: 'Apple Silicon',
    audio: 'CoreAudio and CoreMIDI',
    status: 'ready' as const,
    note: 'Not notarised yet: the first time, right-click the app and choose Open.'
  },
  {
    name: 'Linux',
    package: 'Cathode-<version>-linux-x86_64.AppImage',
    for: 'Linux desktops',
    audio: 'ALSA, JACK or PipeWire, and ALSA MIDI',
    status: 'early' as const,
    note: 'Make it executable (chmod +x) and run it. Audio is new: tested in containers, not yet on a real interface.'
  },
  {
    name: 'Windows',
    package: 'Cathode-<version>-win-x64.exe',
    for: 'Windows 10 and 11',
    audio: 'WASAPI and WinMM MIDI; ASIO planned',
    status: 'early' as const,
    note: 'Not signed yet: SmartScreen asks you to confirm. Audio is new: tested on shared-mode output so far.'
  }
];

export const GETTING_STARTED = [
  'Connect your audio interface and open Cathode.',
  'Open Settings, then Audio setup, and choose your interface. 64 samples at 48 kHz is the target; a loopback test measures your real latency.',
  'Double-click an empty cell on the grid to add a block. An Amp and a Cab between Input and Output make a rig.',
  'Select a block to edit it in the drawer below the grid. Right-click a knob to add a modifier or put it on the Perform page.',
  'Save with ⌘S. Press ? to see every keyboard shortcut.'
];

export const HEADLESS_INSTALL = `tar xzf cathode-server-<version>-linux-arm64.tar.gz
cd cathode-server-<version>-linux-arm64
sudo ./install.sh`;
