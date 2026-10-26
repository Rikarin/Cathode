import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import type { BlockPreset, PresetSummary } from '@cathode/api-client';
import { provideIcons } from '@ng-icons/core';
import { matDeleteRound, matSearchRound, matStarOutlineRound, matStarRound } from '@ng-icons/material-icons/round';
import { XuiAlertDialogImports } from '@xui/alert-dialog';
import { XuiButtonImports } from '@xui/button';
import { XuiDialogImports } from '@xui/dialog';
import { XuiIconImports } from '@xui/icon';
import { XuiInputImports } from '@xui/input';
import { XuiMenuImports } from '@xui/menu';
import { XuiNonIdealStateImports } from '@xui/non-ideal-state';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { XuiTableImports } from '@xui/table';
import { XuiTagImports } from '@xui/tag';
import { XuiTextImports } from '@xui/text';
import { Notify } from '../core/notify';
import { BlockFileExtension, BlockPresetsStore } from '../state/block-presets.store';
import { CatalogStore } from '../state/catalog.store';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';
import { PresetSwitcher } from '../state/preset-switcher';
import { AssetLibrary, AssetList, AssetToolbar } from './asset-library';
import { GlobalBlocks } from './global-blocks';

/**
 * The Library: presets (search by name, author or tag; show all, your own or the read-only factory bank's; double-click
 * or Load opens one in the editor; export, import, delete your own), the Block Library (folders of block and channel presets), the global blocks (rename, delete), and the NAM amps and IR cabs every Amp NAM, Cab
 * and IR Player block picks from (upload, rename, tag, delete; the IR tools). `?view=amps` or `?view=cabs` opens
 * those.
 */
