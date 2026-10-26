import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  input,
  model,
  output,
  signal,
  untracked
} from '@angular/core';
import type { BlockTypeInfo } from '@cathode/api-client';
import { BlockCategories, CthBlockTile } from '@cathode/audio-ui';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { matAddRound, matSearchRound } from '@ng-icons/material-icons/round';
import { XuiDialogImports } from '@xui/dialog';
import { XuiInputImports } from '@xui/input';
import { XuiKbdImports } from '@xui/kbd';

export interface PickerItem {
  type: BlockTypeInfo;
  /** Position in the flattened list, which is the keyboard order. */
  index: number;
  full: boolean;
}

export interface PickerGroup {
  category: string;
  items: PickerItem[];
}

const KnownOrder = Object.keys(BlockCategories);

/**
 * The types that match every word of the query (in name, category or type id), grouped by category: known categories
 * in signal-chain order, then new ones alphabetically, and blocks by name within each.
 */
export function groupBlocks(
  types: readonly BlockTypeInfo[],
  query: string,
  isFull: (type: BlockTypeInfo) => boolean
): PickerGroup[] {
  const words = query.toLowerCase().split(/\s+/).filter(Boolean);
  const byCategory = new Map<string, BlockTypeInfo[]>();
  for (const type of types) {
    const text = `${type.name} ${type.category} ${type.typeId}`.toLowerCase();
    if (words.every(word => text.includes(word))) {
      byCategory.set(type.category, [...(byCategory.get(type.category) ?? []), type]);
    }
  }

  const rank = (category: string) => {
    const known = KnownOrder.indexOf(category);
    return known < 0 ? KnownOrder.length : known;
  };
  const categories = [...byCategory.keys()].sort((a, b) => rank(a) - rank(b) || a.localeCompare(b));
  let index = 0;
  return categories.map(category => ({
    category,
    items: byCategory
      .get(category)!
      .sort((a, b) => a.name.localeCompare(b.name))
      .map(type => ({ type, index: index++, full: isFull(type) }))
  }));
}

/** The item Enter should add: an available exact name match, then a name starting with the query, then the first. */
export function bestMatch(items: readonly PickerItem[], query: string): PickerItem | null {
  const wanted = query.trim().toLowerCase();
  const available = items.filter(item => !item.full);
  const name = (item: PickerItem) => item.type.name.toLowerCase();
  return (
    available.find(item => name(item) === wanted) ??
    available.find(item => name(item).startsWith(wanted)) ??
    available[0] ??
    null
  );
}

/**
 * The block palette: every placeable type as a small coloured tile in one grid that fits without scrolling, in
 * signal-chain order by category (categories the editor does not know yet follow, alphabetically); the tile's colour
 * and its tooltip tell the category. Typing filters by name, category or type id; the arrow keys move through the
 * tiles while the search field keeps focus, Enter adds the highlighted block and Escape closes.
 */
