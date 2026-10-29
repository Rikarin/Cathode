import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { XuiSplitterImports } from '@xui/splitter';
import { BlockEditor } from '../block-editor/block-editor';
import { ampsInUse } from '../grid/block-info';
import { GridEditor } from '../grid/grid-editor';
import { CatalogStore } from '../state/catalog.store';
import { LiveStore } from '../state/live.store';
import { UiStore } from '../state/ui.store';

/**
 * The main view, as in Axe-Edit: the grid on top, the selected block's page below. In Block Info view (F1, or ⌥ held)
 * a line under the grid sums up the amps in use: each amp block's model per channel and the scenes on it.
 */
@Component({
  selector: 'cth-editor-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BlockEditor, GridEditor, XuiSplitterImports],
  host: { class: 'block h-full p-1.5' },
  template: `
    <xui-splitter orientation="vertical" class="h-full">
      <xui-splitter-panel [defaultSize]="50" [min]="25">
        <div class="pane !bg-surface-sunken h-full [scrollbar-gutter:stable] !overflow-auto p-6">
          <cth-grid-editor />
          @if (ui.blockInfo()) {
            <div class="text-foreground-muted mt-3 flex flex-wrap gap-x-6 gap-y-1 text-xs" data-testid="amps-in-use">
              <span class="font-semibold">Amps in use</span>
              @for (amp of amps(); track amp.id) {
                <span>
                  <strong class="text-foreground">{{ amp.id }}</strong>
                  @for (m of amp.models; track m.channel) {
                    · {{ m.channel }}: {{ m.model || '—' }} (scene{{ m.scenes.length > 1 ? 's' : '' }}
                    {{ m.scenes.join(', ') }})
                  }
                </span>
              } @empty {
                <span>none</span>
              }
            </div>
          }
        </div>
      </xui-splitter-panel>
      <xui-splitter-panel [min]="20">
        <cth-block-editor class="pane" />
      </xui-splitter-panel>
    </xui-splitter>
  `
})
export class EditorPage {
  protected readonly ui = inject(UiStore);
  private readonly live = inject(LiveStore);
  private readonly catalog = inject(CatalogStore);

  protected readonly amps = computed(() => {
    const preset = this.live.preset();
    return preset ? ampsInUse(preset, type => this.catalog.info(type)) : [];
  });
}
