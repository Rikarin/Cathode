import { Directive, ElementRef, inject } from '@angular/core';

/**
 * The arrow keys a `role="tablist"` or `role="listbox"` promises, for one built from plain elements: on the container,
 * Up and Down (Left and Right when its `aria-orientation` is horizontal) move to the next item, Home and End to the
 * ends, and the item moved to is focused and clicked, so selection follows focus (the WAI-ARIA Tabs and single-select
 * Listbox patterns). A tablist wraps round at its ends; a listbox stops there.
 *
 * The items give themselves a roving tabindex: 0 on the selected one and -1 on the rest, so Tab enters the widget at
 * the selection and leaves it at the next press.
 */
@Directive({
  selector: '[cthRovingKeys]',
  host: { '(keydown)': 'key($event)' }
})
export class RovingKeys {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected key(event: KeyboardEvent): void {
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) {
      return;
    }

    const items = Array.from(this.host.querySelectorAll<HTMLElement>('[role=tab], [role=option]'));
    const from = items.findIndex(item => event.target instanceof Node && item.contains(event.target));
    if (from < 0) {
      return;
    }

    const wraps = this.host.getAttribute('role') === 'tablist';
    // ARIA's defaults: a tablist lies flat and a listbox stands, unless it says otherwise.
    const orientation = this.host.getAttribute('aria-orientation') ?? (wraps ? 'horizontal' : 'vertical');
    const [back, forward] = orientation === 'horizontal' ? ['ArrowLeft', 'ArrowRight'] : ['ArrowUp', 'ArrowDown'];
    const last = items.length - 1;
    let to: number;
    switch (event.key) {
      case back:
        to = from > 0 ? from - 1 : wraps ? last : 0;
        break;
      case forward:
        to = from < last ? from + 1 : wraps ? 0 : last;
        break;
      case 'Home':
        to = 0;
        break;
      case 'End':
        to = last;
        break;
      default:
        return;
    }

    // The key is this widget's even at an end, where it moves nothing: it must not scroll the page instead.
    event.preventDefault();
    const item = items[to];
    if (item && to !== from) {
      item.focus();
      item.click();
    }
  }
}
