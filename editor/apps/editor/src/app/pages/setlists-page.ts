import { ScrollingModule } from '@angular/cdk/scrolling';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import type { PresetSummary, Setlist, SetlistEntry } from '@cathode/api-client';
import { provideIcons } from '@ng-icons/core';
import {
  matAddRound,
  matArrowDownwardRound,
  matArrowUpwardRound,
  matCloseRound,
  matDragIndicatorRound,
  matPlayArrowRound,
  matPlaylistAddRound,
  matWarningAmberRound
} from '@ng-icons/material-icons/round';
import { XuiAlertDialogImports } from '@xui/alert-dialog';
import { XuiButtonImports } from '@xui/button';
import { XuiCalloutImports } from '@xui/callout';
import { XuiContextMenuImports } from '@xui/context-menu';
import { XuiDialogImports } from '@xui/dialog';
import { XuiFormFieldImports } from '@xui/form-field';
import { XuiIconImports } from '@xui/icon';
import { XuiInputImports } from '@xui/input';
import { XuiMenuImports } from '@xui/menu';
import { XuiNonIdealStateImports } from '@xui/non-ideal-state';
import { XuiSelectImports } from '@xui/select';
import { XuiTagImports } from '@xui/tag';
import { Notify } from '../core/notify';
import { LibraryStore } from '../state/library.store';
import { SetlistPlayer } from '../state/setlist-player';
import { LibraryEntryHeight } from './manage-page';
import { SetlistScenes, freeName, movedEntry, resolveSongs, withSongs } from './setlists';

/** Drag data: a song of the setlist shown (its index), or a preset from the library (its id). */
const SongMime = 'application/x-cathode-song';
const PresetMime = 'application/x-cathode-preset';

/**
 * Setlists: presets in the order of a set, each song on the scene it starts on and with a name of its own if you like.
 * The list on the left makes, renames, duplicates and deletes setlists (right-click, or the buttons over the songs);
 * the songs are added from the library on the right (double-click, ↵, the + button or a drag), reordered by dragging
 * or with ⌥↑ and ⌥↓, and removed with × or Delete. Every change is stored at once. A song whose preset has gone from
 * the library is marked, and is passed over when the set is played. Play opens the Perform page with the setlist.
 * `?id=` picks the setlist shown.
 */