@Component({
  selector: 'cth-block-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CthBlockTile, NgIcon, XuiDialogImports, XuiInputImports, XuiKbdImports],
  providers: [provideIcons({ matAddRound, matSearchRound })],
  template: `
    <xui-dialog
      icon="matAddRound"
      [title]="title()"
      class="max-h-[94vh] w-[min(64rem,calc(100vw-2rem))] max-w-none"
      [(open)]="open"
    >
      <xui-dialog-body class="flex min-h-0 flex-col gap-4">
        <xui-input-group class="w-full">
          <ng-icon xuiInputLeftElement name="matSearchRound" />
          <input
            xuiInput
            cdkFocusInitial
            class="w-full"
            type="text"
            role="combobox"
            aria-label="Search blocks"
            aria-autocomplete="list"
            aria-expanded="true"
            aria-controls="block-picker-list"
            autocomplete="off"
            spellcheck="false"
            placeholder="Search blocks…"
            [attr.aria-activedescendant]="activeItem() ? optionId(activeItem()!) : null"
            [value]="query()"
            (input)="search($any($event.target).value)"
            (keydown)="key($event)"
          />
        </xui-input-group>

        <div
          id="block-picker-list"
          role="listbox"
          aria-label="Blocks"
          class="-mx-1 grid min-h-0 grid-cols-[repeat(auto-fill,minmax(6.75rem,1fr))] content-start gap-1 overflow-y-auto px-1 pt-1.5 pb-2"
        >
          @for (item of items(); track item.type.typeId) {
            <cth-block-tile
              role="option"
              class="outline-foreground h-[4.75rem] cursor-pointer outline-offset-1 hover:-translate-y-0.5 hover:brightness-110"
              [class.outline-2]="item.index === active()"
              [class.cursor-not-allowed]="item.full"
              [class.opacity-40]="item.full"
              [id]="optionId(item)"
              [attr.data-testid]="'pick-' + item.type.typeId"
              [attr.aria-label]="item.type.name + (item.full ? ', limit reached' : '')"
              [attr.aria-selected]="item.index === active()"
              [attr.aria-disabled]="item.full"
              [attr.title]="item.type.name + ' · ' + item.type.category"
              [name]="item.type.name"
              [category]="item.type.category"
              [typeId]="item.type.typeId"
              size="lg"
              (pointerenter)="active.set(item.index)"
              (click)="choose(item)"
            >
              @if (item.full) {
                <span class="absolute top-0.5 right-0.5 rounded bg-black/40 px-1 text-[10px]">
                  max {{ item.type.maxInstances }}
                </span>
              }
            </cth-block-tile>
          } @empty {
            <p class="text-foreground-muted col-span-full py-10 text-center text-sm">
              No blocks match “{{ query() }}”.
            </p>
          }
        </div>

        <p class="text-foreground-muted flex flex-wrap items-center gap-x-4 gap-y-1 text-xs" aria-hidden="true">
          <span><kbd xuiKbd>←</kbd> <kbd xuiKbd>→</kbd> <kbd xuiKbd>↑</kbd> <kbd xuiKbd>↓</kbd> move</span>
          <span><kbd xuiKbd>↵</kbd> add</span>
          <span><kbd xuiKbd>esc</kbd> close</span>
        </p>
      </xui-dialog-body>
    </xui-dialog>
  `
})
export class BlockPicker {
  readonly types = input.required<readonly BlockTypeInfo[]>();
  /** Whether a type has reached its instance limit in the preset. */
  readonly isFull = input<(type: BlockTypeInfo) => boolean>(() => false);
  readonly open = model(false);
  /** "Add a block", or "Replace … with" when the palette replaces a block (keeping its cables). */
  readonly title = input('Add a block');
  readonly picked = output<BlockTypeInfo>();

  protected readonly query = signal('');
  protected readonly active = signal(0);

  protected readonly groups = computed(() => groupBlocks(this.types(), this.query(), this.isFull()));
  protected readonly items = computed(() => this.groups().flatMap(g => g.items));
  protected readonly activeItem = computed(() => this.items()[this.active()] ?? null);

  constructor() {
    // A fresh search every time it opens.
    effect(() => {
      if (this.open()) {
        untracked(() => this.search(''));
      }
    });
  }

  protected optionId(item: PickerItem): string {
    return `block-option-${item.type.typeId}`;
  }

  protected search(query: string): void {
    this.query.set(query);
    this.active.set(bestMatch(this.items(), query)?.index ?? 0);
  }

  protected choose(item: PickerItem): void {
    if (!item.full) {
      this.open.set(false);
      this.picked.emit(item.type);
    }
  }

  protected key(event: KeyboardEvent): void {
    const count = this.items().length;
    let next: number | null = null;
    if (event.key === 'ArrowRight') {
      next = Math.min(count - 1, this.active() + 1);
    } else if (event.key === 'ArrowLeft') {
      next = Math.max(0, this.active() - 1);
    } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      next = this.vertical(event.key === 'ArrowDown' ? 1 : -1);
    } else if (event.key === 'Enter') {
      const item = this.activeItem();
      if (item) {
        this.choose(item);
      }
    } else {
      return;
    }

    event.preventDefault();
    if (next !== null && count > 0) {
      this.active.set(next);
      document.getElementById(this.optionId(this.items()[next]))?.scrollIntoView({ block: 'nearest' });
    }
  }

  /** The tile in the nearest row above or below, closest to the current column. */
  private vertical(direction: 1 | -1): number | null {
    const items = this.items();
    const current = this.activeItem();
    const from = current && document.getElementById(this.optionId(current))?.getBoundingClientRect();
    if (!from) {
      return null;
    }

    const centre = from.left + from.width / 2;
    let best: { index: number; dy: number; dx: number } | null = null;
    for (const item of items) {
      const rect = document.getElementById(this.optionId(item))?.getBoundingClientRect();
      const dy = rect ? (rect.top - from.top) * direction : 0;
      if (!rect || dy < from.height / 2) {
        continue;
      }

      const dx = Math.abs(rect.left + rect.width / 2 - centre);
      if (!best || dy < best.dy - 1 || (Math.abs(dy - best.dy) <= 1 && dx < best.dx)) {
        best = { index: item.index, dy, dx };
      }
    }

    return best?.index ?? null;
  }
}
