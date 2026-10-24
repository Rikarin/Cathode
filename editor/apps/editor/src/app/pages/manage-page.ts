import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { type AssetInfo, type Bank, unwrap } from '@cathode/api-client';
import { provideIcons } from '@ng-icons/core';
import { matAddRound, matStarRound } from '@ng-icons/material-icons/round';
import { XuiAlertDialogImports } from '@xui/alert-dialog';
import { XuiButtonImports } from '@xui/button';
import { XuiDialogImports } from '@xui/dialog';
import { XuiFormFieldImports } from '@xui/form-field';
import { XuiIconImports } from '@xui/icon';
import { XuiInputImports } from '@xui/input';
import { XuiMenuImports } from '@xui/menu';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { ConnectionService } from '../core/connection';
import { Notify } from '../core/notify';
import { TagColors } from '../shell/picker-list';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';
import { AssetSession, ManageSession, type SlotRef, nextSelection } from './manage';

const SlotMime = 'application/x-cathode-slot';
const PresetMime = 'application/x-cathode-preset';

/**
 * Manage Presets and Manage IRs. Presets sit in bank slots: click selects (⇧ extends, ⌘ toggles), drag swaps two
 * slots, ⌘-drag moves, ⇧-drag copies, within a bank or onto another bank in the list (its first empty slot). Rename,
 * duplicate, delete, colour and favourite act on the selection. Every change is held, and marked, until Save; Close
 * discards them. Reset Amp blocks acts on the stored presets at once, after a confirmation.
 */
