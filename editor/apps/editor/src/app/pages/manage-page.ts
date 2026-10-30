import { ScrollingModule } from '@angular/cdk/scrolling';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { type Bank, type PresetSummary, unwrap } from '@cathode/api-client';
import { provideIcons } from '@ng-icons/core';
import { matAddRound, matStarRound } from '@ng-icons/material-icons/round';
import { XuiAlertDialogImports } from '@xui/alert-dialog';
import { XuiButtonImports } from '@xui/button';
import { XuiContextMenuImports } from '@xui/context-menu';
import { XuiDialogImports } from '@xui/dialog';
import { XuiFormFieldImports } from '@xui/form-field';
import { XuiIconImports } from '@xui/icon';
import { XuiInputImports } from '@xui/input';
import { XuiMenuImports } from '@xui/menu';
import { ConnectionService } from '../core/connection';
import { Notify } from '../core/notify';
import { TagColors, tagColorName } from '../shell/picker-list';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';
import { ManageSession, type SlotRef, nextSelection } from './manage';

const SlotMime = 'application/x-cathode-slot';
const PresetMime = 'application/x-cathode-preset';
/** The height of an entry in the Library list, which the virtual scroll lays the list out by. */
export const LibraryEntryHeight = 24;

/**
 * Manage Presets (IRs and amp captures are managed in the Library). Presets sit in bank slots: click selects (⇧ extends, ⌘ toggles), drag swaps two
 * slots, ⌘-drag moves, ⇧-drag copies, within a bank or onto another bank in the list (its first empty slot). Rename,
 * duplicate, delete, colour and favourite act on the selection. A bank's right-click menu renames or deletes it
 * (double-click renames). Creating, renaming and deleting banks happen at once rather than on Save. Every change is held, and marked, until Save; Close
 * discards them. Reset Amp blocks acts on the stored presets at once, after a confirmation. The factory bank is
 * read-only: drag its presets onto a bank of your own (⇧ for a copy you can change); they cannot be renamed or deleted.
 */
