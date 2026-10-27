import { AMP_FAMILIES, AMP_MODELS } from '../catalog/amp-models';
import { ENGINE } from '../catalog/engine.generated';
import { BLOCK_DOCS, CHAPTERS, GUIDE_TEXT } from './content.generated';
import { describeOption, describeParam } from './describe';
import type { EngineBlock, EngineParam } from './engine';
import { ENGINE_BLOCKS } from './engine-params.generated';
import type { BlockDoc, Chapter } from './types';

/*
 * The manual's data: the guide's chapters joined with their Markdown, and the engine's blocks joined with their hand
 * written pages. Everything here is computed once; the pages only read it. A chapter or block page that is not written
 * yet is simply missing here, and the pages say so instead of failing.
 */

// ------------------------------------------------------------------ chapters
export interface ManualChapter extends Chapter {
  /** The chapter's Markdown, or undefined while it is not written. */
  readonly text?: string;
}

/** A file holding nothing but comments (a placeholder) counts as not written. */
const written = (text: string | undefined) => (text && text.replace(/<!--[\s\S]*?-->/g, '').trim() ? text : undefined);

export const MANUAL_CHAPTERS: readonly ManualChapter[] = CHAPTERS.map(chapter => ({
  ...chapter,
  text: written(GUIDE_TEXT[chapter.slug])
}));

export const PARTS: readonly { readonly name: string; readonly chapters: readonly ManualChapter[] }[] = [
  ...new Set(MANUAL_CHAPTERS.map(chapter => chapter.part))
].map(name => ({ name, chapters: MANUAL_CHAPTERS.filter(chapter => chapter.part === name) }));

export const chapterBySlug = (slug: string) => MANUAL_CHAPTERS.find(chapter => chapter.slug === slug);

// ------------------------------------------------------------------ blocks
/** The order of the block reference's groups: the signal's way through a rig. A category added later lands last. */
const CATEGORY_ORDER = [
  'I/O',
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
  'Utility'
];

const rank = (category: string) => {
  const at = CATEGORY_ORDER.indexOf(category);
  return at < 0 ? CATEGORY_ORDER.length : at;
};

export interface ManualBlock extends EngineBlock {
  /** The block's page, or undefined while it is not written (a placeholder with no summary counts as not written). */
  readonly doc?: BlockDoc;
}

export const MANUAL_BLOCKS: readonly ManualBlock[] = ENGINE_BLOCKS.map(block => {
  const doc = BLOCK_DOCS[block.typeId] as BlockDoc | undefined;
  return { ...block, doc: doc?.summary?.trim() || Object.keys(doc?.params ?? {}).length ? doc : undefined };
}).sort((a, b) => rank(a.category) - rank(b.category));

export const CATEGORIES: readonly { readonly name: string; readonly blocks: readonly ManualBlock[] }[] = [
  ...new Set(MANUAL_BLOCKS.map(block => block.category))
].map(name => ({ name, blocks: MANUAL_BLOCKS.filter(block => block.category === name) }));

export const blockById = (typeId: string) => MANUAL_BLOCKS.find(block => block.typeId === typeId);

/** The neighbours of item `at` in a list, for the prev/next links at the foot of a page. */
export function neighbours<T>(list: readonly T[], at: number): { prev?: T; next?: T } {
  return at < 0 ? {} : { prev: list[at - 1], next: list[at + 1] };
}

// ------------------------------------------------------------------ values
/** A number as the editor would show it: four significant digits at most, a real minus sign. */
export function formatNumber(value: number, unit = ''): string {
  let shown = value;
  let unitShown = unit;

  if (unit === 'Hz' && Math.abs(value) >= 1000) {
    shown = value / 1000;
    unitShown = 'kHz';
  }

  const text = String(Number(shown.toPrecision(4))).replace('-', '−');

  if (!unitShown) {
    return text;
  }

  return /^(%|:1|°|x|×)$/.test(unitShown) ? `${text}${unitShown}` : `${text} ${unitShown}`;
}

/** A parameter's value as text: the option's name for a choice, On/Off for a switch. */
export function formatValue(param: EngineParam, value: number): string {
  if (param.kind === 'switch') {
    return value ? 'On' : 'Off';
  }

  if (param.kind === 'choice') {
    return param.options?.[Math.round(value)] ?? String(value);
  }

  return formatNumber(value, param.unit);
}

// ------------------------------------------------------------------ knobs
export interface Choice {
  readonly label: string;
  readonly description?: string;
  /** The equipment a model is based on (amp, drive and wah types), from the catalogue. */
  readonly basedOn?: string;
}