@Component({
  selector: 'cth-manage-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    XuiAlertDialogImports,
    XuiButtonImports,
    XuiDialogImports,
    XuiFormFieldImports,
    XuiIconImports,
    XuiInputImports,
    XuiMenuImports,
    XuiSegmentedControlImports
  ],
  providers: [provideIcons({ matAddRound, matStarRound })],
  host: { class: 'flex h-full flex-col gap-4 p-6' },
  template: `
    <header class="flex flex-wrap items-center gap-3">
      <h1 class="mr-2 text-lg font-semibold">Manage</h1>
      <xui-segmented-control
        size="sm"
        aria-label="What to manage"
        [options]="tabs"
        [value]="tab()"
        (valueChange)="$event && tab.set($event)"
      />
      <span class="flex-1"></span>
      @if (dirty()) {
        <span class="text-warning text-sm" data-testid="manage-dirty">Unsaved changes</span>
      }
      <button xuiButton variant="ghost" type="button" (click)="close()">Close</button>
      <button xuiButton type="button" data-testid="manage-save" [disabled]="!dirty() || saving()" (click)="save()">
        Save
      </button>
    </header>

    @if (tab() === 'presets') {
      <div class="flex min-h-0 flex-1 gap-4">
        <aside class="flex w-56 shrink-0 flex-col gap-3">
          <div class="flex items-center justify-between">
            <span class="text-foreground-muted text-[11px] font-semibold tracking-wide uppercase">Banks</span>
            <button xuiButton size="sm" variant="ghost" aria-label="New bank" (click)="addBank()">
              <ng-icon xui name="matAddRound" size="sm" />
            </button>
          </div>
          <ul class="flex flex-col gap-0.5 text-sm" aria-label="Banks">
            @for (bank of banks(); track bank.id) {
              <li
                class="hover:bg-active-overlay cursor-pointer rounded px-2 py-1"
                [class.bg-active-overlay]="bank.id === bankId()"
                (click)="selectBank(bank.id)"
                (dragover)="$event.preventDefault()"
                (drop)="dropOnBank($event, bank.id)"
              >
                {{ bank.name }}
              </li>
            } @empty {
              <li class="text-foreground-muted text-xs">No banks yet: add one.</li>
            }
          </ul>
          <span class="text-foreground-muted mt-2 text-[11px] font-semibold tracking-wide uppercase">Library</span>
          <ul class="flex min-h-0 flex-1 flex-col overflow-y-auto text-sm" aria-label="Presets to place">
            @for (p of library.ordered(); track p.id) {
              <li
                class="hover:bg-active-overlay cursor-grab truncate rounded px-2 py-0.5"
                draggable="true"
                (dragstart)="dragPreset($event, p.id)"
              >
                {{ p.name }}
              </li>
            }
          </ul>
        </aside>

        <section class="flex min-w-0 flex-1 flex-col gap-3">
          <div class="flex flex-wrap items-center gap-2" role="toolbar" aria-label="Selected presets">
            <span class="text-foreground-muted text-sm">{{ selectedIds().length }} selected</span>
            <button
              xuiButton
              size="sm"
              variant="outline"
              [disabled]="selectedIds().length !== 1"
              (click)="openRename()"
            >
              Rename…
            </button>
            <button xuiButton size="sm" variant="outline" [disabled]="selectedIds().length === 0" (click)="duplicate()">
              Duplicate
            </button>
            <button
              xuiButton
              size="sm"
              variant="outline"
              [disabled]="selectedIds().length === 0"
              [xuiMenuTriggerFor]="colors"
            >
              Colour
            </button>
            <button xuiButton size="sm" variant="outline" [disabled]="selectedIds().length === 0" (click)="favorite()">
              <ng-icon xui name="matStarRound" size="sm" /> Favourite
            </button>
            <button
              xuiButton
              size="sm"
              variant="outline"
              color="error"
              [disabled]="selectedIds().length === 0"
              (click)="remove()"
            >
              Delete
            </button>
            <span class="flex-1"></span>
            <button
              xuiButton
              size="sm"
              variant="outline"
              data-testid="reset-amps"
              [disabled]="storedSelection().length === 0"
              (click)="confirmReset.set(true)"
            >
              Reset Amp blocks…
            </button>
          </div>
          @if (bank(); as b) {
            <div
              class="grid min-h-0 flex-1 grid-cols-[repeat(auto-fill,minmax(9rem,1fr))] content-start gap-1.5 overflow-y-auto"
              role="listbox"
              aria-multiselectable="true"
              [attr.aria-label]="b.name"
            >
              @for (slot of slots(); track slot.index) {
                <div
                  role="option"
                  class="border-border-muted relative flex h-12 cursor-pointer flex-col justify-center rounded-md border px-2 text-xs"
                  [class.bg-active-overlay]="selected().has(slot.index)"
                  [class.border-primary]="selected().has(slot.index)"
                  [attr.aria-selected]="selected().has(slot.index)"
                  [attr.data-testid]="'slot-' + slot.index"
                  [attr.draggable]="slot.id !== null"
                  (click)="select(slot.index, $event)"
                  (dragstart)="dragSlot($event, slot.index)"
                  (dragover)="$event.preventDefault()"
                  (drop)="dropOnSlot($event, slot.index)"
                >
                  <span class="text-foreground-subtle tabular-nums">{{ slot.index + 1 }}</span>
                  <span class="truncate font-medium">{{ slot.name }}</span>
                  @if (slot.color) {
                    <span class="absolute top-1 right-1.5 size-2 rounded-full" [style.background]="slot.color"></span>
                  }
                  @if (slot.changed) {
                    <span class="text-warning absolute right-1.5 bottom-0.5 text-[10px]" aria-label="changed">●</span>
                  }
                </div>
              }
            </div>
          }
          <p class="text-foreground-muted text-xs">
            Drag swaps two slots; ⌘-drag moves, ⇧-drag copies. Drop on a bank in the list for its first empty slot.
          </p>
        </section>
      </div>
    } @else {
      <div class="flex min-h-0 flex-1 flex-col gap-3">
        <div class="flex flex-wrap items-center gap-2" role="toolbar" aria-label="Selected IRs">
          <span class="text-foreground-muted text-sm">{{ selectedIrs().length }} selected</span>
          <button xuiButton size="sm" variant="outline" [disabled]="selectedIrs().length !== 1" (click)="openRename()">
            Rename…
          </button>
          <button
            xuiButton
            size="sm"
            variant="outline"
            [disabled]="selectedIrs().length === 0"
            [xuiMenuTriggerFor]="colors"
          >
            Colour
          </button>
          <button xuiButton size="sm" variant="outline" [disabled]="selectedIrs().length === 0" (click)="favorite()">
            <ng-icon xui name="matStarRound" size="sm" /> Favourite
          </button>
          <button
            xuiButton
            size="sm"
            variant="outline"
            color="error"
            [disabled]="selectedIrs().length === 0"
            (click)="remove()"
          >
            Delete
          </button>
        </div>
        <ul
          class="flex min-h-0 flex-1 flex-col overflow-y-auto text-sm"
          role="listbox"
          aria-multiselectable="true"
          aria-label="Impulse responses"
        >
          @for (ir of irs(); track ir.ref; let i = $index) {
            <li
              role="option"
              class="flex cursor-pointer items-center gap-2 rounded px-2 py-1"
              [class.bg-active-overlay]="irSelected().has(i)"
              [class.line-through]="ir.deleted"
              [attr.aria-selected]="irSelected().has(i)"
              (click)="selectIr(i, $event)"
            >
              <span
                class="size-2.5 rounded-full border border-current/30"
                [style.background]="ir.color ?? 'transparent'"
              ></span>
              <span class="flex-1 truncate">{{ ir.name }}</span>
              @if (ir.favorite) {
                <ng-icon xui name="matStarRound" size="sm" class="text-warning" />
              }
              @if (ir.changed) {
                <span class="text-warning text-[10px]" aria-label="changed">●</span>
              }
            </li>
          }
        </ul>
      </div>
    }

    <ng-template #colors>
      <xui-menu>
        <button xuiMenuItem (click)="color('')">No colour</button>
        @for (c of tagColors; track c) {
          <button xuiMenuItem (click)="color(c)">
            <span class="me-2 inline-block size-3 rounded-full" [style.background]="c"></span>{{ c }}
          </button>
        }
      </xui-menu>
    </ng-template>

    <xui-dialog title="Rename" [(open)]="renaming">
      <xui-dialog-body>
        <xui-form-field label="Name">
          <input
            xuiInput
            [value]="newName()"
            (input)="newName.set($any($event.target).value)"
            (keydown.enter)="rename()"
          />
        </xui-form-field>
      </xui-dialog-body>
      <xui-dialog-footer>
        <button xuiButton variant="ghost" type="button" (click)="renaming.set(false)">Cancel</button>
        <button xuiButton type="button" [disabled]="!newName().trim()" (click)="rename()">Rename</button>
      </xui-dialog-footer>
    </xui-dialog>

    <xui-alert-dialog
      title="Reset Amp blocks?"
      [description]="
        'Every Circuit Amp setting in ' +
        storedSelection().length +
        ' preset(s) goes back to its default, except the amp\\'s own front-panel knobs and switches. This changes the stored presets now.'
      "
      confirmText="Reset"
      [(open)]="confirmReset"
      (confirmed)="resetAmps()"
    />
  `
})
export class ManagePage {
  protected readonly library = inject(LibraryStore);
  private readonly live = inject(LiveStore);
  private readonly connection = inject(ConnectionService);
  private readonly notify = inject(Notify);
  private readonly router = inject(Router);