@Component({
  selector: 'cth-manage-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ScrollingModule,
    XuiAlertDialogImports,
    XuiButtonImports,
    XuiContextMenuImports,
    XuiDialogImports,
    XuiFormFieldImports,
    XuiIconImports,
    XuiInputImports,
    XuiMenuImports
  ],
  providers: [provideIcons({ matAddRound, matStarRound })],
  host: { class: 'flex h-full flex-col gap-4 p-6' },
  template: `
    <header class="flex flex-wrap items-center gap-3">
      <h1 class="mr-2 text-lg font-semibold">Manage presets</h1>
      <span class="flex-1"></span>
      @if (dirty()) {
        <span class="text-warning text-sm" data-testid="manage-dirty">Unsaved changes</span>
      }
      <button xuiButton variant="ghost" type="button" (click)="close()">Close</button>
      <button xuiButton type="button" data-testid="manage-save" [disabled]="!dirty() || saving()" (click)="save()">
        Save
      </button>
    </header>

    <div class="flex min-h-0 flex-1 gap-4">
      <aside class="flex w-56 shrink-0 flex-col gap-4">
        <div class="pane flex max-h-[45%] shrink-0 flex-col">
          <div class="pane-header flex h-9 shrink-0 items-center justify-between ps-3 pe-1">
            <span class="section-label">Banks</span>
            <button type="button" xuiButton size="sm" variant="ghost" aria-label="New bank" (click)="addBank()">
              <ng-icon xui name="matAddRound" size="sm" />
            </button>
          </div>
          <ul class="flex min-h-0 flex-col gap-0.5 overflow-y-auto p-1.5 text-sm" aria-label="Banks">
            @for (bank of banks(); track bank.id) {
              <li
                class="group data-selected:bg-primary not-data-selected:hover:bg-hover-overlay flex h-7 shrink-0 cursor-pointer items-center gap-1.5 rounded-[5px] px-2 data-selected:font-medium data-selected:text-white"
                [attr.data-selected]="bank.id === shownBankId() || null"
                [attr.data-testid]="'bank-' + bank.id"
                [xuiContextMenuTriggerFor]="bankActions"
                [xuiContextMenuDisabled]="bank.factory ?? false"
                [xuiContextMenuTriggerData]="{ id: bank.id }"
                tabindex="0"
                (click)="selectBank(bank.id)"
                (keydown.enter)="selectBank(bank.id)"
                (keydown.space)="$event.preventDefault(); selectBank(bank.id)"
                (dblclick)="!bank.factory && openRenameBank(bank.id)"
                (contextmenu)="selectBank(bank.id)"
                (dragover)="$event.preventDefault()"
                (drop)="dropOnBank($event, bank.id)"
              >
                <span class="min-w-0 flex-1 truncate">{{ bank.name }}</span>
                @if (bank.factory) {
                  <!-- A capsule like the presets list's chips; translucent white on the selected (accent) row. -->
                  <span
                    class="text-foreground-muted flex h-4.5 shrink-0 items-center rounded-full border border-white/8 bg-white/6 px-1.5 text-[10px] font-medium group-data-selected:border-white/30 group-data-selected:bg-white/18 group-data-selected:text-white"
                    >read-only</span
                  >
                }
              </li>
            } @empty {
              <li class="text-foreground-muted px-2 py-1 text-xs">No banks yet: add one.</li>
            }
          </ul>
        </div>
        <div class="pane flex min-h-0 flex-1 flex-col">
          <div class="pane-header flex h-9 shrink-0 items-center px-3">
            <span class="section-label">Library</span>
          </div>
          <!--
          Only the entries in view are rendered. The rows' wrapper is held to the viewport's width (it would size to the
          longest name), with the side padding on it.
        -->
          <cdk-virtual-scroll-viewport
            class="min-h-0 flex-1 py-1.5 text-sm [&_.cdk-virtual-scroll-content-wrapper]:w-full [&_.cdk-virtual-scroll-content-wrapper]:px-1.5"
            role="list"
            aria-label="Presets to place"
            data-testid="library-presets"
            [itemSize]="entryHeight"
          >
            <div
              *cdkVirtualFor="let p of library.ordered(); trackBy: byId; let i = index; let count = count"
              role="listitem"
              class="hover:bg-hover-overlay h-6 cursor-grab truncate rounded-[5px] px-2 py-0.5"
              draggable="true"
              [attr.aria-posinset]="i + 1"
              [attr.aria-setsize]="count"
              (dragstart)="dragPreset($event, p.id)"
            >
              {{ p.name }}
            </div>
          </cdk-virtual-scroll-viewport>
        </div>
      </aside>

      <section class="pane flex min-w-0 flex-1 flex-col">
        <div
          class="pane-header flex min-h-9 shrink-0 flex-wrap items-center gap-2 px-3 py-1"
          role="toolbar"
          aria-label="Selected presets"
        >
          <span class="text-foreground-subtle readout mr-1 text-xs">{{ selectedIds().length }} selected</span>
          <button
            type="button"
            xuiButton
            size="sm"
            variant="outline"
            [disabled]="selectedIds().length !== 1 || selectedFactory()"
            (click)="openRename()"
          >
            Rename…
          </button>
          <button
            type="button"
            xuiButton
            size="sm"
            variant="outline"
            [disabled]="selectedIds().length === 0 || factoryBank()"
            (click)="duplicate()"
          >
            Duplicate
          </button>
          <button
            type="button"
            xuiButton
            size="sm"
            variant="outline"
            [disabled]="selectedIds().length === 0"
            [xuiMenuTriggerFor]="colors"
          >
            Colour
          </button>
          <button
            type="button"
            xuiButton
            size="sm"
            variant="outline"
            [disabled]="selectedIds().length === 0"
            (click)="favorite()"
          >
            <ng-icon xui name="matStarRound" size="sm" /> Favourite
          </button>
          <button
            type="button"
            xuiButton
            size="sm"
            variant="outline"
            color="error"
            data-testid="manage-delete"
            [disabled]="selectedIds().length === 0 || selectedFactory()"
            (click)="remove()"
          >
            Delete
          </button>
          <span class="flex-1"></span>
          <button
            type="button"
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
        @if (factoryBank()) {
          <p class="text-foreground-muted shrink-0 px-3 pt-3 text-xs" data-testid="factory-bank-note">
            The factory bank is read-only. Drag its presets onto a bank of your own, or ⇧-drag for a copy you can
            change.
          </p>
        }
        @if (bank(); as b) {
          <!-- The slots in a sunken well, each a raised tile; a selected one is lit in the accent. -->
          <div
            class="border-hairline-strong m-3 grid min-h-0 flex-1 grid-cols-[repeat(auto-fill,minmax(9rem,1fr))] content-start gap-1.5 overflow-y-auto rounded-lg border bg-(--well) p-2 shadow-[inset_0_1px_3px_rgb(0_0_0/0.6),0_1px_0_var(--highlight)]"
            role="listbox"
            aria-multiselectable="true"
            [attr.aria-label]="b.name"
          >
            @for (slot of slots(); track slot.index) {
              <div
                role="option"
                class="bg-surface-raised hover:bg-surface-raised/80 aria-selected:border-primary relative flex h-12 cursor-pointer flex-col justify-center rounded-md border border-(--hairline-strong) px-2 text-xs shadow-[inset_0_1px_0_var(--highlight),0_1px_2px_rgb(0_0_0/0.4)] aria-selected:bg-[color-mix(in_oklab,var(--color-primary)_22%,var(--color-surface-raised))] aria-selected:shadow-[inset_0_1px_0_var(--highlight),0_0_0_1px_var(--color-primary)]"
                [attr.aria-selected]="selected().has(slot.index)"
                [attr.data-testid]="'slot-' + slot.index"
                [attr.draggable]="slot.id !== null"
                tabindex="0"
                (click)="select(slot.index, $event)"
                (keydown)="slotKey(slot.index, $event)"
                (dragstart)="dragSlot($event, slot.index)"
                (dragover)="$event.preventDefault()"
                (drop)="dropOnSlot($event, slot.index)"
              >
                <span class="text-foreground-subtle readout text-[10px]">{{ slot.index + 1 }}</span>
                <span class="truncate font-medium">{{ slot.name }}</span>
                @if (slot.favorite || slot.color) {
                  <span class="absolute top-1 right-1.5 flex items-center gap-1">
                    @if (slot.favorite) {
                      <ng-icon xui name="matStarRound" size="xs" class="text-warning" aria-label="favourite" />
                    }
                    @if (slot.color) {
                      <span class="size-2 rounded-full" [style.background]="slot.color"></span>
                    }
                  </span>
                }
                @if (slot.changed) {
                  <span class="text-warning absolute right-1.5 bottom-0.5 text-[10px]" aria-label="changed">●</span>
                }
              </div>
            }
          </div>
        }
        <p class="text-foreground-subtle mt-auto shrink-0 border-t border-(--hairline) px-3 py-2 text-xs">
          Drag swaps two slots; ⌘-drag moves, ⇧-drag copies. Drop on a bank in the list for its first empty slot.
        </p>
      </section>
    </div>

    <ng-template #colors>
      <xui-menu>
        <button type="button" xuiMenuItem (click)="color('')">No colour</button>
        @for (c of tagColors; track c) {
          <button type="button" xuiMenuItem (click)="color(c)">
            <span class="me-2 inline-block size-3 rounded-full" [style.background]="c"></span>{{ tagColorName(c) }}
          </button>
        }
      </xui-menu>
    </ng-template>

    <ng-template #bankActions let-id="id">
      <xui-menu>
        <button type="button" xuiMenuItem (click)="openRenameBank(id)">Rename…</button>
        <button type="button" xuiMenuItem color="error" (click)="askDeleteBank(id)">Delete…</button>
      </xui-menu>
    </ng-template>

    <xui-alert-dialog
      destructive
      [title]="'Delete bank “' + bankName(deletingBank()) + '”?'"
      description="Its presets stay in the library. A MIDI mapping that loads slots from this bank stops working. The bank is deleted now, with any unsaved slot changes in it."
      confirmText="Delete"
      [(open)]="confirmDeleteBank"
      (confirmed)="deleteBank()"
    />

    <xui-dialog [title]="renamingBank() ? 'Rename bank' : 'Rename'" [(open)]="renaming">
      <xui-dialog-body>
        <xui-form-field label="Name">
          <input
            #newNameField
            xuiInput
            [value]="newName()"
            (input)="newName.set(newNameField.value)"
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
      [description]="resetAmpsDescription()"
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
  protected readonly tagColorName = tagColorName;
  protected readonly entryHeight = LibraryEntryHeight;
  protected readonly byId = (_: number, preset: PresetSummary) => preset.id;
  /** Bumped after every change to the (mutable) sessions, so the view recomputes. */
  private readonly version = signal(0);
  private readonly session = signal(new ManageSession([], []));
  protected readonly bankId = signal<string | null>(null);
  protected readonly selected = signal<ReadonlySet<number>>(new Set());
  private anchor: number | null = null;
  protected readonly renaming = signal(false);
  /** The bank the Rename dialog renames, or null for the selected preset. */
  protected readonly renamingBank = signal<string | null>(null);
  /** The bank Delete asks about; kept after the dialog closes, which it does before it reports the confirmation. */
  protected readonly deletingBank = signal<string | null>(null);
  protected readonly confirmDeleteBank = signal(false);
  protected readonly newName = signal('');
  protected readonly confirmReset = signal(false);
  protected readonly saving = signal(false);

  protected readonly dirty = computed(() => {
    this.version();
    return this.session().dirty;
  });
  protected readonly banks = computed(() => {
    this.version();
    return [...this.session().banks];
  });
  protected readonly bank = computed(() => this.banks().find(b => b.id === this.bankId()) ?? this.banks()[0] ?? null);
  /** The bank shown, which the list marks: the one chosen, or the first before any is. */
  protected readonly shownBankId = computed(() => this.bank()?.id ?? null);
  /** Whether the bank shown is the read-only factory bank. */
  protected readonly factoryBank = computed(() => this.bank()?.factory ?? false);
  protected readonly slots = computed(() => {
    this.version();
    const bank = this.bank();
    const session = this.session();
    if (!bank) {
      return [];
    }

    return bank.slots.map((id, index) => {
      const ref = { bank: bank.id, slot: index };
      return {
        index,
        id: id ?? null,
        name: id ? session.nameOf(id) : '',
        ...(id ? this.metaOf(id) : { color: null, favorite: false }),
        changed: session.isChanged(ref)
      };
    });
  });
  /** A preset's colour tag and favourite flag as Save will leave them (a pending "" clears the colour). */
  private metaOf(id: string): { color: string | null; favorite: boolean } {
    const session = this.session();
    const pending = session.meta.get(id);
    const stored = session.presets.get(id);
    return {
      color: pending?.color !== undefined ? pending.color || null : (stored?.color ?? null),
      favorite: pending?.favorite ?? stored?.favorite ?? false
    };
  }

  protected readonly selectedIds = computed(() =>
    [...this.selected()].map(i => this.slots()[i]?.id).filter((id): id is string => !!id)
  );
  /** The selected presets that exist on the Server already (not copies waiting for Save), factory ones left out. */
  protected readonly storedSelection = computed(() =>
    this.selectedIds().filter(id => this.session().presets.has(id) && !this.session().isFactory(id))
  );
  /** The Reset Amp blocks confirmation, which names how many presets it changes. */
  protected readonly resetAmpsDescription = computed(
    () =>
      `Every Circuit Amp setting in ${this.storedSelection().length} preset(s) goes back to its default, except the amp's own front-panel knobs and switches. This changes the stored presets now.`
  );
  /** Whether the selection holds a factory preset, which cannot be renamed or deleted. */
  protected readonly selectedFactory = computed(() => this.selectedIds().some(id => this.session().isFactory(id)));
  constructor() {
    void this.reload();
  }

  protected selectBank(id: string): void {
    this.bankId.set(id);
    this.selected.set(new Set());
    this.anchor = null;
  }

  /** Enter or Space on a focused slot picks it as a click would (⇧ and ⌘ included). */
  protected slotKey(index: number, event: KeyboardEvent): void {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.select(index, event);
    }
  }

  protected select(index: number, event: MouseEvent | KeyboardEvent): void {
    const next = nextSelection(this.selected(), this.anchor, index, {
      shift: event.shiftKey,
      toggle: event.metaKey || event.ctrlKey
    });
    this.selected.set(next.selected);
    this.anchor = next.anchor;
  }

  /** Creates a bank on the Server at once, as renaming and deleting one are. */
  protected async addBank(): Promise<void> {
    const bank: Bank = {
      id: crypto.randomUUID().replaceAll('-', ''),
      name: `Bank ${this.banks().length + 1}`,
      slots: []
    };
    const stored = await this.notify.run(async () => {
      unwrap(await this.connection.client.PUT('/api/v1/banks/{id}', { params: { path: { id: bank.id } }, body: bank }));
      return true;
    });
    if (stored) {
      this.session().addBank(bank);
      this.changed();
      this.selectBank(bank.id);
      await this.library.refreshBanks();
    }
  }

  protected dragSlot(event: DragEvent, slot: number): void {
    const bank = this.bank();
    if (bank) {
      event.dataTransfer?.setData(SlotMime, JSON.stringify({ bank: bank.id, slot }));
    }
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
    const [id] = this.selectedIds();
    if (id === undefined) {
      return;
    }

    this.renamingBank.set(null);
    this.newName.set(this.session().nameOf(id));
    this.renaming.set(true);
  }

  protected openRenameBank(id: string): void {
    this.renamingBank.set(id);
    this.newName.set(this.bankName(id));
    this.renaming.set(true);
  }

  protected bankName(id: string | null): string {
    return this.banks().find(b => b.id === id)?.name ?? '';
  }

  protected askDeleteBank(id: string): void {
    this.deletingBank.set(id);
    this.confirmDeleteBank.set(true);
  }

  /** Renames a bank on the Server at once, with its stored slots: pending slot changes stay pending. */
  private async renameBank(id: string, name: string): Promise<void> {
    const stored = this.library.banks().find(b => b.id === id) ?? { id, name, slots: [] };
    const done = await this.notify.run(async () => {
      unwrap(
        await this.connection.client.PUT('/api/v1/banks/{id}', { params: { path: { id } }, body: { ...stored, name } })
      );
      return true;
    });
    if (done) {
      this.session().renameBank(id, name);
      this.renaming.set(false);
      this.changed();
      await this.library.refreshBanks();
    }
  }

  /** Deletes a bank on the Server at once; its pending slot changes go with it. */
  protected async deleteBank(): Promise<void> {
    const id = this.deletingBank();
    this.deletingBank.set(null);
    if (!id) {
      return;
    }

    const done = await this.notify.run(async () => {
      unwrap(await this.connection.client.DELETE('/api/v1/banks/{id}', { params: { path: { id } } }));
      return true;
    });
    if (done) {
      const wasShown = this.bank()?.id === id;
      this.session().deleteBank(id);
      this.changed();
      if (wasShown) {
        this.selectBank(this.banks()[0]?.id ?? '');
      }

      await this.library.refreshBanks();
    }
  }

  protected rename(): void {
    const name = this.newName().trim();
    if (!name) {
      return;
    }

    const bank = this.renamingBank();
    if (bank) {
      void this.renameBank(bank, name);
      return;
    }

    const [id] = this.selectedIds();
    if (id !== undefined) {
      this.session().rename(id, name);
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
    this.selectedIds().forEach(id => this.session().setMeta(id, { color }));
    this.changed();
  }

  protected favorite(): void {
    const session = this.session();
    const all = this.selectedIds().every(id => session.meta.get(id)?.favorite ?? session.presets.get(id)?.favorite);
    this.selectedIds().forEach(id => session.setMeta(id, { favorite: !all }));
    this.changed();
  }

  protected remove(): void {
    this.session().delete(this.selectedIds());
    this.selected.set(new Set());
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

  /** Carries out the session: copies, renames and metadata, banks, then deletions. */
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
          const { name: _copiesKeepTheirName, ...colorAndFavorite } = meta;
          await this.patchPreset(id, colorAndFavorite);
        }
      }

      for (const bank of plan.banks) {
        // Empty slots are null on the wire (see WorkBank).
        unwrap(await client.PUT('/api/v1/banks/{id}', { params: { path: { id: bank.id } }, body: bank as Bank }));
      }

      for (const id of plan.deletes) {
        unwrap(await client.DELETE('/api/v1/presets/{id}', { params: { path: { id } } }));
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
    await Promise.all([this.library.refreshPresets(), this.library.refreshBanks()]);
    this.session.set(new ManageSession(this.library.banks(), this.library.presets()));
    this.selected.set(new Set());
    this.changed();
  }

  private changed(): void {
    this.version.update(v => v + 1);
  }
}
