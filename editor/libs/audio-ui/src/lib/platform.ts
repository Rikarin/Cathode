/** Whether this is an Apple device, whose keyboard has ⌘, ⌥ and ⇧ where others print Ctrl, Alt and Shift. */
export function isMac(): boolean {
  return typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent);
}

/** The modifier keys as the user's keyboard prints them. `mod` is the command modifier: ⌘ on a Mac, Ctrl elsewhere. */
export interface KeyNames {
  mod: string;
  alt: string;
  shift: string;
}

/** The modifier keys' names on this platform, for hints such as "⌥-click to reset" (elsewhere "Alt-click to reset"). */
export function keyNames(mac = isMac()): KeyNames {
  return mac ? { mod: '⌘', alt: '⌥', shift: '⇧' } : { mod: 'Ctrl', alt: 'Alt', shift: 'Shift' };
}
