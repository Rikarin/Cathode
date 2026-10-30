import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NumberSteppers } from './number-steppers';

@Component({
  imports: [NumberSteppers],
  template: `
    <label>
      Channel
      <input type="number" cthSteppers min="1" max="16" value="15" (change)="changes = changes + 1" />
    </label>
    @if (editing()) {
      <input type="number" cthSteppers step="0.5" value="1" data-testid="late" />
    }
  `
})
class Host {
  readonly editing = signal(false);
  changes = 0;
}

describe('NumberSteppers', () => {
  function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    return { fixture, root };
  }

  const steppers = (field: HTMLInputElement) =>
    [...(field.parentElement?.querySelectorAll<HTMLButtonElement>('.cth-stepper') ?? [])] as const;

  it('puts the steppers beside the field and steps it as the arrow keys do, firing change', () => {
    const { fixture, root } = render();
    const field = root.querySelector('input') as HTMLInputElement;
    expect(field.parentElement?.className).toBe('cth-number-field');
    expect(field.parentElement?.parentElement?.tagName).toBe('LABEL');

    const [up, down] = steppers(field);
    up?.click();
    expect(field.value).toBe('16');
    up?.click(); // max holds
    expect(field.value).toBe('16');
    down?.click();
    expect(field.value).toBe('15');
    expect(fixture.componentInstance.changes).toBe(3);
  });

  it('takes no focus from the field, and is not in the tab order', () => {
    const { root } = render();
    const field = root.querySelector('input') as HTMLInputElement;
    const [up] = steppers(field);
    const press = new MouseEvent('mousedown', { bubbles: true, cancelable: true });
    up?.dispatchEvent(press);
    expect(press.defaultPrevented).toBe(true);
    expect(up?.tabIndex).toBe(-1);
  });

  it('encloses a field that an @if places, and takes the group away with it', () => {
    const { fixture, root } = render();
    fixture.componentInstance.editing.set(true);
    fixture.detectChanges();
    TestBed.tick();
    const late = root.querySelector<HTMLInputElement>('[data-testid=late]');
    expect(late?.parentElement?.className).toBe('cth-number-field');
    steppers(late as HTMLInputElement)[0]?.click();
    expect(late?.value).toBe('1.5');

    fixture.componentInstance.editing.set(false);
    fixture.detectChanges();
    expect(root.querySelectorAll('.cth-number-field')).toHaveLength(1);
  });
});
