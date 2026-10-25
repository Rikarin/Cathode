import { AMP_FAMILIES, AMP_MODELS } from './amp-models';
import { ENGINE } from './engine.generated';

/*
 * The catalogue page's data: the engine's own lists (engine.generated.ts) joined with what each model is based on.
 * Everything here is computed once at build time; the page only filters it.
 */

export interface Entry {
  readonly name: string;
  /** The equipment the model is based on, or Cathode's own design. */
  readonly basedOn: string;
  /** How the circuit is built, where the engine says. */
  readonly detail?: string;
}

export interface Group {
  readonly id: string;
  readonly name: string;
  readonly entries: readonly Entry[];
}

const typesOf = (typeId: string): readonly string[] =>
  ENGINE.blocks.find(block => block.typeId === typeId)?.types ?? [];

// ------------------------------------------------------------------ amps
const ampTypes = typesOf('circuitamp');

export const AMP_COUNT = ampTypes.length;

export const AMP_GROUPS: readonly Group[] = [
  ...AMP_FAMILIES.map(family => ({
    id: family.id,
    name: family.name,
    entries: ampTypes
      .filter(name => AMP_MODELS[name]?.family === family.id)
      .map(name => ({ name, basedOn: AMP_MODELS[name].basedOn }))
  })),
  {
    id: 'other',
    name: 'Other',
    entries: ampTypes.filter(name => !AMP_MODELS[name]).map(name => ({ name, basedOn: '' }))
  }
].filter(group => group.entries.length > 0);

// ------------------------------------------------------------------ drives
/** The Drive block's first three types, which came before the circuit catalogue and model no particular pedal. */
const OWN_DRIVES = ['Overdrive', 'Distortion', 'Fuzz'];

/** Drives that are not overdrives, by where the block picker's reader would look for them. */
const DRIVE_GROUP: Record<string, 'distortion' | 'fuzz' | 'boost' | 'lofi'> = {
  Distortion: 'distortion',
  'Governor Dist': 'distortion',
  'Rodent Dist': 'distortion',
  'Rodent LED': 'distortion',
  'DS1 Distortion': 'distortion',
  'Plus Distortion': 'distortion',
  'Shred Distortion': 'distortion',
  'M-Zone Distortion': 'distortion',
  'Fat Rat': 'distortion',
  'Suhr Riot Ge': 'distortion',
  'Suhr Riot LED': 'distortion',
  'Suhr Riot LED/Si': 'distortion',
  'MCMLXXXI Drive': 'distortion',
  "Box O' Crunch": 'distortion',
  'Blackglass 7K': 'distortion',
  Ruckus: 'distortion',
  Fuzz: 'fuzz',
  'Pi Fuzz': 'fuzz',
  'Face Fuzz': 'fuzz',
  'Si Face Fuzz': 'fuzz',
  'NKT Face Fuzz': 'fuzz',
  'Bender Fuzz': 'fuzz',
  'Octave Fuzz': 'fuzz',
  'Master Fuzz': 'fuzz',
  'Super Fuzz': 'fuzz',
  'Hard Fuzz': 'fuzz',
  'Treble Booster': 'boost',
  'FET Boost': 'boost',
  'Esoteric Boost': 'boost',
  'Esoteric ACB': 'boost',
  'Esoteric Bass RCB': 'boost',
  'Shimmer Drive': 'boost',
  'Micro Boost': 'boost',
  'Bosom Boost': 'boost',
  'FAS Boost': 'boost',
  'SDD Preamp': 'boost',
  'FET Preamp': 'boost',
  'Integral Pre': 'boost',
  'Tape Distortion': 'lofi',
  'Bit Crusher': 'lofi'
};

const DRIVE_GROUPS = [
  { id: 'overdrive', name: 'Overdrive' },
  { id: 'distortion', name: 'Distortion' },
  { id: 'fuzz', name: 'Fuzz' },
  { id: 'boost', name: 'Boosts and preamps' },
  { id: 'lofi', name: 'Tape and lo-fi' }
];

/** "Ibanez TS808: JRC4558 with …" → the pedal, then how it is built. */
function split(modelledAfter: string): Pick<Entry, 'basedOn' | 'detail'> {
  const at = modelledAfter.indexOf(': ');

  return at < 0
    ? { basedOn: modelledAfter }
    : { basedOn: modelledAfter.slice(0, at), detail: capitalise(modelledAfter.slice(at + 2)) };
}

const capitalise = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);

const drives: Entry[] = typesOf('drive').map(name => {
  const circuit = ENGINE.drives.find(drive => drive.name === name);

  return circuit
    ? { name, ...split(circuit.modelledAfter) }
    : { name, basedOn: OWN_DRIVES.includes(name) ? "Cathode's own voicing" : '' };
});

export const DRIVE_COUNT = drives.length;

export const DRIVE_GROUP_LIST: readonly Group[] = DRIVE_GROUPS.map(group => ({
  ...group,
  entries: drives.filter(drive => (DRIVE_GROUP[drive.name] ?? 'overdrive') === group.id)
})).filter(group => group.entries.length > 0);

// ------------------------------------------------------------------ wahs
export const WAHS: readonly Entry[] = typesOf('wah').map(name => {
  const circuit = ENGINE.wahs.find(wah => wah.name === name);

  return circuit
    ? { name, basedOn: circuit.modelledAfter }
    : { name, basedOn: "Cathode's own filter voicing of the sweep" };
});

// ------------------------------------------------------------------ cab
export const CAB = ENGINE.cab;

// ------------------------------------------------------------------ blocks
/** The order of the block picker's groups; a category the engine adds later lands at the end. */
const CATEGORY_ORDER = [
  'Amp',
  'Cab',
  'Drive',
  'Dynamics',
  'EQ',
  'Filter',
  'Modulation',
  'Pitch',
  'Delay',
  'Reverb',
  'Synth',
  'Routing',
  'Utility',
  'I/O'
];

export interface BlockEntry {
  readonly name: string;
  readonly types: readonly string[];
}

/** Long Type lists that already have sections of their own: the blocks list points there instead of repeating them. */
const LISTED_ELSEWHERE: Record<string, string> = {
  circuitamp: 'amps',
  drive: 'drives',
  wah: 'wahs'
};

const blocks = ENGINE.blocks
  // The four outputs are one block type in all but number.
  .filter(block => !/^output\d$/.test(block.typeId))
  .map(block => ({
    category: block.category,
    name: block.typeId === 'output' ? 'Output (1 to 4)' : block.name,
    types: LISTED_ELSEWHERE[block.typeId] ? [] : block.types,
    see: LISTED_ELSEWHERE[block.typeId]
  }));

export const BLOCK_COUNT = blocks.length;

export const BLOCK_GROUPS = [...new Set(blocks.map(block => block.category))]
  .sort((a, b) => rank(a) - rank(b))
  .map(category => ({ category, blocks: blocks.filter(block => block.category === category) }));

function rank(category: string): number {
  const at = CATEGORY_ORDER.indexOf(category);

  return at < 0 ? CATEGORY_ORDER.length : at;
}