@Component({
  selector: 'cth-setlists-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ScrollingModule,
    XuiAlertDialogImports,
    XuiButtonImports,
    XuiCalloutImports,
    XuiContextMenuImports,
    XuiDialogImports,
    XuiFormFieldImports,
    XuiIconImports,
    XuiInputImports,
    XuiMenuImports,
    XuiNonIdealStateImports,
    XuiSelectImports,
    XuiTagImports
  ],
  providers: [
    provideIcons({
      matAddRound,
      matArrowDownwardRound,
      matArrowUpwardRound,
      matCloseRound,
      matDragIndicatorRound,
      matPlayArrowRound,
      matPlaylistAddRound,
      matWarningAmberRound
    })
  ],
  host: { class: 'flex h-full flex-col gap-4 p-6' },
  template: `
    <header class="flex flex-wrap items-center gap-3">
      <h1 class="mr-2 text-lg font-semibold">Setlists</h1>
      <span class="flex-1"></span>
      @if (setlist(); as s) {
        <button type="button" xuiButton data-testid="setlist-play" [disabled]="playable() === 0" (click)="play(s.id)">
          <ng-icon xui name="matPlayArrowRound" size="sm" /> Play on the Perform page
        </button>
      }
    </header>

    <div class="flex min-h-0 flex-1 gap-4">
      <aside class="flex w-56 shrink-0 flex-col gap-3 max-xl:w-44">
        <div class="flex items-center justify-between">
          <span class="text-foreground-muted text-[11px] font-semibold tracking-wide uppercase" id="setlists-label">
            Setlists
          </span>
          <button
            type="button"
            xuiButton
            size="sm"
            variant="ghost"
            aria-label="New setlist"
            data-testid="setlist-new"
            (click)="addSetlist()"
          >
            <ng-icon xui name="matAddRound" size="sm" />
          </button>
        </div>
        <ul
          class="flex min-h-0 flex-col gap-0.5 overflow-y-auto text-sm"
          role="listbox"
          aria-labelledby="setlists-label"
        >
          @for (s of library.setlists(); track s.id) {
            <li
              role="option"
              class="hover:bg-active-overlay flex cursor-pointer items-center gap-1 rounded px-2 py-1"
              tabindex="0"
              [class.bg-active-overlay]="s.id === setlist()?.id"
              [attr.aria-selected]="s.id === setlist()?.id"
              [attr.data-testid]="'setlist-' + s.id"
              [xuiContextMenuTriggerFor]="setlistActions"
              [xuiContextMenuTriggerData]="{ id: s.id }"
              (click)="select(s.id)"
              (keydown.enter)="select(s.id)"
              (keydown.space)="$event.preventDefault(); select(s.id)"
              (dblclick)="openRename(s.id)"
              (contextmenu)="select(s.id)"
            >
              <span class="min-w-0 flex-1 truncate">{{ s.name }}</span>
              @if (s.id === player.setlist()?.id) {
                <xui-tag minimal color="primary" class="shrink-0">playing</xui-tag>
              }
            </li>
          } @empty {
            <li class="text-foreground-muted text-xs">No setlists yet: add one.</li>
          }
        </ul>
      </aside>

      <section class="flex min-w-0 flex-1 flex-col gap-3">
        @if (setlist(); as s) {
          <div class="flex flex-wrap items-center gap-2" role="toolbar" aria-label="Setlist">
            <h2 class="mr-2 truncate font-medium" data-testid="setlist-name">{{ s.name }}</h2>
            <span class="text-foreground-muted text-sm">{{ songCount(songs().length) }}</span>
            <span class="flex-1"></span>
            <button type="button" xuiButton size="sm" variant="outline" (click)="openRename(s.id)">Rename…</button>
            <button type="button" xuiButton size="sm" variant="outline" (click)="duplicate(s.id)">Duplicate</button>
            <button type="button" xuiButton size="sm" variant="outline" color="error" (click)="askDelete(s.id)">
              Delete…
            </button>
          </div>
          @if (missing() > 0) {
            <xui-callout minimal compact color="warning" data-testid="setlist-missing">
              {{ missing() === 1 ? 'One song’s preset is' : missing() + ' songs’ presets are' }} not in the library any
              more: playing the set passes over {{ missing() === 1 ? 'it' : 'them' }}. Remove
              {{ missing() === 1 ? 'it' : 'them' }}, or put the preset back.
            </xui-callout>
          }
          <ol
            class="flex min-h-0 flex-1 flex-col gap-1 overflow-y-auto pb-6"
            aria-label="Songs"
            data-testid="setlist-songs"
            (dragover)="dragOver($event, songs().length)"
            (drop)="drop($event, songs().length)"
          >
            @for (song of songs(); track $index; let i = $index, first = $first, last = $last) {
              <li
                class="border-border-muted bg-surface flex items-center gap-2 rounded-md border px-2 py-1.5 text-sm"
                tabindex="0"
                [class.border-t-primary]="dropAt() === i"
                [class.border-t-2]="dropAt() === i"
                [class.opacity-50]="dragging() === i"
                [attr.data-song]="i"
                [attr.data-testid]="'song-' + i"
                [attr.aria-label]="'Song ' + (i + 1) + ': ' + song.title + (song.missing ? ' (missing preset)' : '')"
                (keydown)="songKey($event, i)"
                (dragover)="dragOver($event, i)"
                (drop)="drop($event, i)"
              >
                <span
                  class="text-foreground-muted flex cursor-grab items-center"
                  draggable="true"
                  aria-hidden="true"
                  title="Drag to move"
                  (dragstart)="dragSong($event, i)"
                  (dragend)="dragging.set(null); dropAt.set(null)"
                >
                  <ng-icon xui name="matDragIndicatorRound" size="sm" />
                </span>
                <span class="text-foreground-subtle w-6 shrink-0 text-right tabular-nums">{{ i + 1 }}</span>
                <span class="flex min-w-0 flex-1 basis-32 items-center gap-1">
                  @if (song.missing) {
                    <ng-icon xui name="matWarningAmberRound" size="sm" class="text-warning shrink-0" />
                    <span class="text-warning truncate font-medium" [attr.title]="'No preset ' + song.entry.presetId">
                      Missing preset
                    </span>
                  } @else {
                    <span class="truncate font-medium" [attr.title]="song.presetName">{{ song.presetName }}</span>
                  }
                </span>
                <input
                  #label
                  xuiInput
                  class="h-7 min-w-20 flex-1 basis-40"
                  placeholder="Song name"
                  [attr.aria-label]="'Name of song ' + (i + 1)"
                  [value]="song.entry.label ?? ''"
                  (change)="setLabel(i, label.value)"
                  (keydown.enter)="label.blur()"
                />
                <xui-select
                  class="w-28 shrink-0"
                  [attr.aria-label]="'Starting scene of song ' + (i + 1)"
                  [filterable]="false"
                  [items]="scenes"
                  [itemText]="sceneName"
                  [value]="String(song.entry.scene)"
                  (valueChange)="$event !== null && setScene(i, +$event)"
                />
                <span class="flex shrink-0 items-center">
                  <button
                    type="button"
                    xuiButton
                    variant="ghost"
                    size="sm"
                    [disabled]="first"
                    [attr.aria-label]="'Move song ' + (i + 1) + ' earlier'"
                    (click)="move(i, i - 1)"
                  >
                    <ng-icon xui name="matArrowUpwardRound" size="sm" />
                  </button>
                  <button
                    type="button"
                    xuiButton
                    variant="ghost"
                    size="sm"
                    [disabled]="last"
                    [attr.aria-label]="'Move song ' + (i + 1) + ' later'"
                    (click)="move(i, i + 1)"
                  >
                    <ng-icon xui name="matArrowDownwardRound" size="sm" />
                  </button>
                  <button
                    type="button"
                    xuiButton
                    variant="ghost"
                    size="sm"
                    [attr.aria-label]="'Remove song ' + (i + 1)"
                    (click)="remove(i)"
                  >
                    <ng-icon xui name="matCloseRound" size="sm" />
                  </button>
                </span>
              </li>
            } @empty {
              <li class="flex flex-1">
                <xui-non-ideal-state
                  class="flex-1 rounded-md border border-dashed"
                  [class.border-primary]="dropAt() === 0"
                  title="No songs yet"
                  description="Add presets from the library on the right: double-click one, or drag it here."
                />
              </li>
            }
          </ol>
          <p class="text-foreground-muted text-xs">
            Drag a song to move it, or focus it and press ⌥↑ or ⌥↓; Delete removes it. Changes are saved at once.
          </p>
        } @else {
          <xui-non-ideal-state
            class="flex-1"
            title="No setlist"
            description="A setlist lists presets in the order of a set, each on the scene the song starts on. Click + to make one."
          />
        }
      </section>

      <aside class="flex w-64 shrink-0 flex-col gap-3 max-xl:w-48">
        <span class="text-foreground-muted text-[11px] font-semibold tracking-wide uppercase">Library</span>
        <input
          #filterField
          xuiInput
          type="search"
          placeholder="Filter presets"
          aria-label="Filter presets"
          [value]="filter()"
          (input)="filter.set(filterField.value)"
        />
        <!-- Only the entries in view are rendered. -->
        <cdk-virtual-scroll-viewport
          class="min-h-0 flex-1 text-sm"
          role="list"
          aria-label="Presets to add"
          data-testid="setlist-library"
          [itemSize]="entryHeight"
        >
          <div
            *cdkVirtualFor="let p of presets(); trackBy: byId; let i = index; let count = count"
            role="listitem"
            class="hover:bg-active-overlay group flex h-6 cursor-grab items-center gap-1 rounded ps-2"
            tabindex="0"
            [attr.draggable]="!!setlist()"
            [attr.aria-posinset]="i + 1"
            [attr.aria-setsize]="count"
            [attr.title]="'Double-click or press ↵ to add “' + p.name + '”'"
            (dblclick)="add(p.id)"
            (keydown.enter)="add(p.id)"
            (dragstart)="dragPreset($event, p.id)"
            (dragend)="dropAt.set(null)"
          >
            <span class="min-w-0 flex-1 truncate">{{ p.name }}</span>
            <button
              type="button"
              xuiButton
              variant="ghost"
              size="sm"
              class="h-6 opacity-0 group-focus-within:opacity-100 group-hover:opacity-100"
              tabindex="-1"
              [disabled]="!setlist()"
              [attr.aria-label]="'Add ' + p.name"
              (click)="add(p.id)"
            >
              <ng-icon xui name="matPlaylistAddRound" size="sm" />
            </button>
          </div>
        </cdk-virtual-scroll-viewport>
      </aside>
    </div>

    <ng-template #setlistActions let-id="id">
      <xui-menu>
        <button type="button" xuiMenuItem (click)="openRename(id)">Rename…</button>
        <button type="button" xuiMenuItem (click)="duplicate(id)">Duplicate</button>
        <button type="button" xuiMenuItem color="error" (click)="askDelete(id)">Delete…</button>
      </xui-menu>
    </ng-template>

    <xui-alert-dialog
      destructive
      [title]="'Delete setlist “' + nameOf(deleting()) + '”?'"
      description="Its presets stay in the library. If it is the setlist being played, no setlist is played any more."
      confirmText="Delete"
      [(open)]="confirmDelete"
      (confirmed)="deleteSetlist()"
    />

    <xui-dialog title="Rename setlist" [(open)]="renaming">
      <xui-dialog-body>
        <xui-form-field label="Name">
          <input
            #newNameField
            xuiInput
            data-testid="setlist-rename-field"
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
  `
})
export class SetlistsPage {
  protected readonly library = inject(LibraryStore);
  protected readonly player = inject(SetlistPlayer);
  private readonly notify = inject(Notify);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly String = String;
  protected readonly scenes = SetlistScenes;
  protected readonly sceneName = (scene: string) => `Scene ${+scene + 1}`;
  protected readonly entryHeight = LibraryEntryHeight;
  protected readonly byId = (_: number, preset: PresetSummary) => preset.id;

