import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import type { BlockPreset, PresetSummary } from '@cathode/api-client';
import { provideIcons } from '@ng-icons/core';
import { matDeleteRound, matSearchRound } from '@ng-icons/material-icons/round';
import { XuiAlertDialogImports } from '@xui/alert-dialog';
import { XuiButtonImports } from '@xui/button';
import { type XuiDataColumn, XuiDataTableImports } from '@xui/data-table';
import { XuiDialogImports } from '@xui/dialog';
import { XuiIconImports } from '@xui/icon';
import { XuiInputImports } from '@xui/input';
import { XuiMenuImports } from '@xui/menu';
import { XuiNonIdealStateImports } from '@xui/non-ideal-state';
import { XuiTableImports } from '@xui/table';
import { XuiTextImports } from '@xui/text';
import { Notify } from '../core/notify';
import { BlockFileExtension, BlockPresetsStore } from '../state/block-presets.store';
import { CatalogStore } from '../state/catalog.store';
import { CompareStore } from '../state/compare.store';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';

/** The preset library: search by name, author or tag; load, export, import and delete. */
@Component({
  selector: 'cth-library-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    XuiAlertDialogImports,
    XuiButtonImports,
    XuiDataTableImports,
    XuiDialogImports,
    XuiIconImports,
    XuiInputImports,
    XuiMenuImports,
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

    <section
      class="flex flex-col gap-2 rounded-lg"
      aria-labelledby="block-library-heading"
      data-testid="block-library-section"
      [class.outline-2]="dropping()"
      [class.outline-primary]="dropping()"
      [class.outline-dashed]="dropping()"
      (dragover)="libraryDragOver($event)"
      (dragleave)="dropping.set(false)"
      (drop)="libraryDrop($event)"
    >
      <div class="flex flex-wrap items-center gap-2">
        <h2 xuiHeading id="block-library-heading" class="mr-auto text-sm">Block Library</h2>
        <button xuiButton size="sm" variant="outline" type="button" (click)="saveAllBlocks()">
          Save all blocks of this preset
        </button>
        <button xuiButton size="sm" variant="outline" type="button" (click)="blockFile.click()">Import…</button>
        <input #blockFile type="file" class="hidden" accept=".cthblock" multiple (change)="importBlocks(blockFile)" />
        <button
          xuiButton
          size="sm"
          variant="outline"
          type="button"
          [disabled]="chosen().size === 0"
          (click)="exportBlocks()"
        >
          Export {{ chosen().size || '' }}
        </button>
      </div>
      <p xuiText class="text-foreground-muted text-xs">
        {{ blockPresets.presets().length - blockPresets.user().length }} factory presets for {{ factoryTypes() }} block
        types. Recall one from a block's page; save a channel or a whole block there (⌘B) to add your own. Drop
        .cthblock files here, or on a grid cell, to import them.
      </p>
      @for (folder of blockPresets.folders(); track folder.name) {
        <details open class="flex flex-col">
          <summary class="cursor-pointer text-xs font-semibold">
            {{ folder.name || 'Unfiled' }} ({{ folder.entries.length }})
          </summary>
          <xui-table>
            @for (preset of folder.entries; track preset.id) {
              <xui-tr>
                <xui-td>
                  <input
                    type="checkbox"
                    [attr.aria-label]="'Choose ' + preset.name"
                    [checked]="chosen().has(preset.id)"
                    (change)="toggleChosen(preset.id)"
                  />
                </xui-td>
                <xui-td class="w-full">{{ preset.name }}</xui-td>
                <xui-td>{{ catalog.info(preset.type)?.name ?? preset.type }}</xui-td>
                <xui-td>{{ preset.channels ? 'whole block' : 'one channel' }}</xui-td>
                <xui-td>
                  <button
                    xuiButton
                    variant="ghost"
                    size="sm"
                    [xuiMenuTriggerFor]="folders"
                    [xuiMenuTriggerData]="{ preset }"
                  >
                    Move…
                  </button>
                </xui-td>
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
        </details>
      }
    </section>

    <xui-dialog title="New folder" [open]="folderFor() !== null" (openChange)="!$event && folderFor.set(null)">
      <xui-dialog-body>
        <input
          xuiInput
          aria-label="Folder name"
          [value]="folderName()"
          (input)="folderName.set($any($event.target).value)"
          (keydown.enter)="moveToNewFolder()"
        />
      </xui-dialog-body>
      <xui-dialog-footer>
        <button xuiButton variant="ghost" type="button" (click)="folderFor.set(null)">Cancel</button>
        <button xuiButton type="button" [disabled]="!folderName().trim()" (click)="moveToNewFolder()">Move</button>
      </xui-dialog-footer>
    </xui-dialog>

    <ng-template #folders let-preset="preset">
      <xui-menu>
        <button xuiMenuItem (click)="blockPresets.moveTo(preset, '')">Unfiled</button>
        @for (f of blockPresets.folders(); track f.name) {
          @if (f.name) {
            <button xuiMenuItem (click)="blockPresets.moveTo(preset, f.name)">{{ f.name }}</button>
          }
        }
        <button xuiMenuItem (click)="newFolder(preset)">New folder…</button>
      </xui-menu>
    </ng-template>

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
  private readonly live = inject(LiveStore);
  private readonly notify = inject(Notify);
  private readonly compare = inject(CompareStore);
  private readonly router = inject(Router);

  protected readonly query = signal('');
  protected readonly selected = signal<PresetSummary | null>(null);
  protected readonly confirmDelete = signal(false);
  /** Library entries chosen for export. */
  protected readonly chosen = signal<ReadonlySet<string>>(new Set());
  protected readonly dropping = signal(false);
  protected readonly folderFor = signal<BlockPreset | null>(null);
  protected readonly folderName = signal('');
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
    if (preset && (await this.live.load(preset.id))) {
      this.compare.reset();
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

  protected toggleChosen(id: string): void {
    this.chosen.update(set => {
      const next = new Set(set);
      if (!next.delete(id)) {
        next.add(id);
      }

      return next;
    });
  }

  protected async exportBlocks(): Promise<void> {
    const ids = [...this.chosen()];
    const first = this.blockPresets.presets().find(p => p.id === ids[0]);
    await this.blockPresets.export(ids, ids.length === 1 && first ? first.name : 'block-library');
  }

  protected async importBlocks(input: HTMLInputElement): Promise<void> {
    const files = [...(input.files ?? [])];
    input.value = '';
    for (const file of files) {
      await this.blockPresets.import(file);
    }
  }

  protected libraryDragOver(event: DragEvent): void {
    if (event.dataTransfer?.types.includes('Files')) {
      event.preventDefault();
      this.dropping.set(true);
    }
  }

  protected async libraryDrop(event: DragEvent): Promise<void> {
    event.preventDefault();
    this.dropping.set(false);
    for (const file of [...(event.dataTransfer?.files ?? [])].filter(f => f.name.endsWith(BlockFileExtension))) {
      await this.blockPresets.import(file);
    }
  }

  protected async saveAllBlocks(): Promise<void> {
    const count = await this.blockPresets.saveAll(this.live.preset()?.name || null);
    if (count > 0) {
      this.notify.success(`Saved ${count} block(s) to the Library.`);
    }
  }

  protected newFolder(preset: BlockPreset): void {
    this.folderName.set('');
    this.folderFor.set(preset);
  }

  protected async moveToNewFolder(): Promise<void> {
    const preset = this.folderFor();
    const name = this.folderName().trim();
    if (preset && name) {
      await this.blockPresets.moveTo(preset, name);
      this.folderFor.set(null);
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
