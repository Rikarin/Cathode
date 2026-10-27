import { ChangeDetectionStrategy, Component, computed, effect, input, output, signal, untracked } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { matPushPinRound, matSearchRound, matStarOutlineRound, matStarRound } from '@ng-icons/material-icons/round';
import { XuiButtonImports } from '@xui/button';
import { XuiInputImports } from '@xui/input';
import { XuiMenuImports } from '@xui/menu';
import { XuiTooltipImports } from '@xui/tooltip';

/** One entry of a picker: a preset or an asset. */
export interface PickerEntry {
  id: string;
  name: string;
  /** Searched as well as the name (author, tags, kind). */
  detail?: string;
  color?: string | null;
  favorite?: boolean;
}

/** Colour tags a preset or IR can carry (CSS colours that read on both themes). */
export const TagColors = ['red', 'orange', 'gold', 'limegreen', 'deepskyblue', 'violet'] as const;

/** What the colour menus call each tag colour (the stored value stays the CSS colour). */
const TagColorNames: Record<string, string> = {
  red: 'Red',
  orange: 'Orange',
  gold: 'Yellow',
  limegreen: 'Green',
  deepskyblue: 'Blue',
  violet: 'Purple'
};

/** A colour tag as people say it: "Green" for limegreen; an unknown colour as it is stored. */
export const tagColorName = (color: string): string => TagColorNames[color] ?? color;

/**
 * What a picker lists: entries matching every word of the query (in name or detail), favourites only when asked,
 * favourites first, then by name.
 */
export function pickerEntries<T extends PickerEntry>(entries: readonly T[], query: string, favoritesOnly = false): T[] {
  const words = query.toLowerCase().split(/\s+/).filter(Boolean);
  return entries
    .filter(e => !favoritesOnly || e.favorite)
    .filter(e => words.every(w => `${e.name} ${e.detail ?? ''}`.toLowerCase().includes(w)))
    .sort((a, b) => Number(!!b.favorite) - Number(!!a.favorite) || a.name.localeCompare(b.name));
}

/**
 * A searchable list for the preset and IR pickers: type to filter, ↑/↓ to move, ↵ to choose, ★ to favour, a colour
 * menu per entry, and a pin that keeps the picker open after choosing (to audition one after another).
 */
@Component({
  selector: 'cth-picker-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, XuiButtonImports, XuiInputImports, XuiMenuImports, XuiTooltipImports],
  providers: [provideIcons({ matPushPinRound, matSearchRound, matStarOutlineRound, matStarRound })],
  host: { class: 'flex min-h-0 flex-col gap-3' },
  template: `
    <div class="flex items-center gap-2">
      <xui-input-group class="flex-1">
        <ng-icon xuiInputLeftElement name="matSearchRound" />
        <input
          #queryField
          xuiInput
          cdkFocusInitial
          class="w-full"
          type="text"
          role="combobox"
          autocomplete="off"
          aria-autocomplete="list"
          aria-expanded="true"
          [attr.aria-controls]="listId"
          [attr.aria-label]="'Search ' + noun()"
          [attr.aria-activedescendant]="activeOptionId()"
          [placeholder]="placeholder()"
          [value]="query()"
          (input)="search(queryField.value)"
          (keydown)="key($event)"
        />
      </xui-input-group>
      <button
        type="button"
        xuiButton
        variant="ghost"
        size="sm"
        aria-label="Favourites only"
        [active]="favoritesOnly()"
        [xuiTooltip]="'Favourites only'"
        (click)="favoritesOnly.set(!favoritesOnly())"
      >
        <ng-icon name="matStarRound" />
      </button>
      <button
        type="button"
        xuiButton
        variant="ghost"
        size="sm"
        aria-label="Keep open"
        data-testid="picker-pin"
        [active]="pinned()"
        [xuiTooltip]="'Keep the picker open while you audition'"
        (click)="pinned.set(!pinned())"
      >
        <ng-icon name="matPushPinRound" />
      </button>
    </div>
    @if (extra(); as text) {
      <button
        type="button"
        class="hover:bg-active-overlay rounded-md px-2 py-1 text-left text-sm"
        [class.bg-active-overlay]="index() === -1"
        (click)="extraChosen.emit()"
      >
        {{ text }}
      </button>
    }
    <ul
      role="listbox"
      class="flex h-[min(55vh,26rem)] flex-col overflow-y-auto"
      [id]="listId"
      [attr.aria-label]="noun()"
    >
      @for (entry of shown(); track entry.id; let i = $index) {
        <!-- eslint-disable-next-line @angular-eslint/template/click-events-have-key-events, @angular-eslint/template/interactive-supports-focus -- the combobox pattern: focus stays in the search field, whose arrow keys and Enter drive the options through aria-activedescendant -->
        <li
          role="option"
          class="group flex cursor-pointer items-center gap-2 rounded-md px-2 py-1 text-sm"
          [class.bg-active-overlay]="i === index()"
          [class.font-semibold]="entry.id === current()"
          [id]="optionId(entry)"
          [attr.aria-selected]="i === index()"
          [attr.data-testid]="'pick-entry-' + entry.id"
          (pointerenter)="index.set(i)"
          (click)="choose(entry)"
        >
          <button
            type="button"
            class="size-3 shrink-0 rounded-full border border-current/30"
            [style.background]="entry.color ?? 'transparent'"
            [attr.aria-label]="'Colour tag of ' + entry.name"
            [xuiMenuTriggerFor]="colors"
            [xuiMenuTriggerData]="{ entry }"
            (click)="$event.stopPropagation()"
          ></button>
          <span class="min-w-0 flex-1 truncate">{{ entry.name }}</span>
          @if (entry.detail) {
            <span class="text-foreground-muted truncate text-xs">{{ entry.detail }}</span>
          }
          <button
            type="button"
            class="text-warning shrink-0"
            [class.opacity-0]="!entry.favorite"
            [class.group-hover:opacity-60]="!entry.favorite"
            [attr.aria-label]="(entry.favorite ? 'Unfavour ' : 'Favour ') + entry.name"
            [attr.aria-pressed]="!!entry.favorite"
            (click)="$event.stopPropagation(); favorite.emit({ entry, favorite: !entry.favorite })"
          >
            <ng-icon [name]="entry.favorite ? 'matStarRound' : 'matStarOutlineRound'" size="14" />
          </button>
        </li>
      } @empty {
        @if (!extra()) {
          <li class="text-foreground-muted py-8 text-center text-sm">Nothing matches “{{ query() }}”.</li>
        }
      }
    </ul>
    <ng-template #colors let-entry="entry">
      <xui-menu>
        <button type="button" xuiMenuItem (click)="color.emit({ entry, color: '' })">No colour</button>
        @for (c of tagColors; track c) {
          <button type="button" xuiMenuItem (click)="color.emit({ entry, color: c })">
            <span class="me-2 inline-block size-3 rounded-full" [style.background]="c"></span>{{ tagColorName(c) }}
          </button>
        }
      </xui-menu>
    </ng-template>
  `
})
export class PickerList {
  private static next = 0;
  protected readonly listId = `picker-list-${PickerList.next++}`;
  protected readonly tagColors = TagColors;
  protected readonly tagColorName = tagColorName;

