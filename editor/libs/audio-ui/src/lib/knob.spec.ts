import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CthKnob, DoubleTap } from './knob';

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
      [disabled]="disabled()"
      [(value)]="value"
      (committed)="commits.push($event)"
    />
  `
})
class Host {
  readonly value = signal(0);
  readonly modulated = signal<number | undefined>(undefined);
  readonly disabled = signal(false);
  readonly commits: number[] = [];
}

describe('CthKnob', () => {
  function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const dial = element.querySelector('[role=slider]') as HTMLElement;
    const readout = () => element.querySelector('span')?.textContent.trim();
    const pointer = () => (element.querySelector('line') as SVGLineElement).style.transform;
    const marker = () => element.querySelector<SVGCircleElement>('[data-base-marker]');
    return { fixture, element, dial, host: fixture.componentInstance, readout, pointer, marker };
  }

  it('is a labelled ARIA slider showing the value with its unit', () => {
    const { dial, fixture } = render();

    expect(dial.getAttribute('aria-valuemin')).toBe('-12');
    expect(dial.getAttribute('aria-valuetext')).toBe('0.0 dB');
    const label = (fixture.nativeElement as HTMLElement).querySelector(
      `#${dial.getAttribute('aria-labelledby') ?? ''}`
    );
    expect(label?.textContent.trim()).toBe('Gain');
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

  it('commits once at the end of each interaction: a drag, a key, the wheel coming to rest', () => {
    vi.useFakeTimers();
    try {
      const { host, dial } = render();
      dial.setPointerCapture = () => undefined;
      dial.releasePointerCapture = () => undefined;

      dial.dispatchEvent(new PointerEvent('pointerdown', { button: 0, clientY: 100, pointerId: 1 }));
      dial.dispatchEvent(new PointerEvent('pointermove', { buttons: 1, clientY: 90, pointerId: 1 }));
      dial.dispatchEvent(new PointerEvent('pointermove', { buttons: 1, clientY: 80, pointerId: 1 }));
      expect(host.commits).toHaveLength(0); // still dragging
      dial.dispatchEvent(new PointerEvent('pointerup', { pointerId: 1 }));
      expect(host.commits).toEqual([host.value()]);

      dial.dispatchEvent(new WheelEvent('wheel', { deltaY: -1, cancelable: true }));
      dial.dispatchEvent(new WheelEvent('wheel', { deltaY: -1, cancelable: true }));
      expect(host.commits).toHaveLength(1); // the wheel is still turning
      vi.advanceTimersByTime(1_000);
      expect(host.commits).toHaveLength(2);
      expect(host.commits[1]).toBe(host.value());

      // A drag that starts while the wheel steps are fresh commits them first.
      dial.dispatchEvent(new WheelEvent('wheel', { deltaY: -1, cancelable: true }));
      const wheeled = host.value();
      dial.dispatchEvent(new PointerEvent('pointerdown', { button: 0, clientY: 100, pointerId: 1 }));
      expect(host.commits).toEqual([expect.any(Number), expect.any(Number), wheeled]);
    } finally {
      vi.useRealTimers();
    }
  });

  it('ends a drag whose release it never heard, at the next move without a button or when it loses the pointer', () => {
    const { host, dial } = render();
    dial.setPointerCapture = () => undefined;
    dial.releasePointerCapture = () => undefined;

    dial.dispatchEvent(new PointerEvent('pointerdown', { button: 0, clientY: 100, pointerId: 1 }));
    dial.dispatchEvent(new PointerEvent('pointermove', { buttons: 1, clientY: 90, pointerId: 1 }));
    const dragged = host.value();
    // The button came up over the desktop app's title bar: the next move has none held, and ends the drag there.
    dial.dispatchEvent(new PointerEvent('pointermove', { buttons: 0, clientY: 40, pointerId: 1 }));
    expect(host.value()).toBe(dragged);
    expect(host.commits).toEqual([dragged]);
    dial.dispatchEvent(new PointerEvent('pointermove', { buttons: 0, clientY: 20, pointerId: 1 }));
    expect(host.value()).toBe(dragged);

    dial.dispatchEvent(new PointerEvent('pointerdown', { button: 0, clientY: 100, pointerId: 1 }));
    dial.dispatchEvent(new PointerEvent('lostpointercapture', { pointerId: 1 }));
    expect(host.commits).toHaveLength(2);
    dial.dispatchEvent(new PointerEvent('pointermove', { buttons: 1, clientY: 50, pointerId: 1 }));
    expect(host.value()).toBe(dragged);
  });

  // #186: an iPad in Safari fires no dblclick for two taps, so a finger had no way to type a value.
  describe('a finger’s double tap', () => {
    const touch = (type: string, clientY = 100, clientX = 50) =>
      new PointerEvent(type, { button: 0, buttons: 1, clientX, clientY, pointerId: 7, pointerType: 'touch' });
    /** The knob with the pointer capture jsdom does not have. */
    const renderCapturing = () => {
      const rendered = render();
      rendered.dial.setPointerCapture = () => undefined;
      rendered.dial.releasePointerCapture = () => undefined;
      return rendered;
    };
    const tap = (target: Element, clientY = 100) => {
      target.dispatchEvent(touch('pointerdown', clientY));
      target.dispatchEvent(touch('pointerup', clientY));
    };

    it('opens the value field from the dial, as a double click does', () => {
      const { dial, element, fixture } = renderCapturing();

      tap(dial);
      fixture.detectChanges();
      expect(element.querySelector('input')).toBeNull();

      tap(dial);
      fixture.detectChanges();
      const field = element.querySelector('input');
      expect(field?.getAttribute('aria-label')).toBe('Gain value');
      expect(document.activeElement).toBe(field);
    });

    it('opens the value field from the readout', () => {
      const { element, fixture } = renderCapturing();
      const readout = element.querySelector('span') as HTMLElement;

      tap(readout);
      tap(readout);
      fixture.detectChanges();

      expect(element.querySelector('input')).not.toBeNull();
    });

    it('leaves the value where it was: the taps’ own small movement is not a turn', () => {
      const { dial, host, element, fixture } = renderCapturing();

      dial.dispatchEvent(touch('pointerdown', 100));
      dial.dispatchEvent(touch('pointermove', 96));
      dial.dispatchEvent(touch('pointerup', 96));
      expect(host.value()).not.toBe(0);
      dial.dispatchEvent(touch('pointerdown', 97));
      dial.dispatchEvent(touch('pointermove', 95));
      dial.dispatchEvent(touch('pointerup', 95));
      fixture.detectChanges();

      expect(element.querySelector('input')).not.toBeNull();
      expect(host.value()).toBe(0);
      expect(host.commits.at(-1)).toBe(0);
    });

    it('keeps the field open through the mouse events a browser sends after the second tap', () => {
      const { dial, element, fixture } = renderCapturing();
      tap(dial);
      tap(dial);
      fixture.detectChanges();

      // The default of a mousedown on the dial is to focus it, which would blur the field and close it.
      const compatibility = new MouseEvent('mousedown', { bubbles: true, cancelable: true });
      dial.dispatchEvent(compatibility);
      dial.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
      dial.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      fixture.detectChanges();

      expect(compatibility.defaultPrevented).toBe(true);
      expect(element.querySelector('input')).not.toBeNull();

      // Outside typing, a mouse press on the dial is left alone.
      (element.querySelector('input') as HTMLInputElement).dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Escape' })
      );
      fixture.detectChanges();
      const press = new MouseEvent('mousedown', { bubbles: true, cancelable: true });
      dial.dispatchEvent(press);
      expect(press.defaultPrevented).toBe(false);
    });

    it('is not a drag that ends, a tap after a drag, or two clicks of a mouse', () => {
      const { dial, element, fixture } = renderCapturing();

      // A drag, then a tap: one tap only.
      dial.dispatchEvent(touch('pointerdown', 100));
      dial.dispatchEvent(touch('pointermove', 60));
      dial.dispatchEvent(touch('pointerup', 60));
      tap(dial, 60);
      fixture.detectChanges();
      expect(element.querySelector('input')).toBeNull();

      // A tap, then a touch the browser takes away.
      tap(dial);
      dial.dispatchEvent(touch('pointerdown'));
      dial.dispatchEvent(touch('pointercancel'));
      fixture.detectChanges();
      expect(element.querySelector('input')).toBeNull();

      // A mouse has its own double click.
      for (let click = 0; click < 2; click++) {
        dial.dispatchEvent(
          new PointerEvent('pointerdown', { button: 0, clientY: 100, pointerId: 1, pointerType: 'mouse' })
        );
        dial.dispatchEvent(new PointerEvent('pointerup', { clientY: 100, pointerId: 1, pointerType: 'mouse' }));
      }

      fixture.detectChanges();
      expect(element.querySelector('input')).toBeNull();
    });

    it('does nothing on a disabled knob', () => {
      const { dial, element, fixture } = renderCapturing();
      fixture.componentInstance.disabled.set(true);
      fixture.detectChanges();

      tap(dial);
      tap(dial);
      fixture.detectChanges();

      expect(element.querySelector('input')).toBeNull();
    });
  });

  describe('DoubleTap', () => {
    const at = (type: string, timeStamp: number, clientX = 0, clientY = 0, pointerType = 'touch') => ({
      type,
      timeStamp,
      clientX,
      clientY,
      pointerType
    });
    const tapAt = (taps: DoubleTap, time: number, x = 0, y = 0, held = 80) => {
      taps.press(at('pointerdown', time, x, y));
      return taps.lift(at('pointerup', time + held, x, y));
    };

    it('is two taps close in time and place', () => {
      const taps = new DoubleTap();
      expect(tapAt(taps, 0)).toBe(false);
      expect(tapAt(taps, 200, 5, 5)).toBe(true);
      // The third tap starts again.
      expect(tapAt(taps, 400)).toBe(false);
      expect(tapAt(taps, 600)).toBe(true);
    });

    it('is not two taps far apart in time or place', () => {
      const taps = new DoubleTap();
      expect(tapAt(taps, 0)).toBe(false);
      expect(tapAt(taps, 400)).toBe(false); // too late: a first tap again
      expect(tapAt(taps, 600, 80, 0)).toBe(false); // another place
      expect(tapAt(taps, 800, 80, 0)).toBe(true);
    });

    it('does not count a long press or a touch that moved as a tap', () => {
      const taps = new DoubleTap();
      expect(tapAt(taps, 0)).toBe(false);
      expect(tapAt(taps, 200, 0, 0, 600)).toBe(false); // held: the long-press menu's
      expect(tapAt(taps, 900)).toBe(false);

      taps.press(at('pointerdown', 1000));
      expect(taps.lift(at('pointerup', 1050, 0, 30))).toBe(false); // moved: a drag
      expect(tapAt(taps, 1100)).toBe(false);
    });

    it('says whether a press may be a second tap, and counts only fingers', () => {
      const taps = new DoubleTap();
      expect(taps.press(at('pointerdown', 0))).toBe(false);
      taps.lift(at('pointerup', 50));
      expect(taps.press(at('pointerdown', 200))).toBe(true);
      taps.lift(at('pointerup', 250));

      expect(taps.press(at('pointerdown', 300, 0, 0, 'mouse'))).toBe(false);
      expect(taps.lift(at('pointerup', 320))).toBe(false);
      expect(taps.press(at('pointerdown', 400, 0, 0, 'mouse'))).toBe(false);
      expect(taps.lift(at('pointerup', 420))).toBe(false);
    });
  });

  it('takes a typed value with its unit and clamps it', () => {
    const { dial, host, fixture } = render();

    dial.dispatchEvent(new MouseEvent('dblclick'));
    fixture.detectChanges();
    const editor = (fixture.nativeElement as HTMLElement).querySelector('input') as HTMLInputElement;
    // It opens focused with the value selected, so typing replaces it.
    expect(document.activeElement).toBe(editor);
    expect(editor.selectionStart).toBe(0);
    expect(editor.selectionEnd).toBe(editor.value.length);
    editor.value = '30';
    editor.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));

    expect(host.value()).toBe(12);
  });

  it('keeps the field open and marks it when the typed text has no value, until it is edited or left', () => {
    const { dial, host, element, fixture } = render();
    const field = () => element.querySelector('input');

    dial.dispatchEvent(new MouseEvent('dblclick'));
    fixture.detectChanges();
    const editor = field() as HTMLInputElement;
    editor.value = 'loud';
    editor.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    fixture.detectChanges();
    expect(field()).toBe(editor);
    expect(editor.getAttribute('aria-invalid')).toBe('true');
    expect(host.commits).toHaveLength(0);

    editor.value = '2,5';
    editor.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(editor.hasAttribute('aria-invalid')).toBe(false);
    editor.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    fixture.detectChanges();
    expect(field()).toBeNull();
    expect(host.value()).toBe(2.5);
    expect(host.commits).toEqual([2.5]);

    // Leaving the field with such text closes it and changes nothing.
    dial.dispatchEvent(new MouseEvent('dblclick'));
    fixture.detectChanges();
    const again = field() as HTMLInputElement;
    again.value = 'loud';
    again.dispatchEvent(new FocusEvent('blur'));
    fixture.detectChanges();
    expect(field()).toBeNull();
    expect(host.value()).toBe(2.5);
    expect(host.commits).toEqual([2.5]);
  });

  it('reads back the value it shows, minus infinity as the minimum', () => {
    const { dial, host, element, fixture } = render();
    host.value.set(-12);
    fixture.detectChanges();

    dial.dispatchEvent(new MouseEvent('dblclick'));
    fixture.detectChanges();
    const editor = element.querySelector('input') as HTMLInputElement;
    expect(editor.value).toBe('-12.0 dB');
    editor.value = '-∞ dB';
    editor.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    expect(host.value()).toBe(-12);
    expect(host.commits).toEqual([-12]);
  });

  it('cancels typing with Escape: the value stays, focus returns to the dial and the key goes no further', () => {
    const { dial, host, element, fixture } = render();
    const escapes: Event[] = [];
    element.addEventListener('keydown', event => escapes.push(event));

    dial.dispatchEvent(new MouseEvent('dblclick'));
    fixture.detectChanges();
    const editor = element.querySelector('input') as HTMLInputElement;
    editor.value = '6';
    editor.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    fixture.detectChanges();

    expect(element.querySelector('input')).toBeNull();
    expect(host.value()).toBe(0);
    expect(host.commits).toHaveLength(0);
    expect(document.activeElement).toBe(dial);
    expect(escapes).toHaveLength(0);
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
      dial.dispatchEvent(new PointerEvent('pointermove', { buttons: 1, clientY: 50, pointerId: 1 })); // a quarter of the travel up
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
