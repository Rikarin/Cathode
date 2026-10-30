import { TestBed } from '@angular/core/testing';
import {
  CthTransferChart,
  type TransferDot,
  stepTransferDot,
  transferFraction,
  transferPath,
  transferTicks
} from './transfer-chart';

/** The path's points, in px. */
function points(path: string): [number, number][] {
  return path.split(' L').map(part => {
    const [x, y] = part.replace('M', '').split(' ').map(Number);
    return [x ?? Number.NaN, y ?? Number.NaN];
  });
}

describe('transfer chart scale', () => {
  it('maps the range onto 0 to 1', () => {
    expect(transferFraction(-60, -60, 0)).toBe(0);
    expect(transferFraction(-30, -60, 0)).toBe(0.5);
    expect(transferFraction(0, -60, 0)).toBe(1);
    expect(transferFraction(6, -60, 0)).toBe(1.1);
    expect(transferFraction(-10, 0, 0)).toBe(0);
  });

  it('puts a grid line every step from the top down', () => {
    expect(transferTicks(-60, 0, 12)).toEqual([0, -12, -24, -36, -48, -60]);
    expect(transferTicks(-50, 0, 12)).toEqual([0, -12, -24, -36, -48]);
    expect(transferTicks(-60, 0, 0)).toEqual([]);
  });
});

describe('transferPath', () => {
  it('draws unity as the diagonal, input to the right and output up', () => {
    const drawn = points(transferPath(x => x, -60, 0, 120));
    expect(drawn[0]).toEqual([0, 120]);
    expect(drawn.at(-1)).toEqual([120, 0]);
    expect(drawn).toHaveLength(121); // a point every 0.5 dB
    for (const [x, y] of drawn) {
      expect(x + y).toBeCloseTo(120, 6);
    }
  });

  it('bends where the curve does', () => {
    // 4:1 over -24 dB: 0 dB in comes out at -18 dB.
    const drawn = points(transferPath(x => (x < -24 ? x : -24 + (x + 24) / 4), -60, 0, 120));
    expect(drawn.at(-1)).toEqual([120, 36]);
  });

  it('holds outputs far off the chart just past its edge', () => {
    const drawn = points(transferPath(x => (x < -30 ? -1e9 : Number.NaN), -60, 0, 100));
    expect(Math.max(...drawn.map(([, y]) => y))).toBe(200);
    expect(transferPath(x => x, 0, -60, 100)).toBe('');
  });
});

describe('the live dot', () => {
  const hidden: TransferDot = { input: 0, output: 0, visible: false };

  it('appears where the first reading puts it', () => {
    expect(stepTransferDot(hidden, { input: -20, output: -26 }, 16, false, -60, 0)).toEqual({
      input: -20,
      output: -26,
      visible: true
    });
  });

  it('eases toward a new reading, and jumps there with reduced motion', () => {
    const shown: TransferDot = { input: -40, output: -40, visible: true };
    const eased = stepTransferDot(shown, { input: -20, output: -26 }, 16, false, -60, 0);
    expect(eased.input).toBeGreaterThan(-40);
    expect(eased.input).toBeLessThan(-20);
    expect(eased.output).toBeLessThan(-26);
    expect(eased.output).toBeGreaterThan(-40);
    const later = stepTransferDot(eased, { input: -20, output: -26 }, 1_000, false, -60, 0);
    expect(later.input).toBeCloseTo(-20, 6);

    expect(stepTransferDot(shown, { input: -20, output: -26 }, 16, true, -60, 0)).toEqual({
      input: -20,
      output: -26,
      visible: true
    });
  });

  it('hides without a reading or with an input off the chart', () => {
    const shown: TransferDot = { input: -40, output: -40, visible: true };
    expect(stepTransferDot(shown, null, 16, false, -60, 0).visible).toBe(false);
    expect(stepTransferDot(shown, { input: -96, output: -96 }, 16, false, -60, 0).visible).toBe(false);
    expect(stepTransferDot(shown, { input: Number.NaN, output: 0 }, 16, false, -60, 0).visible).toBe(false);
  });
});

describe('CthTransferChart', () => {
  it('draws the curve, the unity line and its scale, and redraws when the curve changes', () => {
    const fixture = TestBed.createComponent(CthTransferChart);
    fixture.componentRef.setInput('curve', (x: number) => x);
    fixture.componentRef.setInput('label', 'Compressor transfer curve');
    fixture.componentRef.setInput('size', 120);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('svg')?.getAttribute('aria-label')).toBe('Compressor transfer curve');
    expect(host.querySelector('[data-testid="transfer-unity"]')).not.toBeNull();
    const curve = () => host.querySelector('[data-testid="transfer-curve"]')?.getAttribute('d') ?? '';
    expect(curve().endsWith('L120.00 0.00')).toBe(true);
    expect(host.textContent).toContain('-48');
    expect(host.querySelectorAll('line').length).toBe(1 + 2 * 4); // unity and the inner grid both ways

    fixture.componentRef.setInput('curve', (x: number) => Math.min(x, -24));
    fixture.detectChanges();
    expect(curve().endsWith('L120.00 48.00')).toBe(true);
  });
});
