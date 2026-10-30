import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { BlockTypeInfo, PresetBlock } from '@cathode/api-client';
import { EventStream } from '../../core/events';
import { CatalogStore } from '../../state/catalog.store';
import { LiveStore } from '../../state/live.store';
import { CompressorCurveTile, describeCurve, formatRatio } from './compressor-curve-tile';

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

const compressor: PresetBlock = {
  id: 'compressor1',
  type: 'compressor',
  row: 0,
  column: 0,
  assets: {},
  channels: {},
  bypassMode: 'thru',
  sceneIgnore: []
};

function render(meters: Record<string, number> = {}) {
  const pending = signal(new Map<string, number>());
  const blockMeter = vi.fn((id: string, meter: string) => (id === compressor.id ? meters[meter] : undefined));
  TestBed.configureTestingModule({
    providers: [
      { provide: EventStream, useValue: { blockMeter, modulatedValue: () => undefined } },
      {
        provide: LiveStore,
        useValue: { channelOf: () => 'A', valueOf: (_: PresetBlock, __: string, id: string) => pending().get(id) }
      },
      { provide: CatalogStore, useValue: { info: () => CompressorInfo } }
    ]
  });
  const fixture = TestBed.createComponent(CompressorCurveTile);
  fixture.componentRef.setInput('block', compressor);
  fixture.detectChanges();
  const tile = fixture.componentInstance as unknown as {
    transfer: () => { output: (level: number) => number; label: string } | null;
    level: () => () => number | null;
    reduction: () => () => number;
  };
  return { fixture, tile, pending, host: fixture.nativeElement as HTMLElement };
}

describe('CompressorCurveTile', () => {
  beforeAll(() => {
    // jsdom has no ResizeObserver; the tile only needs one to exist.
    if (!('ResizeObserver' in globalThis)) {
      globalThis.ResizeObserver = class {
        observe = () => undefined;
        disconnect = () => undefined;
      } as unknown as typeof ResizeObserver;
    }
  });

  it('draws the curve from its defaults and follows a knob while it is dragged', () => {
    const { fixture, tile, pending, host } = render();
    expect(host.querySelector('cth-transfer-chart')).not.toBeNull();
    expect(tile.transfer()?.output(0)).toBeCloseTo(-20 + 20 / 4, 6); // -20 dB at 4:1
    expect(tile.transfer()?.output(-40)).toBeCloseTo(-40, 6);

    pending.set(new Map([['ratio', 100]])); // a drag to ∞:1, not yet stored
    fixture.detectChanges();
    expect(tile.transfer()?.output(0)).toBeCloseTo(-20, 6);
    expect(tile.transfer()?.label).toContain('∞:1');
  });

  it('shows the gain reduction beside the curve, 0 before any arrives', () => {
    expect(render().tile.reduction()()).toBe(0);
    TestBed.resetTestingModule();

    const { tile, host } = render({ gainReduction: 6.5 });
    expect(tile.reduction()()).toBe(6.5);
    expect(host.querySelector('cth-reduction-meter')).not.toBeNull();
  });

  it('puts the dot at the detector meter, nowhere before it arrives', () => {
    expect(render().tile.level()()).toBeNull();
    TestBed.resetTestingModule();

    expect(render({ detector: -12 }).tile.level()()).toBe(-12);
  });
});
