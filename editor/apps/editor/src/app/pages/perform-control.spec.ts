import { TestBed } from '@angular/core/testing';
import { EventStream } from '../core/events';
import { CatalogStore } from '../state/catalog.store';
import { LiveStore } from '../state/live.store';
import type { ResolvedControl } from './perform';
import { PerformControl } from './perform-control';

describe('PerformControl toolbar', () => {
  function render(resolved: Partial<ResolvedControl> = {}, place: { first?: boolean; last?: boolean } = {}) {
    TestBed.configureTestingModule({
      providers: [
        { provide: LiveStore, useValue: {} },
        { provide: CatalogStore, useValue: { info: () => undefined } },
        { provide: EventStream, useValue: {} }
      ]
    });
    const fixture = TestBed.createComponent(PerformControl);
    // A global control whose block this preset lacks: the toolbar without a knob to render.
    fixture.componentRef.setInput('control', {
      control: { block: 'amp1', param: 'gain', label: null },
      block: null,
      param: null,
      label: 'amp1 gain',
      name: 'amp1 gain',
      ...resolved
    });
    fixture.componentRef.setInput('first', place.first ?? false);
    fixture.componentRef.setInput('last', place.last ?? false);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const button = (label: string) => host.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`);
    const renamed: (string | null)[] = [];
    const moved: number[] = [];
    fixture.componentInstance.rename.subscribe(label => renamed.push(label));
    fixture.componentInstance.move.subscribe(step => moved.push(step));
    return { fixture, host, button, renamed, moved };
  }

  it('names its buttons after the control and disables moves past the ends', () => {
    const { host, button } = render({}, { first: true });
    expect(host.querySelector('[role="toolbar"]')?.getAttribute('aria-label')).toBe('amp1 gain');
    expect(button('Move amp1 gain earlier')?.disabled).toBe(true);
    expect(button('Move amp1 gain later')?.disabled).toBe(false);
    expect(button('Rename amp1 gain')).not.toBeNull();
    expect(button('Remove amp1 gain')).not.toBeNull();
    expect(host.querySelector('[data-testid="perform-drag"]')?.getAttribute('draggable')).toBe('true');
  });

  it('asks to move one place earlier or later', () => {
    const { button, moved } = render();
    button('Move amp1 gain later')?.click();
    button('Move amp1 gain earlier')?.click();
    expect(moved).toEqual([1, -1]);
  });

  it('renames through a labelled field: Enter keeps the text, blank goes back to the name', async () => {
    const { fixture, host, button, renamed } = render();
    button('Rename amp1 gain')?.click();
    fixture.detectChanges();
    await fixture.whenStable();

    const field = host.querySelector<HTMLInputElement>('input[aria-label="Label of amp1 gain"]');
    expect(field).not.toBeNull();
    expect(field?.placeholder).toBe('amp1 gain');
    if (!field) {
      return;
    }

    field.value = 'Crunch';
    field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    field.dispatchEvent(new Event('blur')); // the field closing: not a second rename
    fixture.detectChanges();
    expect(renamed).toEqual(['Crunch']);
    expect(host.querySelector('input[aria-label="Label of amp1 gain"]')).toBeNull();
  });

  it('leaves the caption as it was on Escape, or when the text did not change', () => {
    const { fixture, host, button, renamed } = render({
      control: { block: 'amp1', param: 'gain', label: 'Crunch' },
      label: 'Crunch'
    });
    button('Rename Crunch')?.click();
    fixture.detectChanges();
    const field = host.querySelector<HTMLInputElement>('input[aria-label="Label of Crunch"]');
    expect(field?.value).toBe('Crunch');
    field?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    fixture.detectChanges();

    button('Rename Crunch')?.click();
    fixture.detectChanges();
    const again = host.querySelector<HTMLInputElement>('input[aria-label="Label of Crunch"]');
    if (again) {
      again.value = 'Lead';
      again.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
      again.dispatchEvent(new Event('blur'));
    }

    fixture.detectChanges();
    expect(renamed).toEqual([]);

    button('Rename Crunch')?.click();
    fixture.detectChanges();
    const cleared = host.querySelector<HTMLInputElement>('input[aria-label="Label of Crunch"]');
    if (cleared) {
      cleared.value = '  ';
      cleared.dispatchEvent(new Event('blur'));
    }

    expect(renamed).toEqual([null]);
  });
});
