import { Injectable, Pipe, type PipeTransform, signal } from '@angular/core';
import { isMac, keyNames } from '@cathode/audio-ui';

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
  /**
   * The command this is a second binding of: the key runs that command's handler, and the shortcut list shows it on
   * that command's row ("⌘T or T").
   */
  aliasOf?: string;
  /**
   * On an alias: the binding to name in a browser, where the command's own key may never reach the page (see
   * {@link Shortcuts}). Such an alias is a bare key, which a browser always delivers.
   */
  browser?: boolean;
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

/** The scenes again on the bare digits: a browser may keep ⌘1–8 or Ctrl+1–8 for its own tabs. */
const sceneAliases: Shortcut[] = [1, 2, 3, 4, 5, 6, 7, 8].map(n => ({
  id: `scene${n}Alias`,
  group: 'Scenes',
  label: `Scene ${n}`,
  code: `Digit${n}`,
  aliasOf: `scene${n}`,
  browser: true
}));

const channels: Shortcut[] = ['A', 'B', 'C', 'D'].map(c => ({
  id: `channel${c}`,
  group: 'Channels',
  label: `Channel ${c} on the selected block`,
  key: c.toLowerCase()
}));

/** The fields of a bare-key alias a browser delivers for command `id`, whose own key the browser may keep. */
function browserAlias(id: string): Pick<Shortcut, 'aliasOf' | 'browser'> {
  return { aliasOf: id, browser: true };
}

/**
 * Every shortcut, in the order the help overlay lists them. They clash neither with each other (a test checks) nor
 * with the desktop app's menu, which is Electron's default (⌘C/⌘V/⌘X/⌘A edit, ⌘R reload, ⌘W, ⌘M, ⌘Q, ⌥⌘I).
 *
 * The editor also runs in a plain browser (a headless rig, an iPad), which keeps some keys for itself and never hands
 * them to the page: ⌘T/Ctrl+T (new tab) for certain, and, depending on the browser and the system, the keys that
 * switch its tabs (Ctrl+PgUp/PgDn, ⌘1–8 or Ctrl+1–8) and ⌘L/Ctrl+L (the address bar). Each command bound to such a
 * key has an alias on a bare key (`aliasOf`, `browser`), which no browser keeps: T, L, 1–8, and PgUp/PgDn (with ⇧ for
 * songs). The aliases work in the desktop app too; in a browser the tooltips name them ({@link hintedShortcut}).
 */
export const Shortcuts: readonly Shortcut[] = [
  { id: 'presetPicker', group: 'Presets', label: 'Open the preset picker (a name, or a bank slot number)', key: 'p' },
  { id: 'presetPrevious', group: 'Presets', label: 'Previous preset', key: 'PageUp', mod: true },
  { id: 'presetNext', group: 'Presets', label: 'Next preset', key: 'PageDown', mod: true },
  { id: 'songPrevious', group: 'Presets', label: 'Previous setlist song', key: 'PageUp', mod: true, shift: true },
  { id: 'songNext', group: 'Presets', label: 'Next setlist song', key: 'PageDown', mod: true, shift: true },
  {
    id: 'presetPreviousAlias',
    group: 'Presets',
    label: 'Previous preset',
    key: 'PageUp',
    ...browserAlias('presetPrevious')
  },
  { id: 'presetNextAlias', group: 'Presets', label: 'Next preset', key: 'PageDown', ...browserAlias('presetNext') },
  {
    id: 'songPreviousAlias',
    group: 'Presets',
    label: 'Previous setlist song',
    key: 'PageUp',
    shift: true,
    ...browserAlias('songPrevious')
  },
  {
    id: 'songNextAlias',
    group: 'Presets',
    label: 'Next setlist song',
    key: 'PageDown',
    shift: true,
    ...browserAlias('songNext')
  },
  { id: 'reload', group: 'Presets', label: 'Reload the preset', key: 'F5' },
  { id: 'save', group: 'Presets', label: 'Save', key: 's', mod: true },
  { id: 'saveAs', group: 'Presets', label: 'Save as…', key: 's', mod: true, shift: true },
  { id: 'level', group: 'Presets', label: 'Preset Leveling', key: 'l', mod: true },
  { id: 'levelAlias', group: 'Presets', label: 'Preset Leveling', key: 'l', ...browserAlias('level') },
  ...scenes,
  ...sceneAliases,
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
  { id: 'removeBackspace', group: 'Grid', label: 'Remove the block or cable', key: 'Backspace', aliasOf: 'remove' },
  { id: 'undo', group: 'Edit', label: 'Undo', key: 'z', mod: true },
  { id: 'redo', group: 'Edit', label: 'Redo', key: 'z', mod: true, shift: true },
  { id: 'redoAlias', group: 'Edit', label: 'Redo', key: 'y', mod: true, aliasOf: 'redo' },
  { id: 'tuner', group: 'Other', label: 'Tuner', key: 't', mod: true },
  // A browser keeps ⌘T/Ctrl+T for a new tab and never hands it to the page, so the tuner has a bare key as well.
  { id: 'tunerAlias', group: 'Other', label: 'Tuner', key: 't', ...browserAlias('tuner') },
  { id: 'mute', group: 'Other', label: 'Mute or unmute every output', key: 'm' },
  { id: 'clearClips', group: 'Other', label: 'Reset every clip light', key: 'k', mod: true },
  { id: 'settings', group: 'Other', label: 'Settings', key: ',', mod: true },
  { id: 'help', group: 'Other', label: 'Keyboard shortcuts', key: '?', shift: true }
];

