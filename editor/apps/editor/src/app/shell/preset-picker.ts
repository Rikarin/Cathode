import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild
} from '@angular/core';
import { provideIcons } from '@ng-icons/core';
import { matLibraryMusicRound } from '@ng-icons/material-icons/round';
import { XuiDialogImports } from '@xui/dialog';
import { XuiSelectImports } from '@xui/select';
import { Notify } from '../core/notify';
import { CompareStore } from '../state/compare.store';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';
import { UiStore } from '../state/ui.store';
import { type PickerEntry, PickerList } from './picker-list';

/** A bank slot typed in the Go to picker: "12" is slot 12 of the chosen bank (1-based, as on a foot controller). */
export function parseSlot(query: string): number | null {
  const match = /^\s*(\d{1,3})\s*$/.exec(query);
  const slot = match ? Number(match[1]) : NaN;
  return slot >= 1 && slot <= 128 ? slot - 1 : null;
}

/**
 * The preset picker (P), and Go to (⌘G), which also takes a bank slot number. Choosing loads the preset; with the
 * pin on, the picker stays open to audition one after another.
 */
@Component({
  selector: 'cth-preset-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PickerList, XuiDialogImports, XuiSelectImports],
  providers: [provideIcons({ matLibraryMusicRound })],
  template: `
    <xui-dialog
      icon="matLibraryMusicRound"
      class="w-[min(40rem,calc(100vw-2rem))] max-w-none"
      [title]="ui.presetPicker() === 'goto' ? 'Go to preset or bank slot' : 'Presets'"
      [open]="ui.presetPicker() !== null"
      (openChange)="!$event && ui.presetPicker.set(null)"
    >
      <xui-dialog-body class="flex min-h-0 flex-col gap-3">
        @if (ui.presetPicker() === 'goto' && library.banks().length > 0) {
          <xui-select
            class="w-64"
            aria-label="Bank"
            [filterable]="false"
            [items]="library.banks()"
            [itemText]="bankName"
            [value]="bank()"
            (valueChange)="bankId.set($event?.id ?? null)"
          />
        }
        <cth-picker-list
          noun="presets"
          [placeholder]="ui.presetPicker() === 'goto' ? 'Name, or a slot number' : 'Search name, author or tag'"
          [entries]="entries()"
          [current]="live.preset()?.id ?? null"
          [extra]="slotText()"
          (queryChange)="query.set($event)"
          (chosen)="load($event)"
          (extraChosen)="loadSlot()"
          (favorite)="library.setPresetMetadata($event.entry.id, { favorite: $event.favorite })"
          (color)="library.setPresetMetadata($event.entry.id, { color: $event.color })"
        />
      </xui-dialog-body>
    </xui-dialog>
  `
})
export class PresetPicker {
  protected readonly ui = inject(UiStore);
  protected readonly live = inject(LiveStore);
  protected readonly library = inject(LibraryStore);
  private readonly compare = inject(CompareStore);
  private readonly notify = inject(Notify);
  private readonly list = viewChild(PickerList);

  protected readonly query = signal('');
  protected readonly bankId = signal<string | null>(null);
  protected readonly bank = computed(
    () => this.library.banks().find(b => b.id === this.bankId()) ?? this.library.banks()[0] ?? null
  );
  protected readonly bankName = (bank: { name: string }) => bank.name;
  protected readonly entries = computed<PickerEntry[]>(() =>
    this.library.presets().map(p => ({
      id: p.id,
      name: p.name || '(untitled)',
      detail: [p.author ?? '', ...p.tags].filter(Boolean).join(' · '),
      color: p.color ?? null,
      favorite: p.favorite ?? false
    }))
  );
  protected readonly slotText = computed(() => {
    const slot = parseSlot(this.query());
    const bank = this.bank();
    if (this.ui.presetPicker() !== 'goto' || slot === null || !bank) {
      return null;
    }

    const id = bank.slots[slot];
    const name = id ? (this.library.presets().find(p => p.id === id)?.name ?? id) : 'empty';
    return `${bank.name}, slot ${slot + 1}: ${name}`;
  });

  constructor() {
    effect(() => {
      if (this.ui.presetPicker() !== null) {
        untracked(() => {
          this.query.set('');
          void this.library.refreshPresets();
          void this.library.refreshBanks();
        });
      }
    });
  }

  protected async load(entry: PickerEntry): Promise<void> {
    // TODO(PS): ask about unsaved edits first, through the unsaved-edits prompt once it is on master.
    if (await this.live.load(entry.id)) {
      this.compare.reset();
      if (!this.list()?.pinned()) {
        this.ui.presetPicker.set(null);
      }
    }
  }

  protected async loadSlot(): Promise<void> {
    const slot = parseSlot(this.query());
    const bank = this.bank();
    if (slot === null || !bank) {
      return;
    }

    const loaded = await this.live.mutate(c =>
      c.PUT('/api/v1/live/banks/{id}/slots/{slot}', { params: { path: { id: bank.id, slot } } })
    );
    if (loaded) {
      this.compare.reset();
      this.ui.presetPicker.set(null);
    }
  }

  /** Steps through the library by name, as a footswitch's preset next/previous does. */
  async step(direction: 1 | -1): Promise<void> {
    await this.library.refreshPresets();
    const all = this.library.ordered();
    if (all.length === 0) {
      return;
    }

    const current = all.findIndex(p => p.id === this.live.preset()?.id);
    const next = all[(Math.max(current, 0) + direction + all.length) % all.length];
    if (await this.live.load(next.id)) {
      this.compare.reset();
    }
  }

  /** F5: the saved version again (a fresh load, which clears undo); an unsaved preset has none. */
  async reload(): Promise<void> {
    await this.library.refreshPresets();
    const id = this.live.preset()?.id;
    if (!id || !this.library.presets().some(p => p.id === id)) {
      this.notify.error('This preset has not been saved yet: there is nothing to reload.');
    } else if (await this.live.load(id)) {
      this.compare.reset();
    }
  }
}