  protected readonly tagColors = TagColors;
  protected readonly tabs = [
    { label: 'Presets', value: 'presets' as const },
    { label: 'IRs', value: 'irs' as const }
  ];
  protected readonly tab = signal<'presets' | 'irs'>('presets');
  /** Bumped after every change to the (mutable) sessions, so the view recomputes. */
  private readonly version = signal(0);
  private readonly session = signal(new ManageSession([], []));
  private readonly assets = signal(new AssetSession());
  protected readonly bankId = signal<string | null>(null);
  protected readonly selected = signal<ReadonlySet<number>>(new Set());
  private anchor: number | null = null;
  protected readonly irSelected = signal<ReadonlySet<number>>(new Set());
  private irAnchor: number | null = null;
  protected readonly renaming = signal(false);
  protected readonly newName = signal('');
  protected readonly confirmReset = signal(false);
  protected readonly saving = signal(false);

  protected readonly dirty = computed(() => {
    this.version();
    return this.session().dirty || this.assets().dirty;
  });
  protected readonly banks = computed(() => {
    this.version();
    return [...this.session().banks];
  });
  protected readonly bank = computed(() => this.banks().find(b => b.id === this.bankId()) ?? this.banks()[0] ?? null);
  protected readonly slots = computed(() => {
    this.version();
    const bank = this.bank();
    const session = this.session();
    return (bank?.slots ?? []).map((id, index) => {
      const ref = { bank: bank!.id, slot: index };
      return {
        index,
        id: id ?? null,
        name: id ? session.nameOf(id) : '',
        color: id ? (session.meta.get(id)?.color ?? session.presets.get(id)?.color ?? null) : null,
        changed: session.isChanged(ref)
      };
    });
  });
  protected readonly selectedIds = computed(() =>
    [...this.selected()].map(i => this.slots()[i]?.id).filter((id): id is string => !!id)
  );
  /** The selected presets that exist on the Server already (not copies waiting for Save). */
  protected readonly storedSelection = computed(() => this.selectedIds().filter(id => this.session().presets.has(id)));
  protected readonly irs = computed(() => {
    this.version();
    const session = this.assets();
    return this.library.impulseResponses().map((ir: AssetInfo) => {
      const meta = session.meta.get(ir.ref);
      return {
        ref: ir.ref,
        name: meta?.name ?? ir.name,
        color: meta?.color !== undefined ? meta.color || null : (ir.color ?? null),
        favorite: meta?.favorite ?? ir.favorite ?? false,
        changed: session.isChanged(ir.ref),
        deleted: session.deleted.has(ir.ref)
      };
    });
  });
  protected readonly selectedIrs = computed(() => [...this.irSelected()].map(i => this.irs()[i]?.ref).filter(Boolean));

