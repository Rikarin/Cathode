import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { provideIcons } from '@ng-icons/core';
import { matDeleteRound } from '@ng-icons/material-icons/round';
import { XuiButtonImports } from '@xui/button';
import { XuiIconImports } from '@xui/icon';
import { XuiInputImports } from '@xui/input';
import { XuiNonIdealStateImports } from '@xui/non-ideal-state';
import { XuiTableImports } from '@xui/table';
import { XuiTagImports } from '@xui/tag';
import { XuiTextImports } from '@xui/text';
import { CatalogStore } from '../state/catalog.store';
import { GlobalStore } from '../state/global.store';
import { LiveStore } from '../state/live.store';

/**
 * The global blocks: rename one (Enter or leaving the field saves), or delete it. Deleting never changes a sound: the
 * blocks linked to it, here and in saved presets, keep its settings as their own.
 */
@Component({
  selector: 'cth-global-blocks',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    XuiButtonImports,
    XuiIconImports,
    XuiInputImports,
    XuiNonIdealStateImports,
    XuiTableImports,
    XuiTagImports,
    XuiTextImports
  ],
  providers: [provideIcons({ matDeleteRound })],
  host: { class: 'flex min-h-0 flex-1 flex-col gap-4 overflow-auto px-6 py-4' },
  template: `
    <p xuiText class="text-foreground-muted text-sm">
      A global block shares its settings across presets: edit it in one and every preset linked to it follows. Make one
      from a block's menu (G), and link others of the same type there. Deleting one leaves the linked blocks with its
      settings as their own.
    </p>
    @if (rows().length === 0) {
      <xui-non-ideal-state
        class="flex-1"
        title="No global blocks yet"
        description="Right-click a block on the grid and choose Make global (or press G)."
      />
    } @else {
      <xui-table interactive class="w-full" data-testid="global-blocks" aria-label="Global blocks">
        <xui-tr class="bg-background sticky top-0 z-10">
          <xui-th class="min-w-0 flex-1">Name</xui-th>
          <xui-th class="w-48">Type</xui-th>
          <xui-th class="w-48">This preset</xui-th>
          <xui-th class="w-16" aria-label="Delete" />
        </xui-tr>
        @for (row of rows(); track row.id) {
          <xui-tr>
            <xui-td class="min-w-0 flex-1">
              <input
                #nameField
                xuiInput
                class="w-full max-w-96"
                [attr.aria-label]="'Name of ' + row.name"
                [value]="row.name"
                (keydown.enter)="nameField.blur()"
                (blur)="rename(row.id, nameField)"
              />
            </xui-td>
            <xui-td class="text-foreground-muted w-48">{{ row.type }}</xui-td>
            <xui-td class="w-48">
              @for (id of row.here; track id) {
                <xui-tag minimal round class="mr-1">{{ id }}</xui-tag>
              }
            </xui-td>
            <xui-td class="w-16 justify-end">
              <button
                type="button"
                xuiButton
                variant="ghost"
                size="sm"
                color="error"
                [attr.aria-label]="'Delete global block ' + row.name"
                (click)="live.deleteGlobal(row.id)"
              >
                <ng-icon xui name="matDeleteRound" size="sm" />
              </button>
            </xui-td>
          </xui-tr>
        }
      </xui-table>
    }
  `
})
export class GlobalBlocks {
  protected readonly live = inject(LiveStore);
  private readonly globals = inject(GlobalStore);
  private readonly catalog = inject(CatalogStore);

  protected readonly rows = computed(() => {
    const blocks = this.live.preset()?.blocks ?? [];
    return [...(this.globals.settings()?.blocks ?? [])]
      .sort((a, b) => a.name.localeCompare(b.name))
      .map(g => ({
        id: g.id,
        name: g.name,
        type: this.catalog.info(g.type)?.name ?? g.type,
        here: blocks.filter(b => b.global === g.id).map(b => b.id)
      }));
  });

  protected rename(id: string, input: HTMLInputElement): void {
    const name = input.value.trim();
    const current = this.globals.settings()?.blocks.find(g => g.id === id);
    if (!current || name === current.name) {
      return;
    }

    if (!name) {
      input.value = current.name;
      return;
    }

    void this.globals.update(g => {
      const shared = g.blocks.find(b => b.id === id);
      if (shared) {
        shared.name = name;
      }
    });
  }
}
