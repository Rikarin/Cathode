import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import type { PresetSummary } from '@cathode/api-client';
import { provideIcons } from '@ng-icons/core';
import { matDeleteRound, matSearchRound } from '@ng-icons/material-icons/round';
import { XuiAlertDialogImports } from '@xui/alert-dialog';
import { XuiButtonImports } from '@xui/button';
import { type XuiDataColumn, XuiDataTableImports } from '@xui/data-table';
import { XuiIconImports } from '@xui/icon';
import { XuiInputImports } from '@xui/input';
import { XuiNonIdealStateImports } from '@xui/non-ideal-state';
import { XuiTableImports } from '@xui/table';
import { XuiTextImports } from '@xui/text';
import { BlockPresetsStore } from '../state/block-presets.store';
import { CatalogStore } from '../state/catalog.store';
import { LibraryStore } from '../state/library.store';
import { PresetSwitcher } from '../state/preset-switcher';

/** The preset library: search by name, author or tag; load, export, import and delete. */
@Component({
  selector: 'cth-library-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    XuiAlertDialogImports,
    XuiButtonImports,
    XuiDataTableImports,
    XuiIconImports,
    XuiInputImports,
    XuiTableImports,
    XuiNonIdealStateImports,
    XuiTextImports
  ],
  providers: [provideIcons({ matDeleteRound, matSearchRound })],
  host: { class: 'flex h-full flex-col gap-4 p-6' },
  template: `
    <header class="flex flex-wrap items-center gap-3">
      <h1 xuiHeading class="mr-auto text-lg">Presets</h1>
      <input
        xuiInput
        type="search"
        class="w-64"
        placeholder="Search name, author or tag"
        aria-label="Search presets"
        [value]="query()"
        (input)="query.set($any($event.target).value)"
      />
      <button
        xuiButton
        type="button"
        variant="outline"
        data-testid="export-list"
        (click)="library.download('/api/v1/presets/list.txt', 'Cathode presets.txt')"
      >
        Export list…
      </button>
      <button xuiButton type="button" variant="outline" (click)="importFile.click()">Import…</button>
      <input #importFile type="file" class="hidden" accept=".cthpreset" (change)="import(importFile)" />
    </header>

    @if (library.presets().length === 0) {
      <xui-non-ideal-state
        class="flex-1"
        title="No presets yet"
        description="Build a rig in the editor and save it, or import a .cthpreset file."
      />
    } @else {
      <xui-data-table
        class="flex-1"
        [data]="rows()"
        [columns]="columns"
        [height]="420"
        [multiple]="false"
        [reorderable]="false"
        (cellClicked)="selected.set($event.row)"
      />
      <footer class="flex items-center gap-2">
        <span class="text-foreground-muted mr-auto text-sm">
          {{ selected() ? selected()!.name : rows().length + ' preset(s)' }}
        </span>
        <button xuiButton type="button" variant="outline" [disabled]="!selected()" (click)="export()">Export</button>
        <button
          xuiButton
          type="button"
          variant="outline"
          color="error"
          [disabled]="!selected()"
          (click)="confirmDelete.set(true)"
        >
          Delete
        </button>
        <button xuiButton type="button" data-testid="load-preset" [disabled]="!selected()" (click)="load()">
          Load
        </button>
      </footer>
    }

    <section class="flex flex-col gap-2" aria-labelledby="block-library-heading">
      <h2 xuiHeading id="block-library-heading" class="text-sm">Block Library</h2>
      <p xuiText class="text-foreground-muted text-xs">
        {{ blockPresets.presets().length - blockPresets.user().length }} factory presets for {{ factoryTypes() }} block
        types. Load one from a block's page; save a block's channel there to add your own.
      </p>
      @if (blockPresets.user().length > 0) {
        <xui-table>
          <xui-tr>
            <xui-th>Name</xui-th>
            <xui-th>Block</xui-th>
            <xui-th><span class="sr-only">Remove</span></xui-th>
          </xui-tr>
          @for (preset of blockPresets.user(); track preset.id) {
            <xui-tr>
              <xui-td>{{ preset.name }}</xui-td>
              <xui-td>{{ catalog.info(preset.type)?.name ?? preset.type }}</xui-td>
              <xui-td>
                <button
                  xuiButton
                  variant="ghost"
                  size="sm"
                  color="error"
                  [attr.aria-label]="'Delete block preset ' + preset.name"
                  (click)="blockPresets.delete(preset.id)"
                >
                  <ng-icon xui name="matDeleteRound" size="sm" />
                </button>
              </xui-td>
            </xui-tr>
          }
        </xui-table>
      }
    </section>

    <xui-alert-dialog
      destructive
      title="Delete preset?"
      [description]="
        '“' + (selected()?.name ?? '') + '” will be removed from the library and from any setlist or bank.'
      "
      confirmText="Delete"
      [(open)]="confirmDelete"
      (confirmed)="delete()"
    />
  `
})
export class LibraryPage {
  protected readonly library = inject(LibraryStore);
  protected readonly blockPresets = inject(BlockPresetsStore);
  protected readonly catalog = inject(CatalogStore);
  protected readonly factoryTypes = computed(
    () =>
      new Set(
        this.blockPresets
          .presets()
          .filter(p => p.factory)
          .map(p => p.type)
      ).size
  );
  private readonly switcher = inject(PresetSwitcher);
  private readonly router = inject(Router);

  protected readonly query = signal('');
  protected readonly selected = signal<PresetSummary | null>(null);
  protected readonly confirmDelete = signal(false);
  protected readonly columns: XuiDataColumn<PresetSummary>[] = [
    { id: 'name', header: 'Name', width: 280, sortable: true },
    { id: 'author', header: 'Author', width: 160, sortable: true, value: p => p.author ?? '' },
    { id: 'tags', header: 'Tags', width: 220, value: p => p.tags.join(', ') },
    {
      id: 'modified',
      header: 'Modified',
      width: 180,
      sortable: true,
      value: p => new Date(p.modified).toLocaleString()
    }
  ];

  protected readonly rows = computed(() => {
    const query = this.query().trim().toLowerCase();
    const all = [...this.library.presets()].sort((a, b) => a.name.localeCompare(b.name));
    return query
      ? all.filter(p => [p.name, p.author ?? '', ...p.tags].some(field => field.toLowerCase().includes(query)))
      : all;
  });

  constructor() {
    void this.library.refreshPresets();
    void this.blockPresets.refresh();
  }

  protected async load(): Promise<void> {
    const preset = this.selected();
    if (preset && (await this.switcher.load(preset.id, preset.name))) {
      await this.router.navigateByUrl('/');
    }
  }

  protected export(): void {
    const preset = this.selected();
    if (preset) {
      void this.library.download(`/api/v1/presets/${encodeURIComponent(preset.id)}/export`, `${preset.name}.cthpreset`);
    }
  }

  protected async delete(): Promise<void> {
    const preset = this.selected();
    if (preset) {
      await this.library.deletePreset(preset.id);
      this.selected.set(null);
    }
  }

  protected async import(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    input.value = '';
    if (file) {
      await this.library.import(file);
    }
  }
}
