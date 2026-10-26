import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CthKnob } from './knob';

@Component({
  imports: [CthKnob],
  template: `
    <cth-knob
      label="Gain"
      unit="dB"
      [min]="-12"
      [max]="12"
      [defaultValue]="0"
      [modulated]="modulated()"
      [(value)]="value"
    />
  `
})
class Host {
  readonly value = signal(0);
  readonly modulated = signal<number | undefined>(undefined);
}

describe('CthKnob', () => {
  function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const dial = fixture.nativeElement.querySelector('[role=slider]') as HTMLElement;
    const readout = () => (fixture.nativeElement.querySelector('span') as HTMLElement).textContent?.trim();
    const pointer = () => (fixture.nativeElement.querySelector('line') as SVGLineElement).style.transform;
    const marker = () => fixture.nativeElement.querySelector('[data-base-marker]') as SVGCircleElement | null;
    return { fixture, dial, host: fixture.componentInstance, readout, pointer, marker };
  }

  it('is a labelled ARIA slider showing the value with its unit', () => {
    const { dial, fixture } = render();

    expect(dial.getAttribute('aria-valuemin')).toBe('-12');
    expect(dial.getAttribute('aria-valuetext')).toBe('0.0 dB');
    const label = fixture.nativeElement.querySelector(`#${dial.getAttribute('aria-labelledby')}`);
    expect(label.textContent.trim()).toBe('Gain');
  });

  it('steps with the keyboard: 1 % per arrow, 10 % per page, ends with Home and End', () => {
    const { dial, host, fixture } = render();

    dial.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp' }));
    expect(host.value()).toBeCloseTo(0.24, 5);
    dial.dispatchEvent(new KeyboardEvent('keydown', { key: 'PageDown' }));
    expect(host.value()).toBeCloseTo(-2.16, 5);
    dial.dispatchEvent(new KeyboardEvent('keydown', { key: 'End' }));
    fixture.detectChanges();
    expect(host.value()).toBe(12);
    expect(dial.getAttribute('aria-valuetext')).toBe('+12.0 dB');
  });

  it('takes a typed value with its unit and clamps it', async () => {
    const { dial, host, fixture } = render();

    dial.dispatchEvent(new MouseEvent('dblclick'));
    fixture.detectChanges();
    const editor = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    editor.value = '30';
    editor.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));

    expect(host.value()).toBe(12);
  });

  describe('while a modifier drives it', () => {
    it('points at and reads the modulated value, and marks the value set on the track', () => {
      const { host, fixture, readout, pointer, marker, dial } = render();
      expect(pointer()).toBe('rotate(135deg)'); // 0 dB: half of the 270° sweep
      expect(marker()).toBeNull();

      host.modulated.set(6);
      fixture.detectChanges();

      expect(readout()).toBe('+6.0 dB');
      expect(pointer()).toBe('rotate(202.5deg)'); // three quarters
      expect(marker()).not.toBeNull();
      expect(dial.getAttribute('aria-valuetext')).toBe('0.0 dB'); // the slider's value is still the one set
      expect(dial.getAttribute('title')).toContain('+6.0 dB, modulated from 0.0 dB');

      host.modulated.set(undefined);
      fixture.detectChanges();
      expect(readout()).toBe('0.0 dB');
      expect(marker()).toBeNull();
    });

    it('clamps a modulated value to the range', () => {
      const { host, fixture, readout } = render();

      host.modulated.set(40);
      fixture.detectChanges();

      expect(readout()).toBe('+12.0 dB');
    });

    it('shows the value being set while dragging, then follows the modifier again', () => {
      const { host, fixture, readout, dial, marker } = render();
      dial.setPointerCapture = () => undefined;
      dial.releasePointerCapture = () => undefined;
      host.modulated.set(6);
      fixture.detectChanges();

      dial.dispatchEvent(new PointerEvent('pointerdown', { button: 0, clientY: 100, pointerId: 1 }));
      dial.dispatchEvent(new PointerEvent('pointermove', { clientY: 50, pointerId: 1 })); // a quarter of the travel up
      host.modulated.set(-3); // the modifier keeps moving meanwhile
      fixture.detectChanges();

      expect(host.value()).toBeCloseTo(6, 5);
      expect(readout()).toBe('+6.0 dB');
      expect(marker()).toBeNull();

      dial.dispatchEvent(new PointerEvent('pointerup', { pointerId: 1 }));
      fixture.detectChanges();
      expect(readout()).toBe('-3.0 dB');
      expect(marker()).not.toBeNull();
    });

    it('shows the value set for a moment after a key step', () => {
      vi.useFakeTimers();
      try {
        const { host, fixture, readout, dial } = render();
        host.modulated.set(6);
        fixture.detectChanges();

        dial.dispatchEvent(new KeyboardEvent('keydown', { key: 'PageUp' }));
        fixture.detectChanges();
        expect(readout()).toBe('+2.4 dB');

        vi.advanceTimersByTime(1_000);
        fixture.detectChanges();
        expect(readout()).toBe('+6.0 dB');
      } finally {
        vi.useRealTimers();
      }
    });
  });
});
