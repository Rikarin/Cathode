import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CthToggle } from './toggle';

@Component({
  imports: [CthToggle],
  template: `<cth-toggle label="Bright" [(checked)]="on" (committed)="commits.push($event)" />`
})
class Host {
  readonly on = signal(false);
  readonly commits: boolean[] = [];
}

describe('CthToggle', () => {
  function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const button = fixture.nativeElement.querySelector('[role=switch]') as HTMLButtonElement;
    return { fixture, button, host: fixture.componentInstance };
  }

  it('is a labelled ARIA switch whose handle says OFF or ON', () => {
    const { button, fixture } = render();

    expect(button.getAttribute('aria-checked')).toBe('false');
    expect(button.textContent?.trim()).toBe('OFF');
    const label = fixture.nativeElement.querySelector(`#${button.getAttribute('aria-labelledby')}`);
    expect(label.textContent.trim()).toBe('Bright');

    button.click();
    fixture.detectChanges();
    expect(button.getAttribute('aria-checked')).toBe('true');
    expect(button.textContent?.trim()).toBe('ON');
  });

  it('turns on with ↑ and off with ↓, committing only changes', () => {
    const { button, host } = render();

    button.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp' }));
    button.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp' }));
    expect(host.on()).toBe(true);
    button.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown' }));
    expect(host.on()).toBe(false);
    expect(host.commits).toEqual([true, false]);
  });
});
