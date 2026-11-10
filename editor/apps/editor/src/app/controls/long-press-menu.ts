import { DestroyRef, Directive, ElementRef, inject } from '@angular/core';
import { TouchGestures } from '../grid/touch-gestures';

/**
 * A right-click menu for a finger: on an element with `[xuiContextMenuTriggerFor]`, a long press that stays put opens
 * the menu where the finger is. The context-menu trigger listens for `contextmenu` alone, and an iPad in Safari fires
 * none on a long press (see {@link TouchGestures}); this sends one. A browser that does fire its own (Chrome on
 * Android) is left to it. A mouse and a pen keep their right click.
 */
@Directive({
  selector: '[cthLongPressMenu]',
  host: {
    // Safari's own long-press sheet (for a link or an image) would cover the menu.
    '[style.-webkit-touch-callout]': '"none"',
    '(pointerdown)': 'down($event)',
    '(pointermove)': 'gestures.move($event)',
    '(pointerup)': 'gestures.cancel()',
    '(pointercancel)': 'gestures.cancel()',
    '(contextmenu)': 'gestures.cancel()'
  }
})
export class LongPressMenu {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private stopSwallowing: (() => void) | null = null;

  protected readonly gestures = new TouchGestures<Element>(
    () => undefined,
    (target, x, y) => {
      this.swallowClick();
      // From what was pressed, so a handler on the way up (a list row selecting itself) hears it as a right click's.
      (target.isConnected ? target : this.host).dispatchEvent(
        new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: x, clientY: y })
      );
    }
  );

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.gestures.cancel();
      this.stopSwallowing?.();
    });
  }

  protected down(event: PointerEvent): void {
    this.gestures.down(event, event.target instanceof Element ? event.target : this.host);
  }

  /**
   * The finger that opened the menu still has to lift, and a browser may make a click of that. The click would act on
   * what is under the finger (a switch in a label would toggle) and, landing outside the menu, close it again. So the
   * click of this touch is dropped; the next touch starts clean.
   */
  private swallowClick(): void {
    this.stopSwallowing?.();
    const swallow = (event: Event) => {
      event.preventDefault();
      event.stopImmediatePropagation();
      stop();
    };
    const stop = () => {
      window.removeEventListener('click', swallow, true);
      window.removeEventListener('pointerdown', stop, true);
      this.stopSwallowing = null;
    };
    window.addEventListener('click', swallow, true);
    window.addEventListener('pointerdown', stop, true);
    this.stopSwallowing = stop;
  }
}