/** How a shortcut's key itself reads, without its modifiers. */
function keyName(shortcut: Shortcut): string {
  const names: Record<string, string> = {
    PageUp: 'PgUp',
    PageDown: 'PgDn',
    Delete: 'Del',
    Backspace: '⌫',
    Space: 'Space'
  };
  const key = shortcut.code ? shortcut.code.replace(/^Digit/, '') : (shortcut.key ?? '');
  return names[key] ?? (key.length === 1 ? key.toUpperCase() : key);
}

/** How a shortcut reads, key by key, on macOS (⌘ ⇧ S) or elsewhere (Ctrl Shift S): the keycaps of the shortcut list. */
export function shortcutKeys(shortcut: Shortcut, mac = isMac()): string[] {
  const names = keyNames(mac);
  const keys: string[] = [];
  if (shortcut.mod) {
    keys.push(names.mod);
  }

  if (shortcut.shift && shortcut.key !== '?') {
    keys.push(names.shift);
  }

  if (shortcut.alt) {
    keys.push(names.alt);
  }

  keys.push(keyName(shortcut));
  return keys;
}

/**
 * A shortcut as one short hint for a tooltip or a menu item, written as the platform writes it: "⇧⌘S" on macOS (the
 * modifiers in Apple's order, no separator), "Ctrl+Shift+S" elsewhere. Every hint in a template comes from here (the
 * `cthShortcut` pipe), so none names a key the user's keyboard does not have.
 */
export function shortcutHint(id: string, mac = isMac()): string {
  const shortcut = Shortcuts.find(s => s.id === id);
  if (!shortcut) {
    throw new Error(`No shortcut "${id}"`);
  }

  if (!mac) {
    return shortcutKeys(shortcut, false).join('+');
  }

  const shift = shortcut.shift && shortcut.key !== '?';
  return `${shortcut.alt ? '⌥' : ''}${shift ? '⇧' : ''}${shortcut.mod ? '⌘' : ''}${keyName(shortcut)}`;
}

/**
 * The binding of command `id` to name in a tooltip or a menu: its own in the desktop app (`window.cathode`); in a
 * plain browser, which may keep that key for itself, the bare-key alias when the command has one.
 */
export function hintedShortcut(id: string, desktop = typeof window !== 'undefined' && !!window.cathode): string {
  return desktop ? id : (Shortcuts.find(s => s.aliasOf === id && s.browser)?.id ?? id);
}

/**
 * The commands of a group as the shortcut list shows them: a row per command, with the keys of each of its bindings
 * (its own first, then its aliases).
 */
export function shortcutRows(
  group: ShortcutGroup,
  mac = isMac()
): { id: string; label: string; bindings: string[][] }[] {
  return Shortcuts.filter(s => s.group === group && !s.aliasOf).map(s => ({
    id: s.id,
    label: shortcutLabel(s, mac),
    bindings: [s, ...Shortcuts.filter(alias => alias.aliasOf === s.id)].map(binding => shortcutKeys(binding, mac))
  }));
}