@Component({
  selector: 'cth-library-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AssetList,
    AssetToolbar,
    GlobalBlocks,
    XuiAlertDialogImports,
    XuiButtonImports,
    XuiDialogImports,
    XuiIconImports,
    XuiInputImports,
    XuiMenuImports,
    XuiTableImports,
    XuiTagImports,
    XuiNonIdealStateImports,
    XuiSegmentedControlImports,
    XuiTextImports
  ],
  providers: [AssetLibrary, provideIcons({ matDeleteRound, matSearchRound, matStarOutlineRound, matStarRound })],
  host: { class: 'flex h-full flex-col' },
  template: `
    <header class="border-border flex flex-wrap items-center gap-4 border-b px-6 py-3">
      <h1 xuiHeading class="text-lg">Library</h1>
      <xui-segmented-control
        size="sm"
        aria-label="Library section"
        [options]="views"
        [value]="view()"
        (valueChange)="$event && show($event)"
      />
      @if (view() === 'presets') {
        <xui-segmented-control
          class="ml-auto"
          size="sm"
          aria-label="Which presets"
          data-testid="preset-source"
          [options]="sources"
          [value]="source()"
          (valueChange)="$event && source.set($event)"
        />
        <div class="relative">
          <ng-icon
            xui
            name="matSearchRound"
            size="sm"
            class="text-foreground-muted pointer-events-none absolute top-1/2 left-2.5 -translate-y-1/2"
          />
          <input
            xuiInput
            type="search"
            class="w-72 pl-8"
            placeholder="Search name, author or tag"
            aria-label="Search presets"
            [value]="query()"
            (input)="query.set($any($event.target).value)"
          />
        </div>
        <button xuiButton type="button" variant="outline" size="sm" (click)="importFile.click()">Import…</button>
        <input #importFile type="file" class="hidden" accept=".cthpreset" (change)="import(importFile)" />
        <button
          xuiButton
          type="button"
          variant="outline"
          size="sm"
          data-testid="export-list"
          (click)="library.download('/api/v1/presets/list.txt', 'Cathode presets.txt')"
        >
          Export list…
        </button>
      } @else if (view() === 'blocks') {
        <div class="ml-auto flex items-center gap-2">
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
            Export{{ chosen().size ? ' ' + chosen().size : '' }}
          </button>
        </div>
      } @else if (view() !== 'globals') {
        <cth-asset-toolbar />
      }
    </header>

    @if (view() === 'presets') {
      @if (library.presets().length === 0) {
        <xui-non-ideal-state
          class="flex-1"
          title="No presets yet"
          description="Build a rig in the editor and save it, or import a .cthpreset file."
        />
      } @else {
        <div class="min-h-0 flex-1 overflow-auto px-6 pt-4">
          <xui-table interactive class="w-full" data-testid="preset-list" aria-label="Presets">
            <xui-tr class="bg-background sticky top-0 z-10">
              <xui-th class="w-10" aria-label="Favourite" />
              <xui-th class="min-w-0 flex-1">Name</xui-th>
              <xui-th class="w-48">Author</xui-th>
              <xui-th class="w-52 justify-end">Modified</xui-th>
            </xui-tr>
            @for (preset of rows(); track preset.id) {
              <xui-tr
                tabindex="0"
                class="cursor-default"
                [class.cth-selected-row]="selected()?.id === preset.id"
                [attr.data-state]="selected()?.id === preset.id ? 'selected' : null"
                [attr.aria-selected]="selected()?.id === preset.id"
                [attr.data-testid]="'preset-row-' + preset.id"
                (click)="selected.set(preset)"
                (dblclick)="loadPreset(preset)"
                (keydown.enter)="loadPreset(preset)"
                (focus)="selected.set(preset)"
              >
                <xui-td class="w-10">
                  <button
                    type="button"
                    class="hover:bg-active-overlay rounded p-1"
                    [class.text-warning]="preset.favorite"
                    [class.text-foreground-subtle]="!preset.favorite"
                    [attr.aria-label]="(preset.favorite ? 'Unfavourite ' : 'Favourite ') + preset.name"
                    [attr.aria-pressed]="preset.favorite"
                    (click)="
                      $event.stopPropagation(); library.setPresetMetadata(preset.id, { favorite: !preset.favorite })
                    "
                    (dblclick)="$event.stopPropagation()"
                  >
                    <ng-icon xui size="sm" [name]="preset.favorite ? 'matStarRound' : 'matStarOutlineRound'" />
                  </button>
                </xui-td>
                <xui-td class="min-w-0 flex-1">
                  <div class="flex flex-wrap items-center gap-1.5">
                    @if (preset.color) {
                      <span class="size-2.5 shrink-0 rounded-full" [style.background]="preset.color"></span>
                    }
                    <span class="font-medium">{{ preset.name }}</span>
                    @if (preset.factory) {
                      <xui-tag minimal round color="primary" data-testid="factory-tag">Factory</xui-tag>
                    }
                    @for (tag of preset.tags; track tag) {
                      <xui-tag minimal round>{{ tag }}</xui-tag>
                    }
                  </div>
                </xui-td>
                <xui-td class="text-foreground-muted w-48">{{ preset.author || '—' }}</xui-td>
                <xui-td class="text-foreground-muted w-52 justify-end tabular-nums">
                  {{ preset.factory ? 'Factory bank' : modified(preset) }}
                </xui-td>
              </xui-tr>
            } @empty {
              <xui-tr>
                <xui-td class="text-foreground-muted flex-1 justify-center py-8">
                  No preset matches “{{ query() }}”.
                </xui-td>
              </xui-tr>
            }
          </xui-table>
        </div>
        <footer class="border-border bg-surface flex items-center gap-2 border-t px-6 py-3">
          <span class="text-foreground-muted mr-auto text-sm">
            @if (selected(); as preset) {
              <span class="text-foreground font-medium">{{ preset.name }}</span>
              @if (preset.description) {
                <span class="ms-2" data-testid="preset-description">{{ preset.description }}</span>
              }
            } @else {
              {{ rows().length }} {{ rows().length === 1 ? 'preset' : 'presets' }} · double-click one to load it
            }
          </span>
          <button xuiButton type="button" variant="ghost" [disabled]="!selected()" (click)="export()">Export</button>
          <button
            xuiButton
            type="button"
            variant="ghost"
            color="error"
            [disabled]="!selected() || selected()?.factory"
            [attr.title]="selected()?.factory ? 'Factory presets are read-only' : null"
            (click)="askDelete()"
          >
            Delete
          </button>
          <button
            xuiButton
            type="button"
            color="primary"
            data-testid="load-preset"
            [disabled]="!selected()"
            (click)="load()"
          >
            Load
          </button>
        </footer>
      }
    } @else if (view() === 'blocks') {
      <section
        class="flex min-h-0 flex-1 flex-col gap-4 overflow-auto px-6 py-4"
        aria-label="Block Library"
        data-testid="block-library-section"
        [class.outline-2]="dropping()"
        [class.-outline-offset-8]="dropping()"
        [class.outline-primary]="dropping()"
        [class.outline-dashed]="dropping()"
        (dragover)="libraryDragOver($event)"
        (dragleave)="dropping.set(false)"
        (drop)="libraryDrop($event)"
      >
        <p xuiText class="text-foreground-muted text-sm">
          {{ blockPresets.presets().length - blockPresets.user().length }} factory presets for {{ factoryTypes() }}
          block types. Recall one from a block's page; save a channel or a whole block there (⌘B) to add your own. Drop
          .cthblock files here, or on a grid cell, to import them.
        </p>
        @if (blockPresets.folders().length === 0) {
          <xui-non-ideal-state
            class="flex-1"
            title="No block presets of your own yet"
            description="Open a block, then save its channel or the whole block (⌘B). It shows up here, ready for any preset."
          />
        }
        @for (folder of blockPresets.folders(); track folder.name) {
          <details open class="border-border rounded-lg border">
            <summary
              class="hover:bg-hover-overlay flex cursor-pointer items-center gap-2 rounded-t-lg px-4 py-2 text-sm font-semibold"
            >
              {{ folder.name || 'Unfiled' }}
              <span class="text-foreground-muted font-normal">{{ folder.entries.length }}</span>
            </summary>
            <xui-table compact interactive class="w-full">
              @for (preset of folder.entries; track preset.id) {
                <xui-tr>
                  <xui-td class="w-8">
                    <input
                      type="checkbox"
                      class="accent-primary"
                      [attr.aria-label]="'Choose ' + preset.name"
                      [checked]="chosen().has(preset.id)"
                      (change)="toggleChosen(preset.id)"
                    />
                  </xui-td>
                  <xui-td class="min-w-0 flex-1 font-medium">{{ preset.name }}</xui-td>
                  <xui-td class="text-foreground-muted w-44 whitespace-nowrap">
                    {{ catalog.info(preset.type)?.name ?? preset.type }}
                  </xui-td>
                  <xui-td class="w-32">
                    <xui-tag minimal round>{{ preset.channels ? 'Whole block' : 'Channel' }}</xui-tag>
                  </xui-td>
                  <xui-td class="w-32 justify-end whitespace-nowrap">
                    <button
                      xuiButton
                      variant="ghost"
                      size="sm"
                      [xuiMenuTriggerFor]="folders"
                      [xuiMenuTriggerData]="{ preset }"
                    >
                      Move…
                    </button>
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
    } @else if (view() === 'globals') {
      <cth-global-blocks />
    } @else {
      <cth-asset-list />
    }

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
      [description]="'“' + (selected()?.name ?? '') + '” will be deleted from the library.'"
      confirmText="Delete"
      [(open)]="confirmDelete"
      (confirmed)="delete()"
    />

    <xui-dialog title="This preset is in use" [(open)]="inUse">
      <xui-dialog-body class="flex flex-col gap-2">
        <p xuiText>
          “{{ selected()?.name }}” cannot be deleted while a bank or setlist holds it: a program change or setlist entry
          would load nothing there. It is used by:
        </p>
        <ul class="list-disc ps-5 text-sm" data-testid="preset-uses">
          @for (use of uses(); track use) {
            <li>{{ use }}</li>
          }
        </ul>
        <p xuiText class="text-foreground-muted text-sm">
          Take it out of its banks in Manage presets first (or delete it there), then delete it here.
        </p>
      </xui-dialog-body>
      <xui-dialog-footer>
        <button xuiButton variant="ghost" type="button" (click)="inUse.set(false)">OK</button>
        <button xuiButton type="button" (click)="manage()">Manage presets</button>
      </xui-dialog-footer>
    </xui-dialog>
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
  private readonly switcher = inject(PresetSwitcher);
  private readonly router = inject(Router);

  protected readonly query = signal('');
  /** All presets, the user's own, or the factory bank's. */
  protected readonly source = signal<'all' | 'mine' | 'factory'>('all');
  protected readonly sources = [
    { label: 'All', value: 'all' as const },
    { label: 'Mine', value: 'mine' as const },
    { label: 'Factory', value: 'factory' as const }
  ];
  protected readonly selected = signal<PresetSummary | null>(null);
  protected readonly confirmDelete = signal(false);
  /** Where the selected preset is used, which keeps it from being deleted (the in-use dialog lists them). */
  protected readonly uses = signal<readonly string[]>([]);
  protected readonly inUse = signal(false);
  /** Library entries chosen for export. */
  protected readonly chosen = signal<ReadonlySet<string>>(new Set());
  protected readonly dropping = signal(false);
  protected readonly folderFor = signal<BlockPreset | null>(null);
  protected readonly folderName = signal('');
  private readonly assets = inject(AssetLibrary);
  /** Presets, the Block Library, or the asset views (whose kind the asset state keeps, as an upload can switch it). */
  private readonly section = signal<'presets' | 'blocks' | 'globals' | 'assets'>('presets');
  protected readonly view = computed(() => {
    const section = this.section();
    return section === 'assets' ? this.assets.kind() : section;
  });
  protected readonly views = [
    { label: 'Presets', value: 'presets' as const },
    { label: 'Block Library', value: 'blocks' as const },
    { label: 'Global blocks', value: 'globals' as const },
    { label: 'NAM amps', value: 'amps' as const },
    { label: 'IR cabs', value: 'cabs' as const }
  ];

  /** Favourites first, then the user's presets by name, then the factory bank in its own order. */
  protected readonly rows = computed(() => {
    const query = this.query().trim().toLowerCase();
    const source = this.source();
    const order = new Map(this.library.presets().map((p, i) => [p.id, i]));
    const all = this.library
      .presets()
      .filter(p => source === 'all' || (source === 'factory') === (p.factory ?? false))
      .sort(
        (a, b) =>
          Number(b.favorite ?? false) - Number(a.favorite ?? false) ||
          Number(a.factory ?? false) - Number(b.factory ?? false) ||
          (a.factory ? order.get(a.id)! - order.get(b.id)! : a.name.localeCompare(b.name))
      );
    return query
      ? all.filter(p =>
          [p.name, p.author ?? '', p.description ?? '', ...p.tags].some(field => field.toLowerCase().includes(query))
        )
      : all;
  });

  constructor() {
    void this.library.refreshPresets();
    void this.blockPresets.refresh();
    this.assets.refresh();
    const view = inject(ActivatedRoute).snapshot.queryParamMap.get('view');
    if (view === 'amps' || view === 'cabs' || view === 'blocks' || view === 'globals') {
      this.show(view);
    }
  }

  protected show(view: 'presets' | 'blocks' | 'globals' | 'amps' | 'cabs'): void {
    if (view === 'amps' || view === 'cabs') {
      this.assets.show(view);
      this.section.set('assets');
    } else {
      this.section.set(view);
    }
  }

  protected load(): void {
    const preset = this.selected();
    if (preset) {
      void this.loadPreset(preset);
    }
  }

  /** Opens the editor at once and loads there, so the loading shows over the grid rather than on this page. */
  protected async loadPreset(preset: PresetSummary): Promise<void> {
    this.selected.set(preset);
    if (this.switcher.mayReplace(preset.name)) {
      await this.router.navigateByUrl('/');
      await this.switcher.load(preset.id, preset.name, true);
    }
  }

  protected modified(preset: PresetSummary): string {
    return new Date(preset.modified).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
  }

  protected export(): void {
    const preset = this.selected();
    if (preset) {
      void this.library.download(`/api/v1/presets/${encodeURIComponent(preset.id)}/export`, `${preset.name}.cthpreset`);
    }
  }

  /** Asks before deleting the selected preset, or says where it is used if a bank or setlist holds it. */
  protected async askDelete(): Promise<void> {
    const preset = this.selected();
    if (!preset || preset.factory) {
      return;
    }

    const uses = await this.library.usesOf(preset.id);
    if (uses === undefined) {
      return;
    }

    this.uses.set(uses);
    (uses.length > 0 ? this.inUse : this.confirmDelete).set(true);
  }

  protected async delete(): Promise<void> {
    const preset = this.selected();
    if (preset && !preset.factory && (await this.library.deletePreset(preset.id))) {
      this.selected.set(null);
    }
  }

  protected manage(): void {
    this.inUse.set(false);
    void this.router.navigateByUrl('/manage');
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
