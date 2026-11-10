import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { LongPressMenu } from './long-press-menu';

@Component({
  imports: [LongPressMenu],
  template: `
    <button
      type="button"
      cthLongPressMenu
      (contextmenu)="menus.push([$event.clientX, $event.clientY])"
      (click)="clicks.push('row')"
    >
      <span data-testid="inner">Engaged</span>
    </button>
  `
})
class Host {
  readonly menus: [number, number][] = [];
  readonly clicks: string[] = [];
}

describe('LongPressMenu', () => {
  let host: Host;
  let inner: HTMLElement;

  const pointer = (type: string, pointerType: string, x = 0, y = 0) =>
    inner.dispatchEvent(new PointerEvent(type, { pointerType, clientX: x, clientY: y, bubbles: true }));

  beforeEach(() => {
    vi.useFakeTimers();
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    host = fixture.componentInstance;
    inner = (fixture.nativeElement as HTMLElement).querySelector('[data-testid=inner]') as HTMLElement;
  });
  afterEach(() => vi.useRealTimers());

  it('opens the menu where a finger is held still', () => {
    pointer('pointerdown', 'touch', 40, 50);
    vi.advanceTimersByTime(600);

    expect(host.menus).toEqual([[40, 50]]);
  });

  it('does not after the finger lifts or moves away, nor for a mouse', () => {
    pointer('pointerdown', 'touch');
    pointer('pointerup', 'touch');
    vi.advanceTimersByTime(600);
    pointer('pointerdown', 'touch');
    pointer('pointermove', 'touch', 0, 30);
    vi.advanceTimersByTime(600);
    pointer('pointerdown', 'mouse');
    vi.advanceTimersByTime(600);

    expect(host.menus).toEqual([]);
  });

  it('leaves the menu to a browser that opens it on a long press itself', () => {
    pointer('pointerdown', 'touch', 40, 50);
    vi.advanceTimersByTime(500);
    inner.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, clientX: 40, clientY: 50 }));
    vi.advanceTimersByTime(600);

    expect(host.menus).toHaveLength(1);
  });

  it('drops the click of the finger lifting after it, and no later one', () => {
    pointer('pointerdown', 'touch');
    vi.advanceTimersByTime(600);
    pointer('pointerup', 'touch');
    const lift = new MouseEvent('click', { bubbles: true, cancelable: true });
    inner.dispatchEvent(lift);
    expect(host.clicks).toEqual([]);
    expect(lift.defaultPrevented).toBe(true);

    inner.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    expect(host.clicks).toEqual(['row']);

    // No click came of the lift: the next touch is a tap of its own.
    pointer('pointerdown', 'touch');
    vi.advanceTimersByTime(600);
    pointer('pointerup', 'touch');
    pointer('pointerdown', 'touch');
    pointer('pointerup', 'touch');
    inner.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    expect(host.clicks).toEqual(['row', 'row']);
  });
});
