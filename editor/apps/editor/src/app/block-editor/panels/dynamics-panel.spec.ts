import { TestBed } from '@angular/core/testing';
import type { PresetBlock } from '@cathode/api-client';
import { EventStream } from '../../core/events';
import { DynamicsPanel, reductionMeters } from './dynamics-panel';

describe('reductionMeters', () => {
  it('gives the Compressor one meter on a 24 dB scale', () => {
    expect(reductionMeters('compressor')).toEqual([
      { id: 'gainReduction', caption: 'GR', label: 'Gain reduction', range: 24 }
    ]);
  });

  it('gives the Multiband Compressor one meter per band, low to high', () => {
    expect(reductionMeters('mbcomp').map(m => [m.id, m.caption])).toEqual([
      ['lowReduction', 'Low'],
      ['midReduction', 'Mid'],
      ['highReduction', 'High']
    ]);
  });

  it('gives the gate a deeper scale and other blocks none', () => {
    expect(reductionMeters('gate')[0]?.range).toBe(60);
    expect(reductionMeters('delay')).toEqual([]);
  });
});

describe('DynamicsPanel', () => {
  it('reads each meter of its block from telemetry, 0 before any arrives', () => {
    const blockMeter = vi.fn((block: string, meter: string) =>
      block === 'mbcomp1' && meter === 'midReduction' ? 4.5 : undefined
    );
    TestBed.configureTestingModule({ providers: [{ provide: EventStream, useValue: { blockMeter } }] });
    const fixture = TestBed.createComponent(DynamicsPanel);
    const block: PresetBlock = {
      id: 'mbcomp1',
      type: 'mbcomp',
      row: 0,
      column: 0,
      assets: {},
      channels: {},
      bypassMode: 'thru',
      sceneIgnore: []
    };
    fixture.componentRef.setInput('block', block);
    fixture.detectChanges();

    const panel = fixture.componentInstance as unknown as { sources: () => (() => number)[] };
    expect(panel.sources().map(read => read())).toEqual([0, 4.5, 0]);
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('cth-reduction-meter')).toHaveLength(3);
  });
});