  constructor() {
    void this.reload();
  }

  protected selectBank(id: string): void {
    this.bankId.set(id);
    this.selected.set(new Set());
    this.anchor = null;
  }

  protected select(index: number, event: MouseEvent): void {
    const next = nextSelection(this.selected(), this.anchor, index, {
      shift: event.shiftKey,
      toggle: event.metaKey || event.ctrlKey
    });
    this.selected.set(next.selected);
    this.anchor = next.anchor;
  }

  protected selectIr(index: number, event: MouseEvent): void {
    const next = nextSelection(this.irSelected(), this.irAnchor, index, {
      shift: event.shiftKey,
      toggle: event.metaKey || event.ctrlKey
    });
    this.irSelected.set(next.selected);
    this.irAnchor = next.anchor;
  }

  protected addBank(): void {
    const bank = this.session().addBank(`Bank ${this.banks().length + 1}`);
    this.changed();
    this.selectBank(bank.id);
  }

  protected dragSlot(event: DragEvent, slot: number): void {
    event.dataTransfer?.setData(SlotMime, JSON.stringify({ bank: this.bank()!.id, slot }));
  }

  protected dragPreset(event: DragEvent, id: string): void {
    event.dataTransfer?.setData(PresetMime, id);
  }

  protected dropOnSlot(event: DragEvent, slot: number): void {
    event.preventDefault();
    const bank = this.bank();
    if (bank) {
      this.dropAt(event, { bank: bank.id, slot });
    }
  }

  protected dropOnBank(event: DragEvent, bankId: string): void {
    event.preventDefault();
    const empty = this.session()
      .bank(bankId)
      .slots.findIndex(s => s === null);
    if (empty >= 0) {
      this.dropAt(event, { bank: bankId, slot: empty });
    }
  }

  protected openRename(): void {
    const id = this.tab() === 'presets' ? this.selectedIds()[0] : this.selectedIrs()[0];
    this.newName.set(
      this.tab() === 'presets' ? this.session().nameOf(id) : (this.irs().find(ir => ir.ref === id)?.name ?? '')
    );
    this.renaming.set(true);
  }

  protected rename(): void {
    const name = this.newName().trim();
    if (!name) {
      return;
    }

    if (this.tab() === 'presets') {
      this.session().rename(this.selectedIds()[0], name);
    } else {
      this.assets().change([this.selectedIrs()[0]], { name });
    }

    this.renaming.set(false);
    this.changed();
  }

  protected duplicate(): void {
    const bank = this.bank();
    if (bank) {
      this.session().duplicateSlots([...this.selected()].map(slot => ({ bank: bank.id, slot })));
      this.changed();
    }
  }

  protected color(color: string): void {
    if (this.tab() === 'presets') {
      this.selectedIds().forEach(id => this.session().setMeta(id, { color }));
    } else {
      this.assets().change(this.selectedIrs(), { color });
    }

    this.changed();
  }

  protected favorite(): void {
    if (this.tab() === 'presets') {
      const session = this.session();
      const all = this.selectedIds().every(id => session.meta.get(id)?.favorite ?? session.presets.get(id)?.favorite);
      this.selectedIds().forEach(id => session.setMeta(id, { favorite: !all }));
    } else {
      const all = this.selectedIrs().every(ref => this.irs().find(ir => ir.ref === ref)?.favorite);
      this.assets().change(this.selectedIrs(), { favorite: !all });
    }

    this.changed();
  }

