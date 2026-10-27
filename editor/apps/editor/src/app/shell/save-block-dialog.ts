import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { provideIcons } from '@ng-icons/core';
import { matSaveRound } from '@ng-icons/material-icons/round';
import { XuiButtonImports } from '@xui/button';
import { XuiDialogImports } from '@xui/dialog';
import { XuiFormFieldImports } from '@xui/form-field';
import { XuiInputImports } from '@xui/input';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { BlockPresetsStore, wholeBlock } from '../state/block-presets.store';
import { CatalogStore } from '../state/catalog.store';
import { LiveStore } from '../state/live.store';
import { UiStore } from '../state/ui.store';

/** Save the selected block to the Library (⌘B, or the block's menu): its current channel or the whole block. */
@Component({
  selector: 'cth-save-block-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [XuiButtonImports, XuiDialogImports, XuiFormFieldImports, XuiInputImports, XuiSegmentedControlImports],
  providers: [provideIcons({ matSaveRound })],
  template: `
    <xui-dialog
      title="Save to the Library"
      icon="matSaveRound"
      [open]="ui.saveBlockOpen()"
      (openChange)="ui.saveBlockOpen.set($event)"
    >
      <xui-dialog-body class="flex flex-col gap-4">
        <xui-form-field label="Name">
          <input
            #nameField
            xuiInput
            data-testid="block-preset-name"
            [value]="name()"
            (input)="name.set(nameField.value)"
            (keydown.enter)="save()"
          />
        </xui-form-field>
        <xui-form-field label="Folder">
          <input
            #folderField
            xuiInput
            list="library-folders"
            placeholder="(none)"
            [value]="folder()"
            (input)="folder.set(folderField.value)"
          />
          <datalist id="library-folders">
            @for (f of presets.folders(); track f.name) {
              @if (f.name) {
                <option [value]="f.name"></option>
              }
            }
          </datalist>
        </xui-form-field>
        @if (channels() > 1) {
          <xui-segmented-control
            size="sm"
            aria-label="What to save"
            [options]="scopes()"
            [value]="scope()"
            (valueChange)="$event && scope.set($event)"
          />
        }
      </xui-dialog-body>
      <xui-dialog-footer>
        <button xuiButton variant="ghost" type="button" (click)="ui.saveBlockOpen.set(false)">Cancel</button>
        <button xuiButton type="button" data-testid="block-preset-save" [disabled]="!name().trim()" (click)="save()">
          Save
        </button>
      </xui-dialog-footer>
    </xui-dialog>
  `
})
export class SaveBlockDialog {
  protected readonly ui = inject(UiStore);
  protected readonly presets = inject(BlockPresetsStore);
  private readonly live = inject(LiveStore);
  private readonly catalog = inject(CatalogStore);

  protected readonly name = signal('');
  protected readonly folder = signal('');
  protected readonly scope = signal<'channel' | 'block'>('channel');
  protected readonly channels = computed(() => {
    const block = this.ui.block();
    return block ? this.ui.channels(block) : 0;
  });
  protected readonly scopes = computed(() => {
    const block = this.ui.block();
    return [
      { label: `Channel ${block ? this.live.channelOf(block.id) : 'A'}`, value: 'channel' as const },
      { label: 'Whole block (all channels, modifiers)', value: 'block' as const }
    ];
  });

  constructor() {
    effect(() => {
      if (this.ui.saveBlockOpen()) {
        untracked(() => {
          const block = this.ui.block();
          this.name.set(block ? (this.catalog.info(block.type)?.name ?? block.type) : '');
          this.scope.set('channel');
          void this.presets.refresh();
        });
      }
    });
  }

  protected async save(): Promise<void> {
    const block = this.ui.block();
    const name = this.name().trim();
    if (!block || !name || !this.catalog.info(block.type)?.params.length) {
      return;
    }

    const channel = this.live.channelOf(block.id);
    const entry =
      this.scope() === 'block'
        ? wholeBlock(block, channel, this.live.preset()?.modifiers ?? [])
        : { values: { ...(block.channels[channel] ?? {}) } };
    await this.presets.save(block.type, name, { ...entry, folder: this.folder().trim() });
    this.ui.saveBlockOpen.set(false);
  }
}