  private readonly selectedId = signal<string | null>(inject(ActivatedRoute).snapshot.queryParamMap.get('id') || null);
  protected readonly setlist = computed(
    () => this.library.setlists().find(s => s.id === this.selectedId()) ?? this.library.setlists()[0] ?? null
  );
  protected readonly songs = computed(() => {
    const presets = this.library.presets();
    return resolveSongs(this.setlist(), presets.length > 0 ? presets : null);
  });
  protected readonly missing = computed(() => this.songs().filter(s => s.missing).length);
  protected readonly playable = computed(() => this.songs().length - this.missing());

  protected readonly filter = signal('');
  protected readonly presets = computed(() => {
    const words = this.filter().toLocaleLowerCase().split(/\s+/).filter(Boolean);
    return this.library.ordered().filter(p => words.every(w => p.name.toLocaleLowerCase().includes(w)));
  });

  /** The song being dragged by its row, and where a drop would put a song (before that index). */
  protected readonly dragging = signal<number | null>(null);
  protected readonly dropAt = signal<number | null>(null);

  protected readonly renaming = signal(false);
  private renamingId: string | null = null;
  protected readonly newName = signal('');
  /** The setlist Delete asks about; kept after the dialog closes, which it does before it reports the confirmation. */
  protected readonly deleting = signal<string | null>(null);
  protected readonly confirmDelete = signal(false);

