// Finding a knob's text in a BlockDoc. Plain functions with no Angular in them, so tools/manual-check.mjs uses the same
// rules as the pages.

import type { BlockDoc } from './types';

const patterns = new Map<string, RegExp>();

/** A BlockDoc key as a test for parameter ids: `#` stands for one or more digits. */
export function patternOf(key: string): RegExp {
  let pattern = patterns.get(key);

  if (!pattern) {
    const literal = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    pattern = new RegExp(`^${key.split('#').map(literal).join('\\d+')}$`);
    patterns.set(key, pattern);
  }

  return pattern;
}

/** The entry for `id`: its own key if there is one, otherwise the first `#` pattern that covers it. */
export function lookup<T>(map: Readonly<Record<string, T>> | undefined, id: string): T | undefined {
  if (!map) {
    return undefined;
  }

  if (Object.hasOwn(map, id)) {
    return map[id];
  }

  const key = Object.keys(map).find(key => key.includes('#') && patternOf(key).test(id));
  return key === undefined ? undefined : map[key];
}

/** What a knob does, or undefined where the doc does not say yet. */
export function describeParam(doc: BlockDoc | undefined, id: string): string | undefined {
  const text = lookup(doc?.params, id)?.trim();
  return text ? text : undefined;
}

/** What one choice of a choice knob does, or undefined. */
export function describeOption(doc: BlockDoc | undefined, id: string, option: string): string | undefined {
  const text = lookup(doc?.options, id)?.[option]?.trim();
  return text ? text : undefined;
}
