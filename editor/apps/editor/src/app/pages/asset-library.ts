import { ChangeDetectionStrategy, Component, Injectable, computed, inject, signal } from '@angular/core';
import type { AssetInfo } from '@cathode/api-client';
import { provideIcons } from '@ng-icons/core';
import {
  matExpandMoreRound,
  matSearchRound,
  matStarOutlineRound,
  matStarRound,
  matUploadRound
} from '@ng-icons/material-icons/round';
import { XuiAlertDialogImports } from '@xui/alert-dialog';
import { XuiButtonImports } from '@xui/button';
import { XuiDialogImports } from '@xui/dialog';
import { XuiFormFieldImports } from '@xui/form-field';
import { XuiIconImports } from '@xui/icon';
import { XuiInputImports } from '@xui/input';
import { XuiMenuImports } from '@xui/menu';
import { XuiNonIdealStateImports } from '@xui/non-ideal-state';
import { XuiTableImports } from '@xui/table';
import { XuiTooltipImports } from '@xui/tooltip';
import { ConnectionService } from '../core/connection';
import { Notify } from '../core/notify';
import { TagColors, tagColorName } from '../shell/picker-list';
import { LibraryStore } from '../state/library.store';
import { IrCaptureDialog, IrImportDialog, IrImportSettings, IrMixerDialog } from './ir-tools';
import { nextSelection } from './manage';

/** The Library's two asset views: amp captures, and cab IRs (single IRs and IR packs). */
export type AssetKind = 'amps' | 'cabs';

const AmpFiles = ['.nam', '.aidax', '.json'];
const CabFiles = ['.wav', '.cabgrid'];
const KindNames: Record<string, string> = { nam: 'NAM', aidax: 'AIDA-X', ir: 'IR', cabgrid: 'IR pack' };

/**
 * The state the Library's NAM amps and IR cabs views share between their header tools and their list: which kind is
 * shown, the search, the selection, and uploading (with the IR import settings for .wav files). Provided by the
 * Library page.
 */
@Injectable()
export class AssetLibrary {
  readonly library = inject(LibraryStore);
  private readonly notify = inject(Notify);
  private readonly importSettings = inject(IrImportSettings);

  readonly kind = signal<AssetKind>('amps');
  readonly query = signal('');
  readonly favoritesOnly = signal(false);
  readonly selected = signal<ReadonlySet<string>>(new Set());
  private anchor: number | null = null;

  readonly all = computed(() => (this.kind() === 'amps' ? this.library.models() : this.library.cabGrids()));
  readonly rows = computed(() => {
    const words = this.query().trim().toLowerCase().split(/\s+/).filter(Boolean);
    return [...this.all()]
      .filter(a => !this.favoritesOnly() || a.favorite)
      .filter(a => words.every(w => `${a.name} ${kindName(a)}`.toLowerCase().includes(w)))
      .sort((a, b) => Number(b.favorite) - Number(a.favorite) || a.name.localeCompare(b.name));
  });
  readonly selectedAssets = computed(() => this.rows().filter(a => this.selected().has(a.ref)));
  readonly accept = computed(() => (this.kind() === 'amps' ? AmpFiles : CabFiles).join(','));

  refresh(): void {
    void this.library.refreshAssets();
    void this.library.refreshUsage();
  }

  show(kind: AssetKind): void {
    this.kind.set(kind);
    this.selected.set(new Set());
    this.anchor = null;
  }

  usedBy(asset: AssetInfo): readonly string[] {
    return this.library.usage().get(asset.ref) ?? [];
  }

  select(index: number, event: MouseEvent): void {
    const rows = this.rows();
    const current = new Set(rows.flatMap((a, i) => (this.selected().has(a.ref) ? [i] : [])));
    const next = nextSelection(current, this.anchor, index, {
      shift: event.shiftKey,
      toggle: event.metaKey || event.ctrlKey
    });
    this.selected.set(new Set([...next.selected].map(i => rows[i].ref)));
    this.anchor = next.anchor;
  }

  /** Uploads each file the library takes (IRs with the import settings) and shows the view the last one went to. */
  async upload(files: readonly File[]): Promise<void> {
    const extension = (file: File) => file.name.slice(file.name.lastIndexOf('.')).toLowerCase();
    const taken = files.filter(f => [...AmpFiles, ...CabFiles].includes(extension(f)));
    if (taken.length < files.length) {
      this.notify.error('Only .nam, .aidax, .json, .wav and .cabgrid files go into the library.');
    }

    const added: AssetInfo[] = [];
    for (const file of taken) {
      const asset = await this.library.upload(file, extension(file) === '.wav' ? this.importSettings.options() : {});
      if (asset) {
        added.push(asset);
      }
    }

    if (added.length > 0) {
      const last = added[added.length - 1];
      this.show(last.kind === 'nam' || last.kind === 'aidax' ? 'amps' : 'cabs');
      const inView = new Set(this.all().map(a => a.ref));
      this.selected.set(new Set(added.map(a => a.ref).filter(ref => inView.has(ref))));
      this.notify.success(`Added ${added.length} file${added.length === 1 ? '' : 's'} to the library.`);
    }
  }
}