  constructor() {
    void this.library.refreshSetlists();
    void this.library.refreshPresets();
  }

  protected songCount(n: number): string {
    return n === 1 ? '1 song' : `${n} songs`;
  }

  protected nameOf(id: string | null): string {
    return this.library.setlists().find(s => s.id === id)?.name ?? '';
  }

  protected select(id: string): void {
    this.selectedId.set(id);
    void this.router.navigate([], { queryParams: { id }, replaceUrl: true });
  }

  /** Makes a setlist and asks for its name. */
  protected async addSetlist(): Promise<void> {
    const setlist: Setlist = {
      id: newId(),
      name: freeName(
        'New setlist',
        this.library.setlists().map(s => s.name)
      ),
      entries: []
    };
    if (await this.library.saveSetlist(setlist)) {
      this.select(setlist.id);
      this.openRename(setlist.id);
    }
  }

  protected openRename(id: string): void {
    this.renamingId = id;
    this.newName.set(this.nameOf(id));
    this.renaming.set(true);
  }

  protected async rename(): Promise<void> {
    const name = this.newName().trim();
    const setlist = this.library.setlists().find(s => s.id === this.renamingId);
    if (name && setlist && (await this.library.saveSetlist({ ...setlist, name }))) {
      this.renaming.set(false);
    }
  }

  /**
   * Copies a setlist under a free name and shows the copy. Songs whose preset is missing are left out: a new setlist
   * may only point at presets that exist.
   */
  protected async duplicate(id: string): Promise<void> {
    const source = this.library.setlists().find(s => s.id === id);
    if (!source) {
      return;
    }

    const presets = this.library.presets();
    const songs = resolveSongs(source, presets.length > 0 ? presets : null);
    const missing = new Set(songs.filter(s => s.missing).map(s => s.index));
    const copy: Setlist = {
      id: newId(),
      name: freeName(
        `${source.name} copy`,
        this.library.setlists().map(s => s.name)
      ),
      entries: source.entries.filter((_, i) => !missing.has(i)).map(e => ({ ...e }))
    };
    if (await this.library.saveSetlist(copy)) {
      this.select(copy.id);
      if (missing.size > 0) {
        this.notify.success(`Duplicated “${source.name}”, leaving out ${missing.size} song(s) with a missing preset.`);
      }
    }
  }

  protected askDelete(id: string): void {
    this.deleting.set(id);
    this.confirmDelete.set(true);
  }