  readonly entries = input.required<readonly PickerEntry[]>();
  readonly noun = input('presets');
  readonly placeholder = input('Search…');
  /** The id in use, shown in bold. */
  readonly current = input<string | null>(null);
  /** An extra first row (a bank slot typed as a number), chosen with ↵ when no entry is highlighted. */
  readonly extra = input<string | null>(null);
  readonly chosen = output<PickerEntry>();
  readonly extraChosen = output();
  readonly favorite = output<{ entry: PickerEntry; favorite: boolean }>();
  readonly color = output<{ entry: PickerEntry; color: string }>();
  readonly queryChange = output<string>();

  readonly pinned = signal(false);
  protected readonly query = signal('');
  protected readonly favoritesOnly = signal(false);
  protected readonly index = signal(0);
  protected readonly shown = computed(() => pickerEntries(this.entries(), this.query(), this.favoritesOnly()));
  protected readonly active = computed(() => this.shown()[this.index()] ?? null);
  protected readonly activeOptionId = computed(() => {
    const entry = this.active();
    return entry ? this.optionId(entry) : null;
  });

  constructor() {
    effect(() => {
      if (this.extra()) {
        untracked(() => this.index.set(-1));
      }
    });
  }

  protected optionId(entry: PickerEntry): string {
    return `${this.listId}-${entry.id.replace(/\W/g, '')}`;
  }

  protected search(query: string): void {
    this.query.set(query);
    this.index.set(0);
    this.queryChange.emit(query);
  }

  protected choose(entry: PickerEntry): void {
    this.chosen.emit(entry);
  }

  protected key(event: KeyboardEvent): void {
    const count = this.shown().length;
    const first = this.extra() ? -1 : 0;
    if (event.key === 'ArrowDown') {
      this.index.set(Math.min(count - 1, this.index() + 1));
    } else if (event.key === 'ArrowUp') {
      this.index.set(Math.max(first, this.index() - 1));
    } else if (event.key === 'Enter') {
      const entry = this.active();
      if (this.index() === -1) {
        this.extraChosen.emit();
      } else if (entry) {
        this.choose(entry);
      }
    } else {
      return;
    }

    event.preventDefault();
    const active = this.active();
    if (active) {
      document.getElementById(this.optionId(active))?.scrollIntoView({ block: 'nearest' });
    }
  }
}
