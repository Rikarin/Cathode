import { Injectable, signal } from '@angular/core';

/**
 * A keyboard shortcut. `key` is `KeyboardEvent.key` (case-insensitive for letters) or, for keys that change with ⇧
 * and layouts, a `code` such as `Digit1` or `Space`. `mod` is ⌘ on macOS and Ctrl elsewhere.
 */
export interface Shortcut {
  id: string;
  group: ShortcutGroup;
  /** What it does, for the help overlay. */
  label: string;
  key?: string;
  code?: string;
  mod?: boolean;
  shift?: boolean;
  alt?: boolean;
}

export type ShortcutGroup = 'Presets' | 'Scenes' | 'Channels' | 'Blocks' | 'Grid' | 'Edit' | 'Other';

export const ShortcutGroups: readonly ShortcutGroup[] = [
  'Presets',
  'Scenes',
  'Channels',
  'Blocks',
  'Grid',
  'Edit',
  'Other'
];

const scenes: Shortcut[] = [1, 2, 3, 4, 5, 6, 7, 8].map(n => ({
  id: `scene${n}`,
  group: 'Scenes',
  label: `Scene ${n}`,
  code: `Digit${n}`,
  mod: true
}));

const channels: Shortcut[] = ['A', 'B', 'C', 'D'].map(c => ({
  id: `channel${c}`,
  group: 'Channels',
  label: `Channel ${c} on the selected block`,
  key: c.toLowerCase()
}));

/**
 * Every shortcut, in the order the help overlay lists them. They clash neither with each other (a test checks) nor
 * with the desktop app's menu, which is Electron's default (⌘C/⌘V/⌘X/⌘A edit, ⌘R reload, ⌘W, ⌘M, ⌘Q, ⌥⌘I).
 */
export const Shortcuts: readonly Shortcut[] = [
  { id: 'presetPicker', group: 'Presets', label: 'Open the preset picker (a name, or a bank slot number)', key: 'p' },
  { id: 'presetPrevious', group: 'Presets', label: 'Previous preset', key: 'PageUp', mod: true },
  { id: 'presetNext', group: 'Presets', label: 'Next preset', key: 'PageDown', mod: true },
  { id: 'reload', group: 'Presets', label: 'Reload the preset', key: 'F5' },
  { id: 'save', group: 'Presets', label: 'Save', key: 's', mod: true },
  { id: 'saveAs', group: 'Presets', label: 'Save as…', key: 's', mod: true, shift: true },
  { id: 'level', group: 'Presets', label: 'Preset Leveling', key: 'l', mod: true },
  ...scenes,
  ...channels,
  { id: 'channelNext', group: 'Channels', label: 'Next channel on the selected block', key: 'x' },
  { id: 'channelReset', group: 'Channels', label: 'Reset the channel', key: 'i', mod: true },
  { id: 'bypass', group: 'Blocks', label: 'Bypass or engage the selected block', code: 'Space' },
  { id: 'engageAll', group: 'Blocks', label: 'Engage in all scenes', code: 'Space', shift: true },
  { id: 'bypassAll', group: 'Blocks', label: 'Bypass in all scenes', code: 'Space', shift: true, mod: true },
  { id: 'global', group: 'Blocks', label: 'Make the block global', key: 'g' },
  { id: 'saveBlock', group: 'Blocks', label: 'Save the block to the Library', key: 'b', mod: true },
  { id: 'blockInfo', group: 'Blocks', label: 'Block Info (or hold ⌥)', key: 'F1' },
  { id: 'addBlock', group: 'Grid', label: 'Add a block (or replace the selected one)', key: 'q' },
  { id: 'cut', group: 'Grid', label: 'Cut the block', key: 'x', mod: true },
  { id: 'copy', group: 'Grid', label: 'Copy the block', key: 'c', mod: true },
  { id: 'paste', group: 'Grid', label: 'Paste (onto a same-type block: its settings)', key: 'v', mod: true },
  { id: 'disconnect', group: 'Grid', label: 'Disconnect all cables', key: 'd', mod: true },
  { id: 'shunt', group: 'Grid', label: 'Insert a shunt', key: 'u', mod: true },
  { id: 'cable', group: 'Grid', label: 'Cable from the block (then the arrows and ↵; Esc cancels)', key: 'k' },
  { id: 'selectCable', group: 'Grid', label: 'Select the next cable of the block', key: 'k', shift: true },
  { id: 'remove', group: 'Grid', label: 'Remove the block or cable', key: 'Delete' },
  { id: 'removeBackspace', group: 'Grid', label: 'Remove (alias)', key: 'Backspace' },
  { id: 'undo', group: 'Edit', label: 'Undo', key: 'z', mod: true },
  { id: 'redo', group: 'Edit', label: 'Redo', key: 'z', mod: true, shift: true },
  { id: 'redoAlias', group: 'Edit', label: 'Redo (alias)', key: 'y', mod: true },
  { id: 'tuner', group: 'Other', label: 'Tuner', key: 't', mod: true },
  { id: 'mute', group: 'Other', label: 'Mute or unmute every output', key: 'm' },
  { id: 'settings', group: 'Other', label: 'Settings', key: ',', mod: true },
  { id: 'help', group: 'Other', label: 'Keyboard shortcuts', key: '?', shift: true }
];

