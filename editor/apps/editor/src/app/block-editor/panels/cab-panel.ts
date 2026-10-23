import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import type { PresetBlock } from '@cathode/api-client';
import { XuiEChartImports } from '@xui/echarts';
import type { EChartsOption } from 'echarts';
import { decodeWav, impulseResponseDb, logFrequencies, sectionDb } from '../../dsp/response';
import { LibraryStore } from '../../state/library.store';
import { LiveStore } from '../../state/live.store';
import { AssetPicker } from './asset-picker';

const Frequencies = logFrequencies(160);

/** Decoded responses are cached per asset: an IR never changes under its hash. */
const responses = new Map<string, Promise<number[]>>();

/**
 * The cab's two IR slots and their frequency response: each IR measured from the file, with its level and the
 * block's low and high cut (12 dB/oct, as the engine's filters) applied.
 */
@Component({
  selector: 'cth-cab-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AssetPicker, XuiEChartImports],
  template: `
    <div class="flex flex-wrap items-start gap-6">
      <div class="flex flex-col gap-3">
        @for (slot of slots; track slot) {
          <cth-asset-picker
            [label]="slot === 'ir1' ? 'IR 1' : 'IR 2'"
            accept=".wav"
            [assets]="library.impulseResponses()"
            [reference]="block().assets[slot]"
            (picked)="live.setAsset(block().id, slot, $event.ref)"
          />
        }
      </div>
      <xui-echart
        class="h-56 min-w-96 flex-1"
        aria-label="Cab frequency response"
        [option]="option()"
        [loading]="loading()"
      />
    </div>
  `
})
export class CabPanel {
  protected readonly live = inject(LiveStore);
  protected readonly library = inject(LibraryStore);

  readonly block = input.required<PresetBlock>();

  protected readonly slots = ['ir1', 'ir2'] as const;
  protected readonly loading = signal(false);
  private readonly measured = signal<Partial<Record<'ir1' | 'ir2', number[]>>>({});

  protected readonly option = computed<EChartsOption>(() => {
    const block = this.block();
    const channel = this.live.channelOf(block.id);
    const value = (id: string, fallback: number) => this.live.valueOf(block, channel, id) ?? fallback;
    const lowCut = value('lowcut', 20);
    const highCut = value('highcut', 20_000);
    const cuts = Frequencies.map(
      f =>
        (lowCut > 20 ? sectionDb('highpass', lowCut, 0, 0.7071, f, 48_000) : 0) +
        (highCut < 20_000 ? sectionDb('lowpass', highCut, 0, 0.7071, f, 48_000) : 0)
    );
    const series = this.slots.flatMap((slot, i) => {
      const curve = this.measured()[slot];
      const level = value(i === 0 ? 'level1' : 'level2', 0);
      return curve
        ? [
            {
              type: 'line' as const,
              name: slot === 'ir1' ? 'IR 1' : 'IR 2',
              showSymbol: false,
              data: Frequencies.map((f, n) => [f, curve[n] + level + cuts[n]])
            }
          ]
        : [];
    });
    return {
      grid: { left: 48, right: 16, top: 24, bottom: 32 },
      legend: { show: series.length > 1, top: 0 },
      tooltip: { trigger: 'axis', valueFormatter: v => `${Number(v).toFixed(1)} dB` },
      xAxis: { type: 'log', min: 20, max: 20_000, name: 'Hz', nameGap: 4 },
      yAxis: { type: 'value', name: 'dB', min: -36, max: 18, interval: 6 },
      series
    };
  });

  constructor() {
    effect(() => {
      const assets = this.block().assets;
      void this.measure(assets['ir1'], assets['ir2']);
    });
  }

  private async measure(ir1: string | undefined, ir2: string | undefined): Promise<void> {
    this.loading.set(true);
    try {
      const [one, two] = await Promise.all([ir1, ir2].map(ref => (ref ? this.response(ref) : undefined)));
      this.measured.set({ ir1: one, ir2: two });
    } catch {
      this.measured.set({}); // an unreadable IR plots nothing; the engine reports real load failures
    } finally {
      this.loading.set(false);
    }
  }

  private response(reference: string): Promise<number[]> {
    let curve = responses.get(reference);
    if (!curve) {
      curve = this.library.content(reference).then(bytes => {
        const wav = decodeWav(bytes);
        const samples = wav.channels[0].subarray(0, Math.min(wav.channels[0].length, 8_192));
        // The engine normalises IRs to unit energy; do the same so curves compare.
        const energy = Math.sqrt(samples.reduce((sum, s) => sum + s * s, 0)) || 1;
        return impulseResponseDb(
          samples.map(s => s / energy),
          wav.sampleRate,
          Frequencies
        );
      });
      responses.set(reference, curve);
    }

    return curve;
  }
}
