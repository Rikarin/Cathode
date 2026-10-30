import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  input,
  signal
} from '@angular/core';
import type { PresetBlock } from '@cathode/api-client';
import { CthReductionMeter, CthTransferChart, blockAccent } from '@cathode/audio-ui';
import { EventStream } from '../../core/events';
import { CatalogStore } from '../../state/catalog.store';
import { LiveStore } from '../../state/live.store';
import { blockDefault } from '../type-labels';
import {
  type CompressorCurve,
  CompressorCurveParams,
  type CompressorValues,
  compressorCurve,
  compressorOutput
} from './compressor-curve';
import { reductionMeters } from './dynamics-panel';

/**
 * What the tile spends besides the plot, px: across, the chart's level scale (20 and a 4 gap), the gap to the meter
 * and the meter with its scale and readout; down, the chart's input scale under the plot (a 4 gap and 10).
 */
const Beside = { across: 20 + 4 + 8 + 40, down: 4 + 10 } as const;
/** The smallest plot, px, should the tile be given less room than that. */
const MinPlot = 96;

/** A ratio for people: "4:1", "2.5:1", "∞:1" at the top of the range. */
export function formatRatio(ratio: number): string {
  return ratio >= 100 ? '∞:1' : `${Number(ratio.toFixed(ratio < 10 ? 1 : 0))}:1`;
}

/** What the transfer chart says to a screen reader: the settings that shape the curve. */
export function describeCurve(curve: CompressorCurve): string {
  const db = (value: number) => `${Number(value.toFixed(1))} dB`;
  return (
    `Transfer curve: threshold ${db(curve.threshold)}, ratio ${formatRatio(curve.ratio)}, knee ${db(curve.knee)}, ` +
    `makeup ${db(curve.makeup)}`
  );
}

/**
 * The Compressor's transfer curve, as a tile three knobs wide and two high at the right of its Basic page: output
 * against input level, drawn by the engine's gain computer ported to `compressor-curve.ts` with the values its knobs
 * hold right now (a drag's included), and a dot riding the curve at the block's `detector` meter, where the signal is
 * on it. Its gain-reduction meter stands to the right, as tall as the chart.
 *
 * The tile fills the grid area it is given, three knob columns by two rows: it measures that area and makes the plot
 * the largest square that fits, centred. Its content is laid over the area rather than in it, so it never sets the
 * rows' height itself and cannot feed back on its own size.
 */
@Component({
  selector: 'cth-compressor-curve-tile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CthReductionMeter, CthTransferChart],
  host: { class: 'relative block min-h-48 self-stretch justify-self-stretch', 'data-testid': 'transfer' },
  template: `
    @if (transfer(); as transfer) {
      <div class="absolute inset-0 flex items-center justify-center gap-2">
        <cth-transfer-chart
          [curve]="transfer.output"
          [source]="level()"
          [label]="transfer.label"
          [accent]="accent()"
          [size]="plot().width"
          [height]="plot().height"
        />
        <!-- As tall as the chart with its input scale under the plot. -->
        <cth-reduction-meter
          data-testid="reduction-meters"
          [style.height.px]="plot().height + down"
          [label]="meter.label"
          [range]="meter.range"
          [source]="reduction()"
        />
      </div>
    }
  `
})
export class CompressorCurveTile {
  private readonly events = inject(EventStream);
  private readonly live = inject(LiveStore);
  private readonly catalog = inject(CatalogStore);

  readonly block = input.required<PresetBlock>();

  private readonly area = signal({ width: 0, height: 0 });
  protected readonly down = Beside.down;
  /**
   * The plot's side, px: square (the same dB per pixel both ways, so a 1:1 line is the diagonal), as large as fits the
   * area the grid gives the tile, less the scales and the meter.
   */
  protected readonly plot = computed(() => {
    const { width, height } = this.area();
    const side = Math.max(MinPlot, Math.floor(Math.min(width - Beside.across, height - Beside.down)));
    return { width: side, height: side };
  });
  protected readonly meter = reductionMeters('compressor')[0] ?? {
    id: 'gainReduction',
    caption: 'GR',
    label: 'Gain reduction',
    range: 24
  };

  /**
   * The curve for the values the Compressor's knobs hold now (a modifier's live value, a pending drag, the stored
   * value, else the default), so it moves with a knob as it turns; null before the catalog is in.
   */
  protected readonly transfer = computed(() => {
    const block = this.block();
    const params = this.catalog.info(block.type)?.params ?? [];
    if (params.length === 0) {
      return null;
    }

    const channel = this.live.channelOf(block.id);
    const stored = (id: string) => this.live.valueOf(block, channel, id);
    const value = (id: string) => {
      const param = params.find(p => p.id === id);
      return (
        this.events.modulatedValue(block.id, id) ?? stored(id) ?? (param ? blockDefault(params, param, stored) : 0)
      );
    };
    const values = Object.fromEntries(CompressorCurveParams.map(id => [id, value(id)])) as CompressorValues;
    const curve = compressorCurve(values);
    return { output: (level: number) => compressorOutput(curve, level), label: describeCurve(curve) };
  });

  /** The level the gain computer hears, from the block's telemetry: where the dot sits; null before any arrives. */
  protected readonly level = computed(() => {
    const id = this.block().id;
    return (): number | null => this.events.blockMeter(id, 'detector') ?? null;
  });

  /** How far the Compressor pulls the level down now, dB (0 or more), from its telemetry; 0 before any arrives. */
  protected readonly reduction = computed(() => {
    const id = this.block().id;
    return () => this.events.blockMeter(id, this.meter.id) ?? 0;
  });

  constructor() {
    const host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    const observer = new ResizeObserver(([entry]) => {
      if (entry) {
        this.area.set({ width: entry.contentRect.width, height: entry.contentRect.height });
      }
    });
    afterNextRender(() => observer.observe(host));
    inject(DestroyRef).onDestroy(() => observer.disconnect());
  }

  /** The curve's colour: the block category's accent, as on its tile. */
  protected readonly accent = computed(() => {
    const category = this.catalog.info(this.block().type)?.category;
    return (category ? blockAccent(category) : null) ?? 'var(--color-primary)';
  });
}
