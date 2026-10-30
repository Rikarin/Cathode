import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import type { PresetBlock } from '@cathode/api-client';
import { CthReductionMeter, CthTransferChart, type TransferPoint, blockAccent } from '@cathode/audio-ui';
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

/** One gain-reduction meter of a dynamics block: the telemetry meter it reads, its caption and its scale (dB). */
export interface ReductionMeter {
  id: string;
  caption: string;
  label: string;
  range: number;
}

/**
 * The gain-reduction meters a block type reports (its own telemetry meters, positive dB), or none: one for the
 * Compressor, one per band for the Multiband Compressor, and one on a deeper scale for the Gate/Expander, which closes
 * by tens of dB.
 */
export function reductionMeters(type: string): ReductionMeter[] {
  switch (type) {
    case 'compressor':
      return [{ id: 'gainReduction', caption: 'GR', label: 'Gain reduction', range: 24 }];
    case 'mbcomp':
      return (['Low', 'Mid', 'High'] as const).map(band => ({
        id: `${band.toLowerCase()}Reduction`,
        caption: band,
        label: `${band} band gain reduction`,
        range: 24
      }));
    case 'gate':
      return [{ id: 'gainReduction', caption: 'GR', label: 'Gate gain reduction', range: 60 }];
    default:
      return [];
  }
}

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
 * Gain reduction for the dynamics blocks: how far the Compressor, each band of the Multiband Compressor or the
 * Gate/Expander is pulling the level down right now, from the block's own telemetry meters. Each meter grows down from
 * 0 dB, holds its deepest reading for a second and shows it as a number; a bypassed block reads 0.
 *
 * The Compressor also gets its transfer curve beside the meter: output against input level, drawn by the engine's gain
 * computer ported to `compressor-curve.ts`, with the values its knobs hold right now (a drag's included), and a dot at
 * the block's `detector` and `output` meters, where the signal is on it.
 */
@Component({
  selector: 'cth-dynamics-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CthReductionMeter, CthTransferChart],
  template: `
    @if (meters().length > 0) {
      <section
        class="bg-surface-sunken/40 border-border flex w-fit items-stretch gap-4 rounded-xl border px-4 py-3"
        aria-label="Gain reduction"
        data-testid="reduction-meters"
      >
        @for (meter of meters(); track meter.id) {
          <div class="flex flex-col items-center gap-1">
            <span class="text-foreground-muted text-xs">{{ meter.caption }}</span>
            <cth-reduction-meter
              [class]="transfer() ? 'min-h-32 flex-1' : 'h-32'"
              [label]="meter.label"
              [range]="meter.range"
              [source]="sources()[$index] ?? idle"
            />
          </div>
        }
        @if (transfer(); as transfer) {
          <div class="border-border flex flex-col items-center gap-1 border-s ps-4" data-testid="transfer">
            <span class="text-foreground-muted text-xs">Curve</span>
            <cth-transfer-chart
              [curve]="transfer.output"
              [source]="dot()"
              [label]="transfer.label"
              [accent]="accent()"
              [size]="144"
            />
          </div>
        }
      </section>
    }
  `
})
export class DynamicsPanel {
  private readonly events = inject(EventStream);
  private readonly live = inject(LiveStore);
  private readonly catalog = inject(CatalogStore);

  readonly block = input.required<PresetBlock>();

  protected readonly meters = computed(() => reductionMeters(this.block().type));
  /** One reader per meter, made once per block rather than on every change detection. */
  protected readonly sources = computed(() => {
    const id = this.block().id;
    return this.meters().map(meter => () => this.events.blockMeter(id, meter.id) ?? 0);
  });

  /**
   * The Compressor's curve for the values its knobs hold now (a modifier's live value, a pending drag, the stored
   * value, else the default), so it moves with a knob as it turns; null on the other blocks, or before the catalog is
   * in.
   */
  protected readonly transfer = computed(() => {
    const block = this.block();
    const params = block.type === 'compressor' ? (this.catalog.info(block.type)?.params ?? []) : [];
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

  /** Where the signal sits on the curve, from the block's telemetry; null before any arrives. */
  protected readonly dot = computed(() => {
    const id = this.block().id;
    return (): TransferPoint | null => {
      const level = this.events.blockMeter(id, 'detector');
      const output = this.events.blockMeter(id, 'output');
      return level === undefined || output === undefined ? null : { input: level, output };
    };
  });

  /** The curve's colour: the block category's accent, as on its tile. */
  protected readonly accent = computed(() => {
    const category = this.catalog.info(this.block().type)?.category;
    return (category ? blockAccent(category) : null) ?? 'var(--color-primary)';
  });

  protected readonly idle = () => 0;
}
