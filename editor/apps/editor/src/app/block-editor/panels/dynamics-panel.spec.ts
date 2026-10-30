import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { BlockTypeInfo, PresetBlock } from '@cathode/api-client';
import type { TransferPoint } from '@cathode/audio-ui';
import { EventStream } from '../../core/events';
import { CatalogStore } from '../../state/catalog.store';
import { LiveStore } from '../../state/live.store';
import { DynamicsPanel, describeCurve, formatRatio, reductionMeters } from './dynamics-panel';

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

describe('the curve description', () => {
  it('names the ratio, ∞:1 at the top of the range', () => {
    expect(formatRatio(4)).toBe('4:1');
    expect(formatRatio(2.54)).toBe('2.5:1');
    expect(formatRatio(20)).toBe('20:1');
    expect(formatRatio(100)).toBe('∞:1');
    expect(
      describeCurve({
        type: 0,
        threshold: -20,
        ratio: 4,
        knee: 6,
        makeup: 3.25,
        expanderThreshold: -60,
        expanderSlope: 1
      })
    ).toBe('Transfer curve: threshold -20 dB, ratio 4:1, knee 6 dB, makeup 3.3 dB');
  });
});

/** The Compressor's parameters the curve reads, with the engine's defaults. */
const CompressorInfo = {
  id: 'compressor',
  name: 'Compressor',
  category: 'Dynamics',
  params: [
    ['type', 0],
    ['threshold', -20],
    ['ratio', 4],
    ['knee', 6],
    ['makeup', 0],
    ['autoMakeup', 0],
    ['inputLevel', 0],
    ['controls', 0],
    ['compression', 50],
    ['dynamics', 50],
    ['expThreshold', -60],
    ['expRatio', 2]
  ].map(([id, value]) => ({ id, default: value }))
} as unknown as BlockTypeInfo;

function block(id: string, type: string): PresetBlock {
  return { id, type, row: 0, column: 0, assets: {}, channels: {}, bypassMode: 'thru', sceneIgnore: [] };
}

function render(target: PresetBlock, meters: Record<string, number> = {}) {
  const pending = signal(new Map<string, number>());
  const blockMeter = vi.fn((id: string, meter: string) => (id === target.id ? meters[meter] : undefined));
  TestBed.configureTestingModule({
    providers: [
      { provide: EventStream, useValue: { blockMeter, modulatedValue: () => undefined } },
      {
        provide: LiveStore,
        useValue: { channelOf: () => 'A', valueOf: (_: PresetBlock, __: string, id: string) => pending().get(id) }
      },
      {
        provide: CatalogStore,
        useValue: { info: (type: string) => (type === 'compressor' ? CompressorInfo : undefined) }
      }
    ]
  });
  const fixture = TestBed.createComponent(DynamicsPanel);
  fixture.componentRef.setInput('block', target);
  fixture.detectChanges();
  const panel = fixture.componentInstance as unknown as {
    sources: () => (() => number)[];
    transfer: () => { output: (level: number) => number; label: string } | null;
    dot: () => () => TransferPoint | null;
  };
  return { fixture, panel, pending, host: fixture.nativeElement as HTMLElement };
}

describe('DynamicsPanel', () => {
  it('reads each meter of its block from telemetry, 0 before any arrives', () => {
    const { panel, host } = render(block('mbcomp1', 'mbcomp'), { midReduction: 4.5 });

    expect(panel.sources().map(read => read())).toEqual([0, 4.5, 0]);
    expect(host.querySelectorAll('cth-reduction-meter')).toHaveLength(3);
    expect(host.querySelector('cth-transfer-chart')).toBeNull();
    expect(panel.transfer()).toBeNull();
  });

  it('draws the Compressor curve from its defaults and follows a knob while it is dragged', () => {
    const { fixture, panel, pending, host } = render(block('compressor1', 'compressor'));
    expect(host.querySelector('cth-transfer-chart')).not.toBeNull();
    expect(panel.transfer()?.output(0)).toBeCloseTo(-20 + 20 / 4, 6); // -20 dB at 4:1
    expect(panel.transfer()?.output(-40)).toBeCloseTo(-40, 6);

    pending.set(new Map([['ratio', 100]])); // a drag to ∞:1, not yet stored
    fixture.detectChanges();
    expect(panel.transfer()?.output(0)).toBeCloseTo(-20, 6);
    expect(panel.transfer()?.label).toContain('∞:1');
    expect(host.querySelector('[data-testid="transfer-curve"]')?.getAttribute('d')).toMatch(/L144\.00 48\.00$/);
  });

  it('puts the dot at the detector and output meters, nowhere before they arrive', () => {
    expect(render(block('compressor1', 'compressor')).panel.dot()()).toBeNull();
    TestBed.resetTestingModule();

    const { panel } = render(block('compressor1', 'compressor'), { detector: -12, output: -18 });
    expect(panel.dot()()).toEqual({ input: -12, output: -18 });
  });
});
