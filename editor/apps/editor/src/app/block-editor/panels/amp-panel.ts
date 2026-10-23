import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import type { PresetBlock } from '@cathode/api-client';
import { XuiTagImports } from '@xui/tag';
import { LibraryStore } from '../../state/library.store';
import { LiveStore } from '../../state/live.store';
import { AssetPicker } from './asset-picker';

/** The capture amp: which NAM or AIDA-X model it runs. The tone stack and levels are ordinary knobs below. */
@Component({
  selector: 'cth-amp-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AssetPicker, XuiTagImports],
  template: `
    <div class="flex flex-wrap items-end gap-3">
      <cth-asset-picker
        label="Capture"
        accept=".nam,.aidax,.json"
        [assets]="library.models()"
        [reference]="block().assets['model']"
        (picked)="live.setAsset(block().id, 'model', $event.ref)"
      />
      @if (model(); as m) {
        <xui-tag minimal [color]="m.kind === 'nam' ? 'primary' : 'success'">
          {{ m.kind === 'nam' ? 'NAM' : 'AIDA-X' }}
        </xui-tag>
      }
    </div>
  `
})
export class AmpPanel {
  protected readonly live = inject(LiveStore);
  protected readonly library = inject(LibraryStore);

  readonly block = input.required<PresetBlock>();

  protected readonly model = computed(() => this.library.models().find(m => m.ref === this.block().assets['model']));
}