  /** Deletes the setlist Delete asked about. */
  protected async deleteSetlist(): Promise<void> {
    const id = this.deleting();
    this.deleting.set(null);
    // If it was being played, the Server stops playing it (and says so: the global settings change).
    if (id && (await this.library.deleteSetlist(id))) {
      const next = this.library.setlists()[0];
      if (next) {
        this.select(next.id);
      }
    }
  }

  /** Makes the setlist the one being played, before its first song, and opens the Perform page. */
  protected async play(id: string): Promise<void> {
    if (this.player.setlist()?.id === id || (await this.player.pick(id))) {
      await this.router.navigateByUrl('/perform');
    }
  }

  protected add(presetId: string): void {
    void this.change(entries => withSongs(entries, [presetId]));
  }

  protected async remove(index: number): Promise<boolean> {
    return this.change(entries => entries.filter((_, i) => i !== index));
  }

  protected async move(from: number, to: number): Promise<void> {
    const count = this.songs().length;
    if (to < 0 || to >= count || from === to) {
      return;
    }

    if (await this.change(entries => movedEntry(entries, from, to))) {
      this.focusSong(to);
    }
  }

  protected setScene(index: number, scene: number): void {
    if (this.songs()[index]?.entry.scene !== scene) {
      void this.change(entries => entries.map((e, i) => (i === index ? { ...e, scene } : e)));
    }
  }

  protected setLabel(index: number, text: string): void {
    const label = text.trim() || null;
    if ((this.songs()[index]?.entry.label ?? null) !== label) {
      void this.change(entries => entries.map((e, i) => (i === index ? { ...e, label } : e)));
    }
  }

  /** A focused song row: ⌥↑ and ⌥↓ move it, ↑ and ↓ move the focus, Delete and ⌫ remove it. */
  protected songKey(event: KeyboardEvent, index: number): void {
    if (event.target !== event.currentTarget) {
      return; // typing in its name, or choosing its scene
    }

    const step = event.key === 'ArrowUp' ? -1 : event.key === 'ArrowDown' ? 1 : 0;
    if (step !== 0) {
      event.preventDefault();
      if (event.altKey) {
        void this.move(index, index + step);
      } else {
        this.focusSong(index + step);
      }
    } else if (event.key === 'Delete' || event.key === 'Backspace') {
      event.preventDefault();
      void this.remove(index).then(done => done && this.focusSong(Math.min(index, this.songs().length - 1)));
    }
  }

  protected dragSong(event: DragEvent, index: number): void {
    this.dragging.set(index);
    event.dataTransfer?.setData(SongMime, String(index));
    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = 'move';
    }
  }

  protected dragPreset(event: DragEvent, id: string): void {
    event.dataTransfer?.setData(PresetMime, id);
    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = 'copy';
    }
  }

  protected dragOver(event: DragEvent, index: number): void {
    const types = event.dataTransfer?.types ?? [];
    if (types.includes(SongMime) || types.includes(PresetMime)) {
      event.preventDefault(); // a drop target
      event.stopPropagation(); // a row's place, not the end of the list
      this.dropAt.set(index);
    }
  }

  /** A song dropped before song `index` moves there; a preset dropped there is added there. */
  protected drop(event: DragEvent, index: number): void {
    event.preventDefault();
    event.stopPropagation();
    const song = event.dataTransfer?.getData(SongMime);
    const preset = event.dataTransfer?.getData(PresetMime);
    this.dragging.set(null);
    this.dropAt.set(null);
    if (song) {
      const from = +song;
      // Taken out first, a song moving down lands one place earlier than the row it was dropped on.
      void this.move(from, index > from ? index - 1 : index);
    } else if (preset) {
      void this.change(entries => withSongs(entries, [preset], index));
    }
  }

  /** Stores the shown setlist with its songs changed; false if the Server refused it. */
  private async change(edit: (entries: readonly SetlistEntry[]) => SetlistEntry[]): Promise<boolean> {
    const setlist = this.setlist();
    return setlist ? this.library.saveSetlist({ ...setlist, entries: edit(setlist.entries) }) : false;
  }

  /** Focuses a song's row once the list shows the change. */
  private focusSong(index: number): void {
    afterNextRender(
      () => this.host.nativeElement.querySelector<HTMLElement>(`[data-song="${Math.max(0, index)}"]`)?.focus(),
      { injector: this.injector }
    );
  }
}

/** A new library id, as the Server makes them (32 hex digits). */
function newId(): string {
  return crypto.randomUUID().replaceAll('-', '');
}