function kindName(asset: AssetInfo): string {
  return KindNames[asset.kind] ?? asset.kind;
}

/** The asset views' header tools: search, favourites only, the IR tools (cabs) and Upload. */
@Component({
  selector: 'cth-asset-toolbar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    IrCaptureDialog,
    IrImportDialog,
    IrMixerDialog,
    XuiButtonImports,
    XuiIconImports,
    XuiInputImports,
    XuiMenuImports
  ],
  providers: [provideIcons({ matExpandMoreRound, matSearchRound, matStarRound, matUploadRound })],
  host: { class: 'ml-auto flex items-center gap-4' },
  template: `
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
        [placeholder]="assets.kind() === 'amps' ? 'Search amps' : 'Search cabs'"
        aria-label="Search the library"
        [value]="assets.query()"
        (input)="assets.query.set($any($event.target).value)"
      />
    </div>
    <button
      xuiButton
      type="button"
      size="sm"
      [variant]="assets.favoritesOnly() ? 'default' : 'outline'"
      [attr.aria-pressed]="assets.favoritesOnly()"
      (click)="assets.favoritesOnly.update(on => !on)"
    >
      <ng-icon xui name="matStarRound" size="sm" /> Favourites
    </button>
    @if (assets.kind() === 'cabs') {
      <button xuiButton type="button" variant="outline" size="sm" [xuiMenuTriggerFor]="irTools">
        IR tools <ng-icon xui name="matExpandMoreRound" size="sm" />
      </button>
    }
    <button xuiButton type="button" variant="outline" size="sm" data-testid="library-upload" (click)="files.click()">
      <ng-icon xui name="matUploadRound" size="sm" /> Upload…
    </button>
    <input
      #files
      type="file"
      multiple
      class="hidden"
      data-testid="library-upload-files"
      [accept]="assets.accept()"
      (change)="uploadFrom(files)"
    />

    <ng-template #irTools>
      <xui-menu>
        <button xuiMenuItem (click)="importOpen.set(true)">Import settings…</button>
        <button xuiMenuItem data-testid="open-ir-mixer" (click)="mixerOpen.set(true)">Mix IRs…</button>
        @if (desktop) {
          <button xuiMenuItem data-testid="open-ir-capture" (click)="captureOpen.set(true)">Capture an IR…</button>
        }
      </xui-menu>
    </ng-template>

    <cth-ir-import-dialog [(open)]="importOpen" />
    <cth-ir-mixer-dialog [(open)]="mixerOpen" />
    @if (desktop) {
      <cth-ir-capture-dialog [(open)]="captureOpen" />
    }
  `
})
export class AssetToolbar {
  protected readonly assets = inject(AssetLibrary);
  protected readonly desktop = inject(ConnectionService).desktop;
  protected readonly importOpen = signal(false);
  protected readonly mixerOpen = signal(false);
  protected readonly captureOpen = signal(false);

  protected async uploadFrom(input: HTMLInputElement): Promise<void> {
    const files = [...(input.files ?? [])];
    input.value = '';
    await this.assets.upload(files);
  }
}

/**
 * The amp captures or cab IRs every Amp NAM, Cab and IR Player block picks from: favourite, tag, rename and delete
 * them, see which stored presets use each, and drop files anywhere on the list to upload them.
 */
