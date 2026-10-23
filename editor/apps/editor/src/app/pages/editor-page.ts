import { ChangeDetectionStrategy, Component } from '@angular/core';
import { XuiSplitterImports } from '@xui/splitter';
import { BlockEditor } from '../block-editor/block-editor';
import { GridEditor } from '../grid/grid-editor';

/** The main view, as in Axe-Edit: the grid on top, the selected block's page below. */
@Component({
  selector: 'cth-editor-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BlockEditor, GridEditor, XuiSplitterImports],
  host: { class: 'block h-full' },
  template: `
    <xui-splitter orientation="vertical" class="h-full">
      <xui-splitter-panel [defaultSize]="52" [min]="25">
        <div class="h-full [scrollbar-gutter:stable] overflow-auto p-6">
          <cth-grid-editor />
        </div>
      </xui-splitter-panel>
      <xui-splitter-panel [min]="20">
        <cth-block-editor class="bg-surface border-border border-t" />
      </xui-splitter-panel>
    </xui-splitter>
  `
})
export class EditorPage {}
