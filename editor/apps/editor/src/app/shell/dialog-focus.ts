/** A dialog's header Close button (xUI's dialog draws it in the header of its overlay pane). */
const CloseButton = ".cdk-overlay-pane[role='dialog'] header button[aria-label='Close']";

/**
 * Keeps a dialog that opens from putting focus on its Close button. The focus trap focuses the first focusable control
 * as a dialog opens, which is the header's Close, and the browser shows its focus ring whenever the dialog was opened
 * from the keyboard (⌘, or P). Focus arriving there from outside the dialog can only be that, so it goes to the
 * dialog itself instead: Tab then reaches the controls, Escape still closes, and tabbing round to Close shows its ring
 * as it should. Returns the function that stops it.
 */
export function keepDialogFocusOffClose(document: Document): () => void {
  const redirect = (event: FocusEvent): void => {
    const target = event.target;
    if (!(target instanceof HTMLElement) || !target.matches(CloseButton)) {
      return;
    }

    const pane = target.closest<HTMLElement>('.cdk-overlay-pane');
    const from = event.relatedTarget;
    if (!pane || (from instanceof Node && pane.contains(from))) {
      return; // moved within the dialog: tabbing to Close is meant
    }

    pane.tabIndex = -1;
    pane.focus({ preventScroll: true });
  };

  document.addEventListener('focusin', redirect);
  return () => document.removeEventListener('focusin', redirect);
}
