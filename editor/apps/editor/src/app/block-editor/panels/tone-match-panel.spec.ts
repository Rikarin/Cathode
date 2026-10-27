import { type WritableSignal, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { AssetInfo, PresetBlock } from '@cathode/api-client';
import { LibraryStore } from '../../state/library.store';
import { LiveStore } from '../../state/live.store';
import {
  ToneMatchPanel,
  smoothingName,
  toneMatchAmounts,
  toneMatchRequest,
  toneMatchSmoothings
} from './tone-match-panel';

describe('tone match options', () => {
  it('offers only smoothing widths the Server accepts, named as fractions of an octave', () => {
    expect(toneMatchSmoothings.every(s => s >= 0.1 && s <= 1)).toBe(true);
    expect(toneMatchSmoothings.map(smoothingName)).toEqual([
      '1/6 octave',
      '1/4 octave',
      '1/3 octave',
      '1/2 octave',
      '1 octave'
    ]);
    expect(toneMatchAmounts.every(a => a >= 0 && a <= 100)).toBe(true);
  });

  it('clamps a request to the ranges of the contract', () => {
    expect(toneMatchRequest('sha256:a', 1, 150, 0.01)).toEqual({
      reference: 'sha256:a',
      seconds: 3,
      amount: 100,
      smoothing: 0.1
    });
    expect(toneMatchRequest('sha256:a', 20, 50, 1 / 2)).toEqual({
      reference: 'sha256:a',
      seconds: 20,
      amount: 50,
      smoothing: 0.5
    });
  });
});

describe('ToneMatchPanel', () => {
  it('sends the chosen amount and smoothing with the learn request', async () => {
    const post = vi.fn(() => Promise.resolve({}));
    TestBed.configureTestingModule({
      providers: [
        {
          provide: LiveStore,
          useValue: { mutate: (call: (client: { POST: typeof post }) => unknown) => call({ POST: post }) }
        },
        {
          provide: LibraryStore,
          useValue: { assets: signal<AssetInfo[]>([]), impulseResponses: signal([]), refreshAssets: vi.fn() }
        }
      ]
    });
    TestBed.overrideComponent(ToneMatchPanel, { set: { template: '', imports: [] } });
    const fixture = TestBed.createComponent(ToneMatchPanel);
    const block: PresetBlock = {
      id: 'tm1',
      type: 'tonematch',
      row: 0,
      column: 0,
      assets: {},
      channels: {},
      bypassMode: 'thru',
      sceneIgnore: []
    };
    fixture.componentRef.setInput('block', block);
    fixture.detectChanges();
    const panel = fixture.componentInstance as unknown as {
      reference: WritableSignal<AssetInfo | null>;
      duration: WritableSignal<number>;
      amount: WritableSignal<number>;
      smoothing: WritableSignal<number>;
      learn: () => Promise<void>;
    };

    expect(panel.amount()).toBe(100);
    expect(panel.smoothing()).toBeCloseTo(1 / 3);
    panel.reference.set({ ref: 'sha256:ref', kind: 'ir', name: 'ref.wav' } as AssetInfo);
    panel.duration.set(20);
    panel.amount.set(75);
    panel.smoothing.set(1);
    await panel.learn();

    expect(post).toHaveBeenCalledWith('/api/v1/live/blocks/{id}/tonematch', {
      params: { path: { id: 'tm1' } },
      body: { reference: 'sha256:ref', seconds: 20, amount: 75, smoothing: 1 }
    });
  });
});
