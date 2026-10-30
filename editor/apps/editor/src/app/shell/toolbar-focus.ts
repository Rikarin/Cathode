/** A button in one of the window's top bars (the navigation bar and the preset bar). */
const ToolbarButton = '.chrome-bar button';

/**
 * Keeps a click on a top-bar button from focusing it, as a macOS toolbar's buttons do not take focus. A focused button
 * shows its focus ring at the next key press and leaves the bare-key shortcuts to it, so after a click on the metronome,
 * M would ring the metronome rather than mute. The button still acts on the click, and Tab still reaches it for the
 * keyboard. Returns the function that stops it.
 */
export function keepToolbarClicksUnfocused(document: Document): () => void {
  const keep = (event: MouseEvent): void => {
    const target = event.target;
    if (event.button === 0 && target instanceof Element && target.closest(ToolbarButton)) {
      event.preventDefault(); // the mousedown's default is to focus the button; the click still follows
    }
  };

  document.addEventListener('mousedown', keep, true);
  return () => document.removeEventListener('mousedown', keep, true);
}
