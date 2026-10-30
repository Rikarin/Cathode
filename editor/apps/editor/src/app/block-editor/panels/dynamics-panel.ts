import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import type { PresetBlock } from '@cathode/api-client';
import { CthReductionMeter } from '@cathode/audio-ui';
import { EventStream } from '../../core/events';

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

/**
 * Gain reduction for the dynamics blocks: how far the Compressor, each band of the Multiband Compressor or the
 * Gate/Expander is pulling the level down right now, from the block's own telemetry meters. Each meter grows down from
 * 0 dB, holds its deepest reading for a second and shows it as a number; a bypassed block reads 0.
 *
 * The Compressor shows its meter with its transfer curve, on its Basic page among the knobs (compressor-curve-tile.ts).
 */
@Component({
  selector: 'cth-dynamics-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CthReductionMeter],
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
              class="h-32"
              [label]="meter.label"
              [range]="meter.range"
              [source]="sources()[$index] ?? idle"
            />
          </div>
        }
      </section>
    }
  `
})
export class DynamicsPanel {
  private readonly events = inject(EventStream);

  readonly block = input.required<PresetBlock>();

  protected readonly meters = computed(() => reductionMeters(this.block().type));
  /** One reader per meter, made once per block rather than on every change detection. */
  protected readonly sources = computed(() => {
    const id = this.block().id;
    return this.meters().map(meter => () => this.events.blockMeter(id, meter.id) ?? 0);
  });

  protected readonly idle = () => 0;
}
