import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { ParameterInfo, PresetModifier } from '@cathode/api-client';
import { LiveStore } from '../state/live.store';
import { ModifierDialog, shape } from './modifier-dialog';

describe('modifier curves (as the engine applies them)', () => {
  it('pin both ends and bend the middle the documented way', () => {
    for (const curve of ['linear', 'exp', 'log', 'scurve']) {
      expect(shape(curve, 0)).toBeCloseTo(0, 6);
      expect(shape(curve, 1)).toBeCloseTo(1, 6);
    }

    expect(shape('linear', 0.5)).toBe(0.5);
    expect(shape('exp', 0.5)).toBeLessThan(0.5);
    expect(shape('log', 0.5)).toBeGreaterThan(0.5);
    expect(shape('scurve', 0.5)).toBeCloseTo(0.5, 6);
    expect(shape('scurve', 0.25)).toBeLessThan(0.25);
  });
});

describe('ModifierDialog draft', () => {
  const param = { id: 'mix', name: 'Mix', unit: '%', min: 0, max: 100, default: 50 } as unknown as ParameterInfo;
  const stored = (min: number): PresetModifier => ({
    block: 'delay1',
    param: 'mix',
    source: 'lfo1',
    min,
    max: 80,
    curve: 'linear',
    attackMs: 0,
    releaseMs: 0
  });

  function render() {
    const modifier = signal<PresetModifier | undefined>(stored(10));
    TestBed.configureTestingModule({
      providers: [{ provide: LiveStore, useValue: { modifierOf: () => modifier() } }]
    });
    TestBed.overrideComponent(ModifierDialog, { set: { template: '', imports: [] } });
    const fixture = TestBed.createComponent(ModifierDialog);
    fixture.componentRef.setInput('block', 'delay1');
    fixture.componentRef.setInput('param', param);
    fixture.detectChanges();
    const dialog = fixture.componentInstance as unknown as {
      open: { set(value: boolean): void };
      draft: () => PresetModifier;
      patch(change: Partial<PresetModifier>): void;
    };
    return { fixture, dialog, modifier };
  }

  it('is seeded from the stored modifier when the dialog opens', () => {
    const { fixture, dialog } = render();
    dialog.open.set(true);
    fixture.detectChanges();
    expect(dialog.draft().min).toBe(10);
  });

  it('keeps the edits when the rig state changes while it is open', () => {
    const { fixture, dialog, modifier } = render();
    dialog.open.set(true);
    fixture.detectChanges();
    dialog.patch({ min: 33 });

    // A rig event: the store adopts a new state, so the modifier is a new object (here with another value too).
    modifier.set(stored(12));
    fixture.detectChanges();
    expect(dialog.draft().min).toBe(33);

    // Reopening starts from what is stored again.
    dialog.open.set(false);
    fixture.detectChanges();
    dialog.open.set(true);
    fixture.detectChanges();
    expect(dialog.draft().min).toBe(12);
  });
});
