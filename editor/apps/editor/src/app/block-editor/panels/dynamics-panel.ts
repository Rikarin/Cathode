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
 * Gain reduction for the Multiband Compressor and the Gate/Expander, as a tile at the right of their Basic page, two
 * knob rows high: how far each band, or the gate, is pulling the level down right now, from the block's own telemetry
 * meters. Each meter grows down from 0 dB over the tile's height, holds its deepest reading for a second and shows it
 * as a number; a bypassed block reads 0. Several meters are named (Low, Mid, High); a single one needs no name.
 *
 * The Compressor shows its meter with its transfer curve (compressor-curve-tile.ts). The tile's content is laid over
 * its grid area rather than in it, so it never sets the rows' height itself.
 */
@Component({
  selector: 'cth-dynamics-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CthReductionMeter],
  host: { class: 'relative block min-h-48 self-stretch justify-self-stretch' },
  template: `
    @if (meters().length > 0) {
      <div
        class="absolute inset-0 flex justify-center gap-3"
        role="group"
        aria-label="Gain reduction"
        data-testid="reduction-meters"
      >
        @for (meter of meters(); track meter.id) {
          <div class="flex h-full flex-col items-center gap-1">
            <cth-reduction-meter
              class="min-h-0 flex-1"
              [label]="meter.label"
              [range]="meter.range"
              [source]="sources()[$index] ?? idle"
            />
            @if (meters().length > 1) {
              <span class="text-foreground-muted text-xs">{{ meter.caption }}</span>
            }
          </div>
        }
      </div>
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