@Component({
  selector: 'cth-asset-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    XuiAlertDialogImports,
    XuiButtonImports,
    XuiDialogImports,
    XuiFormFieldImports,
    XuiIconImports,
    XuiInputImports,
    XuiMenuImports,
    XuiNonIdealStateImports,
    XuiTableImports,
    XuiTooltipImports
  ],
  providers: [provideIcons({ matStarOutlineRound, matStarRound })],
  host: {
    class: 'relative flex min-h-0 flex-1 flex-col',
    '(dragover)': 'dragOver($event)',
    '(dragleave)': 'dropping.set(false)',
    '(drop)': 'drop($event)'
  },
  template: `
    @if (assets.all().length === 0) {
      <xui-non-ideal-state
        class="flex-1"
        [title]="assets.kind() === 'amps' ? 'No amp captures yet' : 'No cab IRs yet'"
        [description]="
          assets.kind() === 'amps'
            ? 'Upload .nam or .aidax models, or drop them here. Every Amp NAM block picks from this library.'
            : 'Upload .wav IRs or .cabgrid packs, or drop them here. Every Cab and IR Player block picks from this library.'
        "
      />
    } @else {
      <div class="min-h-0 flex-1 overflow-auto px-6 pt-4">
        <xui-table
          interactive
          class="w-full"
          data-testid="asset-list"
          [attr.aria-label]="assets.kind() === 'amps' ? 'Amp captures' : 'Cab IRs'"
        >
          <xui-tr class="bg-background sticky top-0 z-10">
            <xui-th class="w-10" aria-label="Favourite" />
            <xui-th class="min-w-0 flex-1">Name</xui-th>
            <xui-th class="w-24">Type</xui-th>
            <xui-th class="w-40">Used in</xui-th>
            <xui-th class="w-24 justify-end">Size</xui-th>
            <xui-th class="w-32 justify-end">Added</xui-th>
          </xui-tr>
          @for (asset of assets.rows(); track asset.ref; let i = $index) {
            <xui-tr
              tabindex="0"
              class="cursor-default"
              [class.cth-selected-row]="assets.selected().has(asset.ref)"
              [attr.data-state]="assets.selected().has(asset.ref) ? 'selected' : null"
              [attr.aria-selected]="assets.selected().has(asset.ref)"
              [attr.data-testid]="'asset-row-' + asset.ref"
              (click)="assets.select(i, $event)"
              (dblclick)="openRename(asset)"
              (keydown.enter)="openRename(asset)"
              (keydown.delete)="confirmDelete.set(true)"
              (keydown.backspace)="confirmDelete.set(true)"
            >
              <xui-td class="w-10">
                <button
                  type="button"
                  class="hover:bg-active-overlay rounded p-1"
                  [class.text-warning]="asset.favorite"
                  [class.text-foreground-subtle]="!asset.favorite"
                  [attr.aria-label]="(asset.favorite ? 'Unfavourite ' : 'Favourite ') + asset.name"
                  [attr.aria-pressed]="asset.favorite"
                  (click)="
                    $event.stopPropagation(); assets.library.setAssetMetadata(asset.ref, { favorite: !asset.favorite })
                  "
                >
                  <ng-icon xui size="sm" [name]="asset.favorite ? 'matStarRound' : 'matStarOutlineRound'" />
                </button>
              </xui-td>
              <xui-td class="min-w-0 flex-1">
                <span class="flex min-w-0 items-center gap-2">
                  @if (asset.color) {
                    <span class="size-2.5 shrink-0 rounded-full" [style.background]="asset.color"></span>
                  }
                  <span class="truncate font-medium">{{ asset.name }}</span>
                </span>
              </xui-td>
              <xui-td class="text-foreground-muted w-24">{{ kind(asset) }}</xui-td>
              <xui-td class="text-foreground-muted w-40">
                @if (assets.usedBy(asset).length; as count) {
                  <span [xuiTooltip]="assets.usedBy(asset).join(', ')"
                    >{{ count }} {{ count === 1 ? 'preset' : 'presets' }}</span
                  >
                } @else {
                  —
                }
              </xui-td>
              <xui-td class="text-foreground-muted w-24 justify-end tabular-nums">{{ size(asset) }}</xui-td>
              <xui-td class="text-foreground-muted w-32 justify-end tabular-nums">{{ added(asset) }}</xui-td>
            </xui-tr>
          } @empty {
            <xui-tr>
              <xui-td class="text-foreground-muted flex-1 justify-center py-8">
                @if (assets.query()) {
                  Nothing matches “{{ assets.query() }}”.
                } @else {
                  No favourites here yet.
                }
              </xui-td>
            </xui-tr>
          }
        </xui-table>
      </div>
      <footer class="border-border bg-surface flex items-center gap-2 border-t px-6 py-3">
        <span class="text-foreground-muted mr-auto text-sm">
          @if (assets.selectedAssets().length === 1) {
            <span class="text-foreground font-medium">{{ assets.selectedAssets()[0].name }}</span>
          } @else if (assets.selectedAssets().length > 1) {
            {{ assets.selectedAssets().length }} selected
          } @else {
            {{ count() }} · drop files here to upload
          }
        </span>
        <button
          xuiButton
          type="button"
          variant="ghost"
          [disabled]="assets.selectedAssets().length !== 1"
          (click)="openRename(assets.selectedAssets()[0])"
        >
          Rename…
        </button>
        <button
          xuiButton
          type="button"
          variant="ghost"
          [disabled]="assets.selectedAssets().length === 0"
          [xuiMenuTriggerFor]="colors"
        >
          Colour
        </button>
        <button
          xuiButton
          type="button"
          variant="ghost"
          color="error"
          data-testid="delete-assets"
          [disabled]="assets.selectedAssets().length === 0"
          (click)="confirmDelete.set(true)"
        >
          Delete
        </button>
      </footer>
    }

    @if (dropping()) {
      <div
        class="border-primary bg-primary/10 pointer-events-none absolute inset-3 flex items-center justify-center rounded-xl border-2 border-dashed"
      >
        <span class="text-primary text-sm font-medium">Drop .nam, .aidax, .wav or .cabgrid files to upload</span>
      </div>
    }

    <ng-template #colors>
      <xui-menu>
        <button xuiMenuItem (click)="color('')">No colour</button>
        @for (c of tagColors; track c) {
          <button xuiMenuItem (click)="color(c)">
            <span class="me-2 inline-block size-3 rounded-full" [style.background]="c"></span>{{ tagColorName(c) }}
          </button>
        }
      </xui-menu>
    </ng-template>

    @if (renaming(); as asset) {
      <xui-dialog title="Rename" [open]="true" (openChange)="!$event && renaming.set(null)">
        <xui-dialog-body>
          <xui-form-field label="Name">
            <input
              xuiInput
              class="w-full"
              data-testid="asset-name"
              [value]="newName()"
              (input)="newName.set($any($event.target).value)"
              (keydown.enter)="rename(asset)"
            />
          </xui-form-field>
        </xui-dialog-body>
        <xui-dialog-footer>
          <button xuiButton variant="ghost" type="button" (click)="renaming.set(null)">Cancel</button>
          <button xuiButton type="button" [disabled]="!newName().trim()" (click)="rename(asset)">Rename</button>
        </xui-dialog-footer>
      </xui-dialog>
    }

    <xui-alert-dialog
      destructive
      [title]="
        assets.selectedAssets().length === 1
          ? 'Delete “' + assets.selectedAssets()[0].name + '”?'
          : 'Delete ' + assets.selectedAssets().length + ' files?'
      "
      [description]="deleteWarning()"
      confirmText="Delete"
      [(open)]="confirmDelete"
      (confirmed)="delete()"
    />
  `
})
export class AssetList {
  protected readonly assets = inject(AssetLibrary);
  protected readonly tagColors = TagColors;
  protected readonly tagColorName = tagColorName;
  protected readonly dropping = signal(false);
  protected readonly renaming = signal<AssetInfo | null>(null);
  protected readonly newName = signal('');
  protected readonly confirmDelete = signal(false);
  protected readonly kind = kindName;
  protected readonly count = computed(() => {
    const n = this.assets.rows().length;
    return this.assets.kind() === 'amps' ? `${n} capture${n === 1 ? '' : 's'}` : `${n} IR${n === 1 ? '' : 's'}`;
  });

