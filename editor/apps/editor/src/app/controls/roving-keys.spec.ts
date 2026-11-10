import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { RovingKeys } from './roving-keys';

@Component({
  imports: [RovingKeys],
  template: `
    <div cthRovingKeys aria-label="Pages" aria-orientation="vertical" [attr.role]="role()">
      @for (name of names; track name) {
        <button
          type="button"
          [attr.role]="role() === 'tablist' ? 'tab' : 'option'"
          [attr.tabindex]="name === chosen() ? 0 : -1"
          [attr.aria-selected]="name === chosen()"
          (click)="chosen.set(name)"
        >
          {{ name }}
        </button>
      }
    </div>
  `
})
class Host {
  readonly names = ['Basic', 'Tone', 'Advanced'];
  readonly role = signal<'tablist' | 'listbox'>('tablist');
  readonly chosen = signal('Basic');
}

describe('RovingKeys', () => {
  function render(role: 'tablist' | 'listbox' = 'tablist') {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.role.set(role);
    fixture.detectChanges();
    const host = fixture.componentInstance;
    const items = () => Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'));
    const press = (key: string, init: KeyboardEventInit = {}) => {
      const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
      (document.activeElement ?? document.body).dispatchEvent(event);
      fixture.detectChanges();
      return event;
    };
    items()[0]?.focus();
    return { host, items, press };
  }

  it('moves focus and the selection with Up, Down, Home and End, one tab stop at the selection', () => {
    const { host, items, press } = render();

    expect(press('ArrowDown').defaultPrevented).toBe(true);
    expect(host.chosen()).toBe('Tone');
    expect(document.activeElement).toBe(items()[1]);
    expect(items().map(item => item.getAttribute('tabindex'))).toEqual(['-1', '0', '-1']);

    press('End');
    expect(host.chosen()).toBe('Advanced');
    press('ArrowUp');
    expect(host.chosen()).toBe('Tone');
    press('Home');
    expect(host.chosen()).toBe('Basic');
    expect(document.activeElement).toBe(items()[0]);
  });

  it('wraps round the ends of a tablist, and stops at the ends of a listbox', () => {
    const tabs = render('tablist');
    tabs.press('ArrowUp');
    expect(tabs.host.chosen()).toBe('Advanced');
    tabs.press('ArrowDown');
    expect(tabs.host.chosen()).toBe('Basic');

    TestBed.resetTestingModule();
    const list = render('listbox');
    expect(list.press('ArrowUp').defaultPrevented).toBe(true);
    expect(list.host.chosen()).toBe('Basic');
    list.press('End');
    list.press('ArrowDown');
    expect(list.host.chosen()).toBe('Advanced');
  });

  it('leaves the other keys, the other axis and keys with a modifier alone', () => {
    const { host, press } = render();

    expect(press('ArrowRight').defaultPrevented).toBe(false);
    expect(press('ArrowDown', { altKey: true }).defaultPrevented).toBe(false);
    expect(press('a').defaultPrevented).toBe(false);
    expect(host.chosen()).toBe('Basic');
  });
});
