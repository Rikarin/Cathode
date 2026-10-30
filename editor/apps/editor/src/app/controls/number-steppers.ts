import { DestroyRef, Directive, ElementRef, Injector, afterNextRender, inject } from '@angular/core';

/** Material's rounded keyboard arrow, pointing up, as the preset display's steppers draw it. */
const ArrowUp =
  'M8.12 14.71 12 10.83l3.88 3.88a.996.996 0 1 0 1.41-1.41L12.7 8.71a.996.996 0 0 0-1.41 0L6.7 13.3a.996.996 0 0 0 0 1.41c.39.38 1.03.39 1.42 0z';

/**
 * A number field (`<input xuiInput type="number" cthSteppers>`) with the preset display's up and down steppers at its
 * right end, in place of the browser's own spin arrows. The field is wrapped in a small inline group with the two steppers; a click steps it as the arrow keys do
 * (stepUp and stepDown, so its step, min and max hold) and then fires the input and change events typing fires, so
 * whatever the field's template listens for runs as it does for a typed value. The steppers take no focus, so a field
 * that commits on blur (the tempo) stays open while it is stepped.
 */
@Directive({ selector: 'input[cthSteppers]' })
export class NumberSteppers {
  /** The group the field is wrapped in, with its steppers. */
  protected readonly group = wrap(
    inject<ElementRef<HTMLInputElement>>(ElementRef).nativeElement,
    inject(DestroyRef),
    inject(Injector)
  );
}

function wrap(field: HTMLInputElement, destroyRef: DestroyRef, injector: Injector): HTMLElement {
  const document = field.ownerDocument;
  const group = document.createElement('span');
  group.className = 'cth-number-field';
  const enclose = () => {
    field.parentNode?.insertBefore(group, field);
    group.prepend(field);
  };
  // A field at the root of an @if block is placed only after its directives start: enclose it once it is, in the write
  // phase, so before any callback that focuses it (moving a focused field would blur it, and a blur commits the tempo).
  if (field.parentNode) {
    enclose();
  } else {
    afterNextRender({ write: enclose }, { injector });
  }

  const steppers = document.createElement('span');
  steppers.className = 'cth-number-steppers';
  steppers.append(stepper(field, 1, 'Increase'), stepper(field, -1, 'Decrease'));
  group.append(steppers);

  // Angular removes the field from its parent, which is now the group: take the group with it.
  destroyRef.onDestroy(() => group.remove());
  return group;
}

function stepper(field: HTMLInputElement, direction: 1 | -1, label: string): HTMLButtonElement {
  const button = field.ownerDocument.createElement('button');
  button.type = 'button';
  button.tabIndex = -1; // the arrow keys step the field from the keyboard
  button.className = 'cth-stepper';
  button.setAttribute('aria-label', label);
  button.innerHTML =
    `<svg viewBox="0 0 24 24" width="14" height="14" aria-hidden="true"` +
    `${direction < 0 ? ' style="transform: rotate(180deg)"' : ''}><path fill="currentColor" d="${ArrowUp}"/></svg>`;
  button.addEventListener('mousedown', event => event.preventDefault()); // keep focus in the field
  button.addEventListener('click', () => {
    if (field.disabled || field.readOnly) {
      return;
    }

    if (direction > 0) {
      field.stepUp();
    } else {
      field.stepDown();
    }

    field.dispatchEvent(new Event('input', { bubbles: true }));
    field.dispatchEvent(new Event('change', { bubbles: true }));
  });
  return button;
}
