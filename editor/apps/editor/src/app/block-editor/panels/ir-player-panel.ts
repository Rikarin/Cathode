import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import type { PresetBlock } from '@cathode/api-client';
import { LibraryStore } from '../../state/library.store';
import { LiveStore } from '../../state/live.store';
import { AssetPicker } from './asset-picker';

/** The IR Player's two impulse responses (bodies, rooms, any IR that is not a cab), combined by its Mode. */
@Component({
  selector: 'cth-ir-player-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AssetPicker],
  template: `
    <div class="flex flex-wrap gap-4">
      @for (slot of slots; track slot.asset) {
        <cth-asset-picker
          [label]="slot.label"
          accept=".wav"
          [assets]="library.impulseResponses()"
          [reference]="block().assets[slot.asset]"
          (picked)="live.setAsset(block().id, slot.asset, $event.ref)"
        />
      }
    </div>
  `
})
export class IrPlayerPanel {
  protected readonly live = inject(LiveStore);
  protected readonly library = inject(LibraryStore);

  /** The engine's asset slots, `ir` and `ir2`. */
  protected readonly slots = [
    { asset: 'ir', label: 'Impulse response 1' },
    { asset: 'ir2', label: 'Impulse response 2' }
  ];

  readonly block = input.required<PresetBlock>();
}
