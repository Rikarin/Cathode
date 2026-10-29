import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CthStepBars } from './step-bars';

@Component({
  imports: [CthStepBars],
  template: `<cth-step-bars [values]="values()" [active]="active()" />`
})
class Host {
  readonly values = signal<readonly number[]>([1, 0, 0.5, 0.25]);
  readonly active = signal<number | null>(null);
}

describe('CthStepBars active stage', () => {
  function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const bars = () => Array.from(element.querySelectorAll<HTMLElement>('[role=slider]'));
    /** Which bars are marked, by their data attribute and by their lit wash and LED. */
    const marked = () => bars().filter(bar => bar.hasAttribute('data-active'));
    const lit = () =>
      bars().filter(bar => {
        const [wash, led] = Array.from(bar.querySelectorAll<HTMLElement>(':scope > span')).slice(0, 2);
        return wash?.style.opacity === '1' && led?.style.opacity === '1';
      });
    return { fixture, host: fixture.componentInstance, bars, marked, lit };
  }

  it('marks no stage by default', () => {
    const { marked, lit } = render();

    expect(marked()).toEqual([]);
    expect(lit()).toEqual([]);
  });

  it('marks the playing stage, and follows it', () => {
    const { fixture, host, bars, marked, lit } = render();

    host.active.set(2);
    fixture.detectChanges();
    expect(marked()).toEqual([bars()[2]]);
    expect(lit()).toEqual([bars()[2]]);

    host.active.set(3);
    fixture.detectChanges();
    expect(marked()).toEqual([bars()[3]]);
    expect(lit()).toEqual([bars()[3]]);

    host.active.set(null);
    fixture.detectChanges();
    expect(marked()).toEqual([]);
  });

  it('marks nothing for a stage beyond the bars, as when the stage count just shrank', () => {
    const { fixture, host, marked } = render();

    host.active.set(7);
    fixture.detectChanges();

    expect(marked()).toEqual([]);
  });

  it('only fades its marks in: nothing is added, moved or resized, and focus still shows the value', () => {
    const { fixture, host, bars } = render();
    const element = fixture.nativeElement as HTMLElement;
    const elements = element.querySelectorAll('*').length;
    const values = bars().map(bar => bar.getAttribute('aria-valuenow'));

    bars()[1]?.dispatchEvent(new FocusEvent('focus'));
    host.active.set(1);
    fixture.detectChanges();

    expect(element.querySelectorAll('*').length).toBe(elements);
    expect(bars().map(bar => bar.getAttribute('aria-valuenow'))).toEqual(values);
    const marks = Array.from(bars()[1]?.querySelectorAll<HTMLElement>(':scope > span') ?? []).slice(0, 2);
    expect(marks.every(mark => mark.classList.contains('absolute'))).toBe(true);
    const label = bars()[1]?.querySelector('.tabular-nums');
    expect(label?.classList.contains('opacity-100')).toBe(true);
  });
});