  protected remove(): void {
    if (this.tab() === 'presets') {
      this.session().delete(this.selectedIds());
      this.selected.set(new Set());
    } else {
      this.assets().delete(this.selectedIrs());
    }

    this.changed();
  }

  protected async resetAmps(): Promise<void> {
    const ids = this.storedSelection();
    const result = await this.notify.run(async () =>
      unwrap(await this.connection.client.POST('/api/v1/presets/reset-amps', { body: { presets: ids } }))
    );
    if (result) {
      this.notify.success(`Reset the Amp blocks of ${result.changed.length} preset(s).`);
      const live = this.live.state();
      if (live && result.changed.includes(live.preset.id) && !live.dirty) {
        await this.live.load(live.preset.id); // show the reset in the working copy too
      }
    }
  }

  /** Carries out the session: copies, renames and metadata, banks, deletions, then IRs. */
  protected async save(): Promise<void> {
    const plan = this.session().plan();
    const client = this.connection.client;
    this.saving.set(true);
    const done = await this.notify.run(async () => {
      for (const copy of plan.copies) {
        unwrap(
          await client.POST('/api/v1/presets/{id}/duplicate', {
            params: { path: { id: copy.source } },
            body: { id: copy.id, name: copy.name }
          })
        );
      }

      for (const [id, meta] of plan.meta) {
        if (!plan.copies.some(c => c.id === id)) {
          await this.patchPreset(id, meta);
        } else if (meta.color !== undefined || meta.favorite !== undefined) {
          await this.patchPreset(id, { color: meta.color, favorite: meta.favorite });
        }
      }

      for (const bank of plan.banks) {
        // Empty slots are null on the wire (see WorkBank).
        unwrap(await client.PUT('/api/v1/banks/{id}', { params: { path: { id: bank.id } }, body: bank as Bank }));
      }

      for (const id of plan.deletes) {
        unwrap(await client.DELETE('/api/v1/presets/{id}', { params: { path: { id } } }));
      }

      const assets = this.assets();
      for (const [reference, meta] of assets.meta) {
        unwrap(
          await client.PATCH('/api/v1/assets/{reference}/metadata', {
            params: { path: { reference } },
            body: { name: meta.name ?? null, color: meta.color ?? null, favorite: meta.favorite ?? null }
          })
        );
      }

      for (const reference of assets.deleted) {
        unwrap(await client.DELETE('/api/v1/assets/{reference}', { params: { path: { reference } } }));
      }

      return true;
    });
    this.saving.set(false);
    if (done) {
      this.notify.success('Saved.');
    }

    await this.reload();
  }

  /** Close discards whatever is not saved. */
  protected async close(): Promise<void> {
    await this.router.navigateByUrl('/');
  }

  private dropAt(event: DragEvent, to: SlotRef): void {
    const session = this.session();
    const slot = event.dataTransfer?.getData(SlotMime);
    const preset = event.dataTransfer?.getData(PresetMime);
    if (slot) {
      const from = JSON.parse(slot) as SlotRef;
      if (event.shiftKey) {
        session.copy(from, to);
      } else if (event.metaKey || event.ctrlKey) {
        session.move(from, to);
      } else if (from.bank !== to.bank || from.slot !== to.slot) {
        session.swap(from, to);
      }
    } else if (preset) {
      session.assign(preset, to);
    }

    this.changed();
  }

  private async patchPreset(id: string, meta: { name?: string; color?: string; favorite?: boolean }): Promise<void> {
    unwrap(
      await this.connection.client.PATCH('/api/v1/presets/{id}/metadata', {
        params: { path: { id } },
        body: { name: meta.name ?? null, color: meta.color ?? null, favorite: meta.favorite ?? null }
      })
    );
  }

  private async reload(): Promise<void> {
    await Promise.all([this.library.refreshPresets(), this.library.refreshBanks(), this.library.refreshAssets()]);
    this.session.set(new ManageSession(this.library.banks(), this.library.presets()));
    this.assets.set(new AssetSession());
    this.selected.set(new Set());
    this.irSelected.set(new Set());
    this.changed();
  }

  private changed(): void {
    this.version.update(v => v + 1);
  }
}
