import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { type PickerEntry, PickerList, pickerEntries } from './picker-list';

@Component({
  imports: [PickerList],
  template: `<cth-picker-list
    [entries]="entries"
    (favorite)="favorites.push([$event.entry.id, $event.favorite])"
    (extraChosen)="extras = extras + 1"
  />`
})
class Host {
  extras = 0;
  readonly entries: PickerEntry[] = [
    { id: 'a', name: 'Alpha', detail: 'clean', favorite: true, color: 'limegreen' },
    { id: 'b', name: 'Bravo' }
  ];
  readonly favorites: [string, boolean][] = [];
}

describe('pickerEntries', () => {
  it('lists favourites first, then by name, matching every word in the name or detail', () => {
    const entries: PickerEntry[] = [
      { id: '1', name: 'Zulu', favorite: true },
      { id: '2', name: 'Alpha', detail: 'clean' },
      { id: '3', name: 'Bravo', detail: 'clean lead' }
    ];

    expect(pickerEntries(entries, '').map(e => e.id)).toEqual(['1', '2', '3']);
    expect(pickerEntries(entries, 'clean b').map(e => e.id)).toEqual(['3']);
    expect(pickerEntries(entries, '', true).map(e => e.id)).toEqual(['1']);
  });
});

describe('PickerList', () => {
  function render() {
    // jsdom lays nothing out, so it has no scrollIntoView for the arrow keys to call.
    Element.prototype.scrollIntoView = () => undefined;
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const search = element.querySelector('[role=combobox]') as HTMLInputElement;
    const button = (id: string) => element.querySelector(`[data-testid=${id}]`) as HTMLButtonElement;
    return { fixture, element, search, button, host: fixture.componentInstance };
  }

  it('keeps the controls inside an option out of the tab order and out of its name', () => {
    const { element } = render();

    const inOptions = Array.from(element.querySelectorAll('[role=option] button'));
    expect(inOptions).toHaveLength(4);
    for (const control of inOptions) {
      expect(control.getAttribute('tabindex')).toBe('-1');
      expect(control.getAttribute('aria-hidden')).toBe('true');
    }

    // What the star and the dot show is said in words instead.
    const [alpha, bravo] = Array.from(element.querySelectorAll('[role=option]'));
    expect(alpha?.querySelector('.sr-only')?.textContent).toBe(', favourite, Green tag');
    expect(bravo?.querySelector('.sr-only')).toBeNull();
  });

  it('favours and tags the highlighted entry from the buttons under the list', () => {
    const { fixture, search, button, host } = render();

    expect(button('picker-favorite').getAttribute('aria-label')).toBe('Unfavourite Alpha');
    expect(button('picker-favorite').getAttribute('aria-pressed')).toBe('true');
    expect(button('picker-color').getAttribute('aria-label')).toBe('Colour tag of Alpha: Green');

    search.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown' }));
    fixture.detectChanges();
    expect(button('picker-favorite').getAttribute('aria-label')).toBe('Favourite Bravo');
    expect(button('picker-color').getAttribute('aria-label')).toBe('Colour tag of Bravo: none');
    button('picker-favorite').click();
    expect(host.favorites).toEqual([['b', true]]);
  });

  it('chooses no extra row with ↓ and ↵ when the list is empty and there is none', () => {
    const { fixture, search, host } = render();

    search.value = 'zzz';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    search.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown' }));
    search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    fixture.detectChanges();

    expect(host.extras).toBe(0);
  });

  it('has nothing to favour or tag when nothing matches', () => {
    const { fixture, search, button } = render();

    search.value = 'zzz';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(button('picker-favorite').disabled).toBe(true);
    expect(button('picker-color').disabled).toBe(true);
  });
});
