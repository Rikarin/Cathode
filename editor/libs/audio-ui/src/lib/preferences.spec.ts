import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CthKnob } from './knob';
import { CTH_KNOB_PREFERENCES, circularNormal, frameDue } from './preferences';

@Component({
  imports: [CthKnob],
  template: `<cth-knob label="Gain" [min]="0" [max]="10" [(value)]="value" />`
})
class Host {
  readonly value = signal(5);
}

describe('knob and meter preferences', () => {
  it('map a pointer around the knob onto its 270° travel', () => {
    expect(circularNormal(-10, 10)).toBeCloseTo(0, 5); // 7:30, the start
    expect(circularNormal(0, -10)).toBeCloseTo(0.5, 5); // straight up, the middle
    expect(circularNormal(10, 10)).toBeCloseTo(1, 5); // 4:30, the end
    expect(circularNormal(-0.2, 10)).toBe(0); // the dead zone keeps the nearer end
    expect(circularNormal(0, 0)).toBeNull();
  });

  it('redraw meters at the chosen rate', () => {
    expect(frameDue(16, 0, 60)).toBe(true);
    expect(frameDue(16, 0, 30)).toBe(false);
    expect(frameDue(34, 0, 30)).toBe(true);
    expect(frameDue(1000, 0, 0)).toBe(false);
  });

  function knob(wheel: 'normal' | 'reverse' | 'off') {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: CTH_KNOB_PREFERENCES,
          useValue: { mode: signal('linear'), wheel: signal(wheel), tooltips: signal(false) }
        }
      ]
    });
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return { fixture, dial: fixture.nativeElement.querySelector('[role=slider]') as HTMLElement };
  }

  it('turn the wheel the user’s way, or not at all', () => {
    const reverse = knob('reverse');
    reverse.dial.dispatchEvent(new WheelEvent('wheel', { deltaY: -1, cancelable: true }));
    expect(reverse.fixture.componentInstance.value()).toBeCloseTo(4.9, 5);
    expect(reverse.dial.getAttribute('title')).toBeNull(); // tooltips off
  });

  it('leave the wheel to the page when off', () => {
    const off = knob('off');
    const wheel = new WheelEvent('wheel', { deltaY: -1, cancelable: true });
    off.dial.dispatchEvent(wheel);
    expect(off.fixture.componentInstance.value()).toBe(5);
    expect(wheel.defaultPrevented).toBe(false);
  });
});
