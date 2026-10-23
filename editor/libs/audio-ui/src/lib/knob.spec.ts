import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CthKnob } from './knob';

@Component({
    imports: [CthKnob],
    template: `<cth-knob label="Gain" unit="dB" [min]="-12" [max]="12" [defaultValue]="0" [(value)]="value" />`
})
class Host {
    readonly value = signal(0);
}

describe('CthKnob', () => {
    function render() {
        const fixture = TestBed.createComponent(Host);
        fixture.detectChanges();
        const dial = fixture.nativeElement.querySelector('[role=slider]') as HTMLElement;
        return { fixture, dial, host: fixture.componentInstance };
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
});