/** One row of a block's knob list: one knob, or a family of numbered knobs that share their text (Tap 1–10 Level). */
export interface Knob {
  /** The first member's id, the row's anchor (`#knob-<id>`). */
  readonly id: string;
  /** Every member's id; each gets an anchor of its own. */
  readonly ids: readonly string[];
  /** The name, with a family's numbers as a range: "Tap 1–10 Level". */
  readonly name: string;
  readonly param: EngineParam;
  /** For a family, the member's number for each id; empty for a single knob. */
  readonly numbers: readonly number[];
  readonly description?: string;
  /** "0 – 10 dB"; empty for a choice or a switch. */
  readonly range: string;
  /** The default, or for a family whose members differ, each member's ("1: 0 dB, 2: −3 dB"). */
  readonly defaultText: string;
  readonly choices: readonly Choice[];
  /** For a knob shown only with another knob at some settings (`visibleWhen`): "Controls is Compression". */
  readonly shownWhen?: string;
}

export interface KnobPage {
  /** The editor page's name; empty for a block shown on one page. */
  readonly name: string;
  readonly knobs: readonly Knob[];
}

/** The one number in an id, and where it is: `tap10Level` → 10. Ids with no number, or two, are not family members. */
function numberIn(id: string): { readonly n: number; readonly key: string } | undefined {
  const runs = [...id.matchAll(/\d+/g)];

  if (runs.length !== 1) {
    return undefined;
  }

  const run = runs[0];
  return { n: Number(run[0]), key: id.slice(0, run.index) + '#' + id.slice(run.index + run[0].length) };
}

/** `name` with the number `n` (standing alone, once) replaced by `with`, or undefined if it is not there once. */
function replaceNumber(name: string, n: number, by: string): string | undefined {
  const pattern = new RegExp(`(?<![\\d.])${n}(?![\\d.])`, 'g');
  return name.match(pattern)?.length === 1 ? name.replace(pattern, by) : undefined;
}

const BASED_ON: Record<string, (option: string) => string | undefined> = {
  circuitamp: option => AMP_MODELS[option]?.basedOn,
  drive: option => ENGINE.drives.find(drive => drive.name === option)?.modelledAfter,
  wah: option => ENGINE.wahs.find(wah => wah.name === option)?.modelledAfter
};

function choicesOf(block: ManualBlock, param: EngineParam): Choice[] {
  if (param.kind !== 'choice') {
    return [];
  }

  const basedOn = param.id === 'type' ? BASED_ON[block.typeId] : undefined;

  return (param.options ?? []).map(label => ({
    label,
    description: describeOption(block.doc, param.id, label),
    basedOn: basedOn?.(label)
  }));
}

function rangeOf(param: EngineParam): string {
  if (param.kind !== 'float') {
    return '';
  }

  // The unit is said once, after the top of the range, unless the two ends differ in it: "−80 – 20 dB", "20 Hz – 2 kHz".
  const bottom = formatNumber(param.min, param.unit);
  const top = formatNumber(param.max, param.unit);
  const unitOf = (text: string) => text.replace(/^[−\d.]+/, '');

  return unitOf(bottom) === unitOf(top)
    ? `${bottom.slice(0, bottom.length - unitOf(bottom).length)} – ${top}`
    : `${bottom} – ${top}`;
}

/** What a member's availability looks like across types ('' where hidden), so only alike knobs form a family. */
const availability = (param: EngineParam) => param.typeLabels?.map(label => (label ? '1' : '0')).join('') ?? '';

/** A block's knobs, page by page in the editor's order, with numbered families collapsed into one row. */
export function knobPages(block: ManualBlock): KnobPage[] {
  const pageNames = block.pages?.length ? block.pages : [''];

  return pageNames
    .map(page => {
      // A parameter hidden on every type (kept only so old presets load) has no knob.
      const params = block.params.filter(
        param => (param.page ?? '') === page && (!param.typeLabels || param.typeLabels.some(Boolean))
      );
      return { name: page, knobs: collapse(block, params) };
    })
    .filter(page => page.knobs.length > 0);
}

function collapse(block: ManualBlock, params: readonly EngineParam[]): Knob[] {
  // Group the numbered knobs that are alike in everything but their number.
  const families = new Map<string, { param: EngineParam; n: number }[]>();
  const familyOf = new Map<string, string>();

  for (const param of params) {
    const found = numberIn(param.id);
    const name = found && replaceNumber(param.name, found.n, '#');

    if (!found || !name) {
      continue;
    }

    const key = JSON.stringify([
      found.key,
      name,
      param.kind,
      param.unit,
      param.min,
      param.max,
      param.options,
      availability(param),
      param.visibleWhen ?? null,
      describeParam(block.doc, param.id) ?? ''
    ]);

    families.set(key, [...(families.get(key) ?? []), { param, n: found.n }]);
    familyOf.set(param.id, key);
  }

  const knobs: Knob[] = [];
  const done = new Set<string>();

  for (const param of params) {
    if (done.has(param.id)) {
      continue;
    }

    const key = familyOf.get(param.id);
    const members = key ? [...families.get(key)!].sort((a, b) => a.n - b.n) : [];
    const consecutive = members.length > 1 && members.every((m, i) => i === 0 || m.n === members[i - 1].n + 1);

    if (consecutive) {
      members.forEach(member => done.add(member.param.id));
      knobs.push(family(block, members));
    } else {
      done.add(param.id);
      knobs.push(single(block, param));
    }
  }

  return knobs;
}

