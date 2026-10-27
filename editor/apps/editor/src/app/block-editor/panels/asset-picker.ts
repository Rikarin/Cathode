import {
  ChangeDetectionStrategy,
  Component,
  type ElementRef,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild
} from '@angular/core';
import type { AssetInfo } from '@cathode/api-client';
import { provideIcons } from '@ng-icons/core';
import { matUploadRound } from '@ng-icons/material-icons/round';
import { XuiButtonImports } from '@xui/button';
import { XuiDialogImports } from '@xui/dialog';
import { XuiSelectImports } from '@xui/select';
import { type PickerEntry, PickerList } from '../../shell/picker-list';
import { LibraryStore } from '../../state/library.store';

/** Picks an asset of one family for a block slot, or uploads a new file (which is then picked). */
@Component({
  selector: 'cth-asset-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PickerList, XuiButtonImports, XuiDialogImports, XuiSelectImports],
  providers: [provideIcons({ matUploadRound })],
  host: { class: 'flex flex-col gap-1' },
  template: `
    <span class="text-foreground-muted text-xs" [id]="labelId()">{{ label() }}</span>
    <div class="flex items-center gap-2">
      <xui-select
        class="w-64"
        placeholder="None"
        [attr.aria-labelledby]="labelId()"
        [items]="assets()"
        [itemText]="name"
        [value]="current()"
        (valueChange)="$event && picked.emit($event)"
      />
      <button xuiButton variant="outline" size="sm" type="button" (click)="browsing.set(true)">Browse…</button>
      <button xuiButton variant="outline" size="sm" type="button" (click)="file.click()">Upload…</button>
      <input #file type="file" class="hidden" [accept]="accept()" (change)="upload(file)" />
    </div>
    <xui-dialog class="w-[min(36rem,calc(100vw-2rem))] max-w-none" [title]="label()" [(open)]="browsing">
      <xui-dialog-body class="flex min-h-0 flex-col">
        <cth-picker-list
          [noun]="label()"
          [entries]="entries()"
          [current]="reference() ?? null"
          (chosen)="choose($event)"
          (favorite)="library.setAssetMetadata($event.entry.id, { favorite: $event.favorite })"
          (color)="library.setAssetMetadata($event.entry.id, { color: $event.color })"
        />
      </xui-dialog-body>
    </xui-dialog>
  `
})
export class AssetPicker {
  protected readonly library = inject(LibraryStore);
  private readonly list = viewChild(PickerList);
  private readonly fileInput = viewChild.required<ElementRef<HTMLInputElement>>('file');

  readonly label = input.required<string>();
  readonly assets = input.required<readonly AssetInfo[]>();
  readonly reference = input<string | undefined>();
  readonly accept = input('');
  readonly picked = output<AssetInfo>();

  protected readonly labelId = computed(() => `asset-${this.label().replace(/\W+/g, '-').toLowerCase()}`);
  protected readonly current = computed(() => this.assets().find(a => a.ref === this.reference()) ?? null);
  protected readonly name = (asset: AssetInfo) => (asset.favorite ? '★ ' : '') + asset.name;
  /** The browser: search, favourites, colour tags, and a pin to keep it open while auditioning IRs. */
  protected readonly browsing = signal(false);
  protected readonly entries = computed<PickerEntry[]>(() =>
    this.assets().map(a => ({ id: a.ref, name: a.name, detail: a.kind, color: a.color ?? null, favorite: a.favorite }))
  );

  protected choose(entry: PickerEntry): void {
    const asset = this.assets().find(a => a.ref === entry.id);
    if (asset) {
      this.picked.emit(asset);
    }

    if (!this.list()?.pinned()) {
      this.browsing.set(false);
    }
  }

  constructor() {
    // Assets can arrive from elsewhere (another editor, a preset import); show the store as it is now.
    void this.library.refreshAssets();
  }

  protected async upload(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    input.value = '';
    if (file) {
      const asset = await this.library.upload(file);
      if (asset) {
        this.picked.emit(asset);
      }
    }

    this.fileInput().nativeElement.blur();
  }
}