/** A shortcut's label for the shortcut list: a modifier it mentions (⌥) reads as the platform names it. */
export function shortcutLabel(shortcut: Shortcut, mac = isMac()): string {
  return shortcut.label.replaceAll('⌥', keyNames(mac).alt);
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

  const input = element.closest<HTMLInputElement>('input');
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

/** Controls that take typed characters themselves, to find an entry by its first letters. */
const TypeToFind = [
  'select',
  '[role=listbox]',
  '[role=option]',
  '[role=menu]',
  '[role=menuitem]',
  '[role=menuitemcheckbox]',
  '[role=menuitemradio]',
  '[role=combobox]',
  '[role=tree]',
  '[role=treeitem]'
].join(', ');

/** Whether an event comes from a focused control (or something inside one), which takes bare keys itself. */
export function isOnControl(target: EventTarget | null): boolean {
  const element = target as HTMLElement | null;
  return !!element?.closest && element.closest(Controls) !== null;
}

/**
 * Whether the focused control the event comes from would use this key itself: Space, Delete and Backspace on any
 * control, a character (a letter, digit or ?) only on a list or menu that finds its entries by typing. A button, switch,
 * checkbox or knob does nothing with a letter, so M still mutes after a click on the metronome.
 */
export function controlTakesKey(target: EventTarget | null, key: string): boolean {
  const element = target as HTMLElement | null;
  if (!element?.closest) {
    return false;
  }

  const character = key.length === 1 && key !== ' ';
  return element.closest(character ? TypeToFind : Controls) !== null;
}

/**
 * Whether a shortcut is a bare key (Space, a letter, Delete; with or without ⇧) that a focused control would lose:
 * those stay out of the way on buttons, switches, checkboxes, sliders and the like. ⌘ shortcuts and the function keys
 * work everywhere, and so does everything on the grid, which is not such a control.
 */
export function yieldsToControl(shortcut: Shortcut): boolean {
  return !shortcut.mod && !/^F\d+$/.test(shortcut.key ?? '');
}

/**
 * The shortcuts that still run while a modal dialog is open: those that open or close a dialog themselves, and the two
 * that never touch the preset (mute is the panic button). Every other shortcut acts on the rig behind the dialog, where
 * the user cannot see what it did, so it waits until the dialog is closed.
 */
const WithDialogOpen: ReadonlySet<string> = new Set(['tuner', 'settings', 'help', 'mute', 'clearClips']);

/** Whether a modal dialog (Settings, the Tuner, a confirmation…) is open: xUI marks its overlay pane `aria-modal`. */
export function isDialogOpen(target: EventTarget | null): boolean {
  const root = (target as Node | null)?.ownerDocument ?? (typeof document === 'undefined' ? null : document);
  return !!root?.querySelector('[aria-modal="true"]');
}

/**
 * Whether the page has text selected: an address or the access token in Settings, a log, an error message. ⌘C and ⌘X
 * then belong to the browser, which copies that text; consuming the key for the grid would cancel the copy.
 */
export function hasSelectedText(target: EventTarget | null): boolean {
  const root = (target as Node | null)?.ownerDocument ?? (typeof document === 'undefined' ? null : document);
  const selection = root?.getSelection();
  return !!selection && !selection.isCollapsed && selection.toString().length > 0;
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

/**
 * A command's keys by its id, for a tooltip or a menu item: `'save' | cthShortcut` is "⌘S", or "Ctrl+S" off macOS. In
 * a browser it is the key the browser delivers: `'tuner' | cthShortcut` is "T" there ({@link hintedShortcut}).
 */
@Pipe({ name: 'cthShortcut' })
export class ShortcutHint implements PipeTransform {
  transform(id: string): string {
    return shortcutHint(hintedShortcut(id));
  }
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

  /** Registers handlers by command id (an alias runs its command's handler); returns a function that removes them. */
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
    // An alias is another key for its command: the command's handler, and the command's rules.
    const command = shortcut?.aliasOf ?? shortcut?.id;
    if (shortcut && yieldsToControl(shortcut) && controlTakesKey(event.target, event.key)) {
      return false; // Space on a focused button presses it; it does not bypass the selected block
    }

    if ((command === 'copy' || command === 'cut') && hasSelectedText(event.target)) {
      return false; // the selected text is what the user means to copy, not the block
    }

    // Behind a modal dialog the rig is out of sight: only the shortcuts that are safe there run.
    const blocked = command && !WithDialogOpen.has(command) && isDialogOpen(event.target);
    const handler = command && !blocked && this.handlers.get(command);
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
