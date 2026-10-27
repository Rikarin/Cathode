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
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';
import { PresetSwitcher } from '../state/preset-switcher';
import { UiStore } from '../state/ui.store';
import { type PickerEntry, PickerList } from './picker-list';

/** A bank slot typed in the preset picker: "12" is slot 12 of the chosen bank (1-based, as on a foot controller). */
export function parseSlot(query: string): number | null {
  const match = /^\s*(\d{1,3})\s*$/.exec(query);
  const slot = match ? Number(match[1]) : NaN;
  return slot >= 1 && slot <= 128 ? slot - 1 : null;
}

/**
 * The preset picker (P): search by name, author or tag, or type a slot number of the bank chosen above the list (when
 * there are banks). Choosing loads the preset; with the pin on, the picker stays open to audition one after another.
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
      title="Presets"
      [open]="ui.presetPicker()"
      (openChange)="!$event && ui.presetPicker.set(false)"
    >
      <xui-dialog-body class="flex min-h-0 flex-col gap-3">
        @if (library.banks().length > 0) {
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
          [placeholder]="
            library.banks().length > 0 ? 'Search name, author or tag, or a slot number' : 'Search name, author or tag'
          "
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
  private readonly switcher = inject(PresetSwitcher);
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
      detail: [p.factory ? 'Factory' : (p.author ?? ''), ...p.tags].filter(Boolean).join(' · '),
      color: p.color ?? null,
      favorite: p.favorite
    }))
  );
  protected readonly slotText = computed(() => {
    const slot = parseSlot(this.query());
    const bank = this.bank();
    if (slot === null || !bank) {
      return null;
    }

    const id = bank.slots[slot];
    const name = id ? (this.library.presets().find(p => p.id === id)?.name ?? id) : 'empty';
    return `${bank.name}, slot ${slot + 1}: ${name}`;
  });

  constructor() {
    effect(() => {
      if (this.ui.presetPicker()) {
        untracked(() => {
          this.query.set('');
          void this.library.refreshPresets();
          void this.library.refreshBanks();
        });
      }
    });
  }

  /** Loads through the preset switcher: it asks before discarding unsaved edits, and keeps Recent Presets. */
  protected async load(entry: PickerEntry): Promise<void> {
    if ((await this.switcher.load(entry.id, entry.name)) && !this.list()?.pinned()) {
      this.ui.presetPicker.set(false);
    }
  }

  protected async loadSlot(): Promise<void> {
    const slot = parseSlot(this.query());
    const bank = this.bank();
    if (slot === null || !bank) {
      return;
    }

    const id = bank.slots[slot];
    if (!id) {
      this.notify.error(`Slot ${slot + 1} of ${bank.name} is empty.`);
      return;
    }

    const name = this.library.presets().find(p => p.id === id)?.name ?? id;
    if (await this.switcher.load(id, name)) {
      this.ui.presetPicker.set(false);
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
    // From a preset not in the library, next is the first and previous the last.
    const next =
      current < 0 ? all[direction === 1 ? 0 : all.length - 1] : all[(current + direction + all.length) % all.length];
    if (next) {
      await this.switcher.load(next.id, next.name);
    }
  }

  /** F5: the saved version again (a fresh load, which clears undo); an unsaved preset has none. */
  async reload(): Promise<void> {
    await this.library.refreshPresets();
    const id = this.live.preset()?.id;
    if (!id || !this.library.presets().some(p => p.id === id)) {
      this.notify.error('This preset has not been saved yet: there is nothing to reload.');
    } else {
      await this.switcher.load(id, this.live.preset()?.name ?? id);
    }
  }
}