function single(block: ManualBlock, param: EngineParam): Knob {
  return {
    id: param.id,
    ids: [param.id],
    name: param.name,
    param,
    numbers: [],
    description: describeParam(block.doc, param.id),
    range: rangeOf(param),
    defaultText: defaultTextOf(block, param),
    choices: choicesOf(block, param),
    shownWhen: shownWhenOf(block, param)
  };
}

function family(block: ManualBlock, members: readonly { param: EngineParam; n: number }[]): Knob {
  const first = members[0];
  const span = `${first.n}–${members[members.length - 1].n}`;
  const defaults = members.map(member => formatValue(member.param, member.param.default));
  const same = defaults.every(text => text === defaults[0]);

  return {
    id: first.param.id,
    ids: members.map(member => member.param.id),
    name: replaceNumber(first.param.name, first.n, span) ?? first.param.name,
    param: first.param,
    numbers: members.map(member => member.n),
    description: describeParam(block.doc, first.param.id),
    range: rangeOf(first.param),
    defaultText: same ? defaults[0] : members.map((member, i) => `${member.n}: ${defaults[i]}`).join(', '),
    choices: choicesOf(block, first.param),
    shownWhen: shownWhenOf(block, first.param)
  };
}

/** The setting of another knob the knob is shown with, as text ("Controls is Compression"), if it has one. */
function shownWhenOf(block: ManualBlock, param: EngineParam): string | undefined {
  const when = param.visibleWhen;
  const other = when && block.params.find(p => p.id === when.param);

  return when && other ? `${other.name} is ${when.values.map(v => formatValue(other, v)).join(' or ')}` : undefined;
}

/**
 * The knob's default as text; where some types ship it at another setting (`typeDefaults`), those types with theirs:
 * "5 (Quick Rod: 0)".
 */
function defaultTextOf(block: ManualBlock, param: EngineParam): string {
  const text = formatValue(param, param.default);
  const types = block.params.find(p => p.id === 'type')?.options ?? [];
  const others = new Map<string, string[]>();

  param.typeDefaults?.forEach((value, i) => {
    if (value !== param.default) {
      const other = formatValue(param, value);
      others.set(other, [...(others.get(other) ?? []), types[i] ?? String(i)]);
    }
  });

  return others.size ? `${text} (${[...others].map(([other, on]) => `${on.join(', ')}: ${other}`).join('; ')})` : text;
}

/** The knob's default on type `type` of its block, as text. */
export function defaultOn(knob: Knob, type: number): string {
  const value = knob.param.typeDefaults?.[type];
  return value === undefined || knob.numbers.length > 0 ? knob.defaultText : formatValue(knob.param, value);
}

/** The knob's name on type `type` of its block ('' where that type hides it), or its own name when it has no labels. */
export function labelOn(knob: Knob, type: number): string {
  const label = knob.param.typeLabels?.[type];

  if (label === undefined) {
    return knob.name;
  }

  if (!label || knob.numbers.length === 0) {
    return label;
  }

  const first = knob.numbers[0];
  return replaceNumber(label, first, `${first}–${knob.numbers[knob.numbers.length - 1]}`) ?? label;
}

/** For a knob whose name or presence depends on the block's type: the types grouped by the name they give it. */
export function namesByType(knob: Knob, types: readonly string[]): { name: string; types: string[] }[] {
  if (!knob.param.typeLabels) {
    return [];
  }

  const groups = new Map<string, string[]>();
  types.forEach((type, i) => {
    const name = labelOn(knob, i);
    groups.set(name, [...(groups.get(name) ?? []), type]);
  });

  return [...groups].map(([name, types]) => ({ name, types }));
}

// ------------------------------------------------------------------ amp families
/** The Amp block's models by family, for its model picker; a model not in the catalogue lands under Other. */
export function ampFamilies(types: readonly string[]): { name: string; models: { name: string; index: number }[] }[] {
  const models = types.map((name, index) => ({ name, index }));

  return [
    ...AMP_FAMILIES.map(family => ({
      name: family.name,
      models: models.filter(model => AMP_MODELS[model.name]?.family === family.id)
    })),
    { name: 'Other', models: models.filter(model => !AMP_MODELS[model.name]) }
  ].filter(group => group.models.length > 0);
}

/** Markdown reduced to plain text, for search and meta descriptions. */
export function plainText(markdown: string): string {
  return markdown
    .replace(/<!--[\s\S]*?-->/g, ' ')
    .replace(/```[\s\S]*?```/g, ' ')
    .replace(/!\[[^\]]*\]\([^)]*\)/g, ' ')
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/[`*_>#|]/g, '')
    .replace(/\s+/g, ' ')
    .trim();
}

/** The first sentence or so of a text, for a meta description (about 155 characters at most). */
export function snippet(text: string, max = 155): string {
  if (text.length <= max) {
    return text;
  }

  const cut = text.slice(0, max - 1);
  const sentence = cut.lastIndexOf('. ');
  return sentence > 60 ? cut.slice(0, sentence + 1) : `${cut.slice(0, cut.lastIndexOf(' '))}…`;
}
