import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { AssetInfo, PresetBlock } from '@cathode/api-client';
import { ConnectionService } from '../../core/connection';
import { EventStream } from '../../core/events';
import { Notify } from '../../core/notify';
import { CatalogStore } from '../../state/catalog.store';
import { LibraryStore } from '../../state/library.store';
import { LiveStore } from '../../state/live.store';
import { CabPanel } from './cab-panel';

/** A mono 48 kHz 16-bit WAV holding a unit impulse. */
function impulse(): ArrayBuffer {
  const buffer = new ArrayBuffer(44 + 64 * 2);
  const view = new DataView(buffer);
  const text = (at: number, s: string) => {
    for (let i = 0; i < s.length; i++) {
      view.setUint8(at + i, s.charCodeAt(i));
    }
  };
  text(0, 'RIFF');
  view.setUint32(4, 36 + 128, true);
  text(8, 'WAVE');
  text(12, 'fmt ');
  view.setUint32(16, 16, true);
  view.setUint16(20, 1, true);
  view.setUint16(22, 1, true);
  view.setUint32(24, 48_000, true);
  view.setUint32(28, 96_000, true);
  view.setUint16(32, 2, true);
  view.setUint16(34, 16, true);
  text(36, 'data');
  view.setUint32(40, 128, true);
  view.setInt16(44, 32_767, true);
  return buffer;
}

describe('CabPanel response plot', () => {
  function render(reference: string) {
    const level = signal(0);
    const content = vi.fn(() => Promise.resolve(impulse()));
    TestBed.configureTestingModule({
      providers: [
        {
          provide: LiveStore,
          useValue: {
            channelOf: () => 'A',
            // Slot 1 plays an IR (source 0); its level follows the knob.
            valueOf: (_block: PresetBlock, _channel: string, id: string) => (id === 'm1level' ? level() : undefined)
          }
        },
        {
          provide: LibraryStore,
          useValue: {
            assets: signal<AssetInfo[]>([{ ref: reference, kind: 'ir', name: 'a.wav' } as AssetInfo]),
            content
          }
        },
        { provide: CatalogStore, useValue: { info: () => undefined } },
        { provide: EventStream, useValue: { blockMeter: () => undefined } },
        { provide: ConnectionService, useValue: {} },
        { provide: Notify, useValue: {} }
      ]
    });
    TestBed.overrideComponent(CabPanel, { set: { template: '', imports: [] } });
    const fixture = TestBed.createComponent(CabPanel);
    const block = (): PresetBlock => ({
      id: 'cab1',
      type: 'cab',
      row: 0,
      column: 0,
      assets: { slot1: reference },
      channels: {},
      bypassMode: 'thru',
      sceneIgnore: []
    });
    fixture.componentRef.setInput('block', block());
    fixture.detectChanges();
    const panel = fixture.componentInstance as unknown as { loading: () => boolean; plotted: () => boolean };
    return { fixture, panel, level, content, block };
  }

  it('measures the IR once and does not reload it while knobs move or the state is answered', async () => {
    const { fixture, panel, level, content, block } = render('ir-knob-moves');
    await vi.waitFor(() => expect(panel.loading()).toBe(false)); // decoding the file
    expect(content).toHaveBeenCalledTimes(1);
    expect(panel.plotted()).toBe(true);

    const loads: boolean[] = [];
    for (let move = 1; move <= 5; move++) {
      level.set(-move); // a knob drag
      fixture.componentRef.setInput('block', block()); // a state answer: a new block object, the same files
      fixture.detectChanges();
      loads.push(panel.loading());
    }

    await fixture.whenStable();
    expect(loads.every(loading => !loading)).toBe(true);
    expect(content).toHaveBeenCalledTimes(1);
  });
});
