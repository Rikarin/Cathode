import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import type { PresetBlock } from '@cathode/api-client';
import { LibraryStore } from '../../state/library.store';
import { LiveStore } from '../../state/live.store';
import { AssetPicker } from './asset-picker';

/** The IR Player's impulse response: a body, a room or any IR that is not a cab. */
@Component({
  selector: 'cth-ir-player-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AssetPicker],
  template: `
    <cth-asset-picker
      label="Impulse response"
      accept=".wav"
      [assets]="library.impulseResponses()"
      [reference]="block().assets['ir']"
      (picked)="live.setAsset(block().id, 'ir', $event.ref)"
    />
  `
})
export class IrPlayerPanel {
  protected readonly live = inject(LiveStore);
  protected readonly library = inject(LibraryStore);

  readonly block = input.required<PresetBlock>();
}
