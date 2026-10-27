import { LooperActions, type SwitchAction } from '@cathode/api-client';

/** One entry of an action picker's list: what people read, and what is stored. */
export interface Choice<T = string | number | null> {
  label: string;
  value: T;
}

/** What an action picker offers targets and values from: the rig as it is now. */
export interface ActionContext {
  /** The loaded preset's blocks a switch can bypass or switch the channel of, with their names. */
  blocks: readonly { id: string; name: string }[];
  /** The banks, for presetSlot, with the preset names their slots hold. */
  banks: readonly { id: string; name: string; slots: readonly (string | null)[] }[];
  presetName: (id: string) => string;
  layouts: readonly { id: string; name: string }[];
  /** The view names of the layout the switch belongs to (or the active layout's), for view. */
  views: readonly string[];
}

const ChannelLetters = ['A', 'B', 'C', 'D'];
const SceneCount = 8;

/**
 * The targets an action picks from, by name, storing the same ids as before: the loaded preset's blocks for bypass and
 * channel, banks for presetSlot, looper commands, footswitch layouts. Null for an action without a target. A stored
 * target the lists do not hold (a block of another preset, a deleted bank) stays listed, so it is shown and kept.
 */
export function targetChoices(action: string, context: ActionContext, current: string | null): Choice<string>[] | null {
  let choices: Choice<string>[];
  switch (action) {
    case 'bypass':
    case 'channel':
      choices = context.blocks.map(b => ({ label: `${b.name} (${b.id})`, value: b.id }));
      break;
    case 'presetSlot':
      choices = context.banks.map(b => ({ label: b.name, value: b.id }));
      break;
    case 'looper':
      choices = LooperActions.map(a => ({ label: a, value: a }));
      break;
    case 'layout':
      choices = context.layouts.map(l => ({ label: l.name, value: l.id }));
      break;
    default:
      return null;
  }

  return current && !choices.some(c => c.value === current)
    ? [...choices, { label: `${current} (not found)`, value: current }]
    : choices;
}

/**
 * The values an action picks from, as people count them, storing what the engine reads: scenes 1-8 (0-7), channels and
 * Manuals A-D (0-3), Externals 1-8, the layout's views by name (from 0), a bank's slots with the preset in each (0-127).
 * Null for an action without a value. Scene and presetSlot may also have none: a CC's value or the program number picks.
 */
export function valueChoices(
  action: string,
  context: ActionContext,
  target: string | null,
  current: number | null
): Choice<number | null>[] | null {
  let choices: Choice<number | null>[];
  switch (action) {
    case 'scene':
      choices = [
        { label: '(from the message)', value: null },
        ...range(SceneCount).map(i => ({ label: `Scene ${i + 1}`, value: i }))
      ];
      break;
    case 'channel':
      choices = ChannelLetters.map((c, i) => ({ label: `Channel ${c}`, value: i }));
      break;
    case 'manual':
      choices = ChannelLetters.map((c, i) => ({ label: `Manual ${c}`, value: i }));
      break;
    case 'external':
      choices = range(8).map(i => ({ label: `External ${i + 1}`, value: i + 1 }));
      break;
    case 'view':
      choices = context.views.map((name, i) => ({ label: name, value: i }));
      break;
    case 'presetSlot': {
      const slots = context.banks.find(b => b.id === target)?.slots ?? [];
      choices = [
        { label: '(the program number)', value: null },
        ...range(128).map(i => {
          const id = slots[i];
          return { label: `Slot ${i + 1}${id ? `: ${context.presetName(id)}` : ''}`, value: i };
        })
      ];
      break;
    }
    default:
      return null;
  }

  return current !== null && !choices.some(c => c.value === current)
    ? [...choices, { label: String(current), value: current }]
    : choices;
}

/**
 * The action as it starts when picked: its first target and its first value. A footswitch sends one fixed value, so it
 * starts on Scene 1 or Slot 1; a MIDI mapping (`fromMessage`) starts with none, so the message's value picks.
 */
export function freshAction(action: string, context: ActionContext, fromMessage = false): SwitchAction {
  const target = targetChoices(action, context, null)?.[0]?.value ?? null;
  const values = valueChoices(action, context, target, null) ?? [];
  const value = (fromMessage ? values[0] : values.find(v => v.value !== null))?.value ?? null;
  return { action, target, value };
}

function range(n: number): number[] {
  return Array.from({ length: n }, (_, i) => i);
}