/** How a shortcut reads, on macOS or elsewhere. */
export function shortcutKeys(shortcut: Shortcut, mac = isMac()): string[] {
  const keys: string[] = [];
  if (shortcut.mod) {
    keys.push(mac ? '⌘' : 'Ctrl');
  }

  if (shortcut.shift && shortcut.key !== '?') {
    keys.push('⇧');
  }

  if (shortcut.alt) {
    keys.push(mac ? '⌥' : 'Alt');
  }

  const names: Record<string, string> = {
    PageUp: 'PgUp',
    PageDown: 'PgDn',
    Delete: 'Del',
    Backspace: '⌫',
    Space: 'Space'
  };
  const key = shortcut.code ? (shortcut.code.replace(/^Digit/, '') as string) : (shortcut.key ?? '');
  keys.push(names[key] ?? (key.length === 1 ? key.toUpperCase() : key));
  return keys;
}

/** Whether an event comes from somewhere the user types: shortcuts stay out of the way there. */
export function isTyping(target: EventTarget | null): boolean {
  const element = target as HTMLElement | null;
  if (!element?.closest) {
    return false;
  }

  if (element.closest('textarea, [contenteditable=""], [contenteditable="true"], select')) {
    return true;
  }

  const input = element.closest('input') as HTMLInputElement | null;
  return input !== null && !['checkbox', 'radio', 'button', 'range', 'submit'].includes(input.type);
}

/** Controls that act on keys themselves: Space presses a button, toggles a switch or a checkbox, picks a radio. */
const Controls = [
  'button',
  'a[href]',
  'summary',
  'input',
  'select',
  '[role=button]',
  '[role=switch]',
  '[role=checkbox]',
  '[role=radio]',
  '[role=slider]',
  '[role=spinbutton]',
  '[role=option]',
  '[role=menuitem]',
  '[role=menuitemcheckbox]',
  '[role=menuitemradio]',
  '[role=tab]',
  '[role=combobox]',
  '[role=listbox]',
  '[role=treeitem]'
].join(', ');

/** Whether an event comes from a focused control (or something inside one), which takes bare keys itself. */
export function isOnControl(target: EventTarget | null): boolean {
  const element = target as HTMLElement | null;
  return !!element?.closest && element.closest(Controls) !== null;
}

/**
 * Whether a shortcut is a bare key (Space, a letter, Delete; with or without ⇧) that a focused control would lose:
 * those stay out of the way on buttons, switches, checkboxes, sliders and the like. ⌘ shortcuts and the function keys
 * work everywhere, and so does everything on the grid, which is not such a control.
 */
export function yieldsToControl(shortcut: Shortcut): boolean {
  return !shortcut.mod && !/^F\d+$/.test(shortcut.key ?? '');
}

/** The shortcut an event presses, if any: modifiers must match exactly (⌥ is ignored, it shows Block Info). */
export function findShortcut(
  event: Pick<KeyboardEvent, 'key' | 'code' | 'metaKey' | 'ctrlKey' | 'shiftKey' | 'altKey'>,
  shortcuts: readonly Shortcut[] = Shortcuts
): Shortcut | null {
  const mod = event.metaKey || event.ctrlKey;
  return (
    shortcuts.find(s => {
      const keyMatches = s.code ? s.code === event.code : s.key?.toLowerCase() === event.key.toLowerCase();
      // `?` needs ⇧ on most layouts; it is matched by the character typed.
      const shiftMatches = s.key === '?' || !!s.shift === event.shiftKey;
      return keyMatches && !!s.mod === mod && shiftMatches && (!s.alt || event.altKey);
    }) ?? null
  );
}

function isMac(): boolean {
  return typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent);
}

/**
 * Dispatches shortcuts to whoever handles them now: the app handles preset and edit commands, the grid the block
 * commands (it knows the focused cell), so a shortcut does nothing on a page that has no handler for it.
 */
@Injectable({ providedIn: 'root' })
export class ShortcutService {
  private readonly handlers = new Map<string, () => void>();

  /** Whether Block Info is showing: toggled with F1, or while ⌥ is held. */
  readonly blockInfo = signal(false);
  readonly altHeld = signal(false);

  /** Registers handlers by shortcut id; returns a function that removes them again. */
  register(handlers: Record<string, () => void>): () => void {
    for (const [id, handler] of Object.entries(handlers)) {
      this.handlers.set(id, handler);
    }

    return () => {
      for (const [id, handler] of Object.entries(handlers)) {
        if (this.handlers.get(id) === handler) {
          this.handlers.delete(id);
        }
      }
    };
  }

  /** Handles a keydown; returns whether a shortcut ran (and the event was consumed). */
  dispatch(event: KeyboardEvent): boolean {
    if (event.key === 'Alt') {
      this.altHeld.set(true);
      return false;
    }

    if (event.defaultPrevented || isTyping(event.target)) {
      return false;
    }

    const shortcut = findShortcut(event);
    if (shortcut && yieldsToControl(shortcut) && isOnControl(event.target)) {
      return false; // Space on a focused button presses it; it does not bypass the selected block
    }

    const handler = shortcut && this.handlers.get(shortcut.id);
    if (!handler) {
      if (event.metaKey && event.key.toLowerCase() === 'a') {
        event.preventDefault(); // an editor, not a document: select-all would only paint every label blue
      }

      return false;
    }

    event.preventDefault();
    handler();
    return true;
  }

  keyUp(event: KeyboardEvent): void {
    if (event.key === 'Alt') {
      this.altHeld.set(false);
    }
  }
}
