import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { ParameterInfo, PresetModifier } from '@cathode/api-client';
import { LiveStore } from '../state/live.store';
import { ModifierDialog, engagedWhen, respond, shape, sparseEngage, sparseResponse } from './modifier-dialog';

describe('engage settings', () => {
  const engage: PresetModifier = {
    block: 'wah1',
    param: 'engage',
    source: 'ext1',
    min: 0,
    max: 1,
    curve: 'linear',
    attackMs: 0,
    releaseMs: 0
  };

  it('describe the defaults as the plain switch at 50 %', () => {
    expect(engagedWhen({})).toBe('at 50 % and above');
    expect(engagedWhen({ threshold: 0.2, offDelayMs: 300 })).toBe('at 20 % and above, off after 300 ms below it');
    expect(engagedWhen({ mode: 'moving', threshold: 0.05, offDelayMs: 250 })).toBe(
      'as soon as it moves, off after 250 ms below 5 %'
    );
    expect(engagedWhen({ mode: 'moving', threshold: 0.05 })).toBe('as soon as it moves, off once back below 5 %');
  });

  it('are stored only when they differ from the defaults', () => {
    expect(sparseEngage({ ...engage, mode: 'position', threshold: 0.5, offDelayMs: 0 })).toEqual(engage);
    expect(sparseEngage({ ...engage, mode: 'moving', threshold: 0.05, offDelayMs: 300 })).toEqual({
      ...engage,
      mode: 'moving',
      threshold: 0.05,
      offDelayMs: 300
    });
  });
});

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

describe('modifier response (as the engine applies it)', () => {
  const plain: PresetModifier = {
    block: 'delay1',
    param: 'mix',
    source: 'ext1',
    min: 0,
    max: 100,
    curve: 'linear',
    attackMs: 0,
    releaseMs: 0
  };

  it('leaves the source alone at its defaults', () => {
    for (const s of [0, 0.2, 0.5, 0.9, 1]) {
      expect(respond({}, s)).toBeCloseTo(s, 6);
    }
  });

  it('follows Start, Mid and End, then Scale and Offset, within 0-1', () => {
    expect(respond({ start: 1, end: 0 }, 0)).toBeCloseTo(1, 6);
    expect(respond({ start: 1, end: 0 }, 1)).toBeCloseTo(0, 6);
    expect(respond({ mid: 0 }, 0.4)).toBeCloseTo(0, 6);
    expect(respond({ mid: 0 }, 0.75)).toBeCloseTo(0.5, 6);
    expect(respond({ scale: 2 }, 0.25)).toBeCloseTo(0.5, 6);
    expect(respond({ scale: 2 }, 0.9)).toBe(1);
    expect(respond({ scale: 0.5, offset: 0.25 }, 1)).toBeCloseTo(0.75, 6);
  });

  it('bends around the centre with Slope', () => {
    expect(respond({ slope: 0.9 }, 0.5)).toBeCloseTo(0.5, 6);
    expect(respond({ slope: 0.9 }, 0.55)).toBeGreaterThan(0.6);
    expect(respond({ slope: 0.1 }, 0.55)).toBeLessThan(0.53);
    expect(respond({ slope: 0.1 }, 1)).toBeCloseTo(1, 6);
  });

  it('is stored only where it differs from the defaults', () => {
    expect(
      sparseResponse({
        ...plain,
        source2: null,
        operation: 'multiply',
        start: 0,
        mid: 0.5,
        end: 1,
        slope: 0.5,
        scale: 1,
        offset: 0
      })
    ).toEqual(plain);
    expect(sparseResponse({ ...plain, source2: 'lfo1', operation: 'multiply', start: 1, end: 0 })).toEqual({
      ...plain,
      source2: 'lfo1',
      operation: 'multiply',
      start: 1,
      end: 0
    });
    expect(sparseResponse({ ...plain, source2: 'lfo1', operation: 'add' })).toEqual({ ...plain, source2: 'lfo1' });
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