  protected readonly deleteWarning = computed(() => {
    const chosen = this.assets.selectedAssets();
    const presets = [...new Set(chosen.flatMap(a => this.assets.usedBy(a)))];
    const it = chosen.length === 1 ? 'it' : 'them';
    return presets.length === 0
      ? `No stored preset uses ${it}. Deleting removes ${it} from the library for good.`
      : `Used by ${presets.length} stored preset${presets.length === 1 ? '' : 's'} (${presets.slice(0, 5).join(', ')}` +
          `${presets.length > 5 ? ', …' : ''}): their blocks lose ${it} and play nothing in that slot.`;
  });

  protected size(asset: AssetInfo): string {
    return asset.size >= 1024 * 1024
      ? `${(asset.size / 1024 / 1024).toFixed(1)} MB`
      : `${Math.max(1, Math.round(asset.size / 1024))} KB`;
  }

  protected added(asset: AssetInfo): string {
    return new Date(asset.added).toLocaleDateString(undefined, { dateStyle: 'medium' });
  }

  protected openRename(asset: AssetInfo): void {
    this.newName.set(asset.name);
    this.renaming.set(asset);
  }

  protected async rename(asset: AssetInfo): Promise<void> {
    const name = this.newName().trim();
    if (name && (await this.assets.library.setAssetMetadata(asset.ref, { name }))) {
      this.renaming.set(null);
    }
  }

  protected async color(color: string): Promise<void> {
    for (const asset of this.assets.selectedAssets()) {
      await this.assets.library.setAssetMetadata(asset.ref, { color });
    }
  }

  protected async delete(): Promise<void> {
    await this.assets.library.deleteAssets(this.assets.selectedAssets().map(a => a.ref));
    await this.assets.library.refreshUsage();
    this.assets.selected.set(new Set());
  }

  protected dragOver(event: DragEvent): void {
    if (event.dataTransfer?.types.includes('Files')) {
      event.preventDefault();
      this.dropping.set(true);
    }
  }

  protected async drop(event: DragEvent): Promise<void> {
    if (!event.dataTransfer?.types.includes('Files')) {
      return;
    }

    event.preventDefault();
    this.dropping.set(false);
    await this.assets.upload([...event.dataTransfer.files]);
  }
}
