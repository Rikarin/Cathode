import type { CanDeactivateFn } from '@angular/router';

/** A page that holds changes until they are saved, and asks before a navigation throws them away. */
export interface AsksBeforeLeaving {
  /** Whether the page may be left: true at once when nothing is unsaved, otherwise the user's answer. */
  canLeave(): boolean | Promise<boolean>;
}

/** Keeps the router on a page with unsaved changes until the user agrees to discard them. */
export const askBeforeLeaving: CanDeactivateFn<AsksBeforeLeaving> = page => page.canLeave();
