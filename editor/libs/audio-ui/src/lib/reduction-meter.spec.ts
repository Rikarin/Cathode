import { TestBed } from '@angular/core/testing';
import {
  CthReductionMeter,
  type ReductionState,
  formatReduction,
  reductionFraction,
  reductionTicks,
  stepReduction
} from './reduction-meter';

describe('reduction meter mapping', () => {
  it('grows down from 0 dB to the bottom of its range', () => {
    expect(reductionFraction(0, 24)).toBe(0);
    expect(reductionFraction(6, 24)).toBe(0.25);
    expect(reductionFraction(24, 24)).toBe(1);
    expect(reductionFraction(40, 24)).toBe(1); // pegged, never past the end
    expect(reductionFraction(30, 60)).toBe(0.5);
  });

  it('reads a boost, a stray negative or a missing value as no reduction', () => {
    expect(reductionFraction(-3, 24)).toBe(0);
    expect(reductionFraction(Number.NaN, 24)).toBe(0);
    expect(reductionFraction(Number.POSITIVE_INFINITY, 24)).toBe(0);
    expect(reductionFraction(6, 0)).toBe(0);
  });

  it('marks every 6 dB, or every 12 dB on a deeper scale', () => {
    expect(reductionTicks(24)).toEqual([0, 6, 12, 18, 24]);
    expect(reductionTicks(60)).toEqual([0, 12, 24, 36, 48, 60]);
  });

  it('formats the reduction as a cut', () => {
    expect(formatReduction(0)).toBe('0.0 dB');
    expect(formatReduction(0.04)).toBe('0.0 dB');
    expect(formatReduction(4.25)).toBe('-4.3 dB');
    expect(formatReduction(-2)).toBe('0.0 dB');
  });
});

describe('reduction meter ballistics', () => {
  const rest: ReductionState = { level: 0, peak: 0, peakAt: 0 };

  it('follows the reading and holds the deepest reduction for a second', () => {
    let state = stepReduction(rest, 9, 100);
    expect(state).toEqual({ level: 9, peak: 9, peakAt: 100 });

    state = stepReduction(state, 3, 600);
    expect(state).toEqual({ level: 3, peak: 9, peakAt: 100 }); // the bar recovers; the mark holds

    state = stepReduction(state, 2, 1_101);
    expect(state).toEqual({ level: 2, peak: 2, peakAt: 1_101 }); // the hold is over: the mark falls to the bar
  });

  it('moves the mark at once to a deeper reduction', () => {
    const state = stepReduction({ level: 4, peak: 6, peakAt: 0 }, 8, 200);
    expect(state).toEqual({ level: 8, peak: 8, peakAt: 200 });
  });

  it('treats a missing reading as none', () => {
    expect(stepReduction(rest, Number.NaN, 0).level).toBe(0);
  });
});

describe('CthReductionMeter', () => {
  it('is a labelled meter over its range, at rest before any reading', () => {
    const fixture = TestBed.createComponent(CthReductionMeter);
    fixture.componentRef.setInput('source', () => 0);
    fixture.componentRef.setInput('label', 'Low band gain reduction');
    fixture.componentRef.setInput('range', 60);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const meter = host.querySelector('[role="meter"]');
    expect(meter?.getAttribute('aria-label')).toBe('Low band gain reduction');
    expect(meter?.getAttribute('aria-valuemax')).toBe('60');
    expect(meter?.getAttribute('aria-valuetext')).toBe('0.0 dB');
    expect(host.textContent).toContain('-48');
  });
});
