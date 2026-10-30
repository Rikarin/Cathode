import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { provideIcons } from '@ng-icons/core';
import { matSkipNextRound, matSkipPreviousRound } from '@ng-icons/material-icons/round';
import { XuiButtonImports } from '@xui/button';
import { XuiIconImports } from '@xui/icon';
import { XuiSelectImports } from '@xui/select';
import { XuiTooltipImports } from '@xui/tooltip';
import { LibraryStore } from '../state/library.store';
import { SetlistPlayer } from '../state/setlist-player';
import type { Song } from './setlists';

/** The setlist choice for none (xui-select takes an id string). */
const NoSetlist = 'none';

/**
 * The Perform page's setlist bar: which setlist is being played, the song playing and the next one, previous and next
 * song (also ⇧⌘PgUp and ⇧⌘PgDn anywhere, and the songPrevious and songNext footswitch actions), and every song of the
 * set to jump to.
 */
@Component({
  selector: 'cth-setlist-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, XuiButtonImports, XuiIconImports, XuiSelectImports, XuiTooltipImports],
  providers: [provideIcons({ matSkipNextRound, matSkipPreviousRound })],
  host: {
    class: 'pane flex flex-col gap-3 p-4',
    role: 'region',
    'aria-label': 'Setlist',
    'data-testid': 'setlist-bar'
  },
  template: `
    <div class="flex flex-wrap items-center gap-3">
      <span class="text-foreground-muted text-xs font-medium" id="setlist-choice-label">Setlist</span>
      <xui-select
        class="w-48"
        aria-labelledby="setlist-choice-label"
        data-testid="setlist-choice"
        [items]="choices()"
        [itemText]="choiceName"
        [value]="player.setlist()?.id ?? none"
        (valueChange)="pick($event)"
      />
      @if (player.setlist(); as setlist) {
        <button
          type="button"
          xuiButton
          variant="outline"
          aria-label="Previous song"
          data-testid="song-previous"
          [disabled]="!player.previous()"
          [xuiTooltip]="'Previous song (⇧⌘PgUp)'"
          (click)="player.step(-1)"
        >
          <ng-icon xui name="matSkipPreviousRound" size="sm" />
        </button>
        <!-- The song playing and the next one, on an LCD between the transport buttons. -->
        <div class="lcd-glass flex min-w-0 flex-1 items-center gap-4 px-3 py-1.5">
          <div class="flex min-w-0 flex-1 flex-col" aria-live="polite" data-testid="song-current">
            @if (player.current(); as song) {
              <span class="truncate text-lg font-semibold">
                <span class="text-lcd-accent tabular-nums">{{ song.index + 1 }}</span> {{ song.title }}
              </span>
              <span class="text-lcd-muted truncate text-xs">{{ detail(song) }}</span>
            } @else {
              <span class="text-lcd-muted text-sm">
                {{
                  player.songs().length === 0
                    ? 'This setlist has no songs yet.'
                    : 'Not started: Next song loads the first.'
                }}
              </span>
            }
          </div>
          <div class="flex min-w-0 flex-col items-end text-right" data-testid="song-next">
            <span class="text-lcd-muted text-[10px] font-semibold tracking-wide uppercase">Next</span>
            <span class="max-w-56 truncate text-sm">
              @if (player.next(); as next) {
                <span class="text-lcd-muted tabular-nums">{{ next.index + 1 }}</span> {{ next.title }}
              } @else {
                <span class="text-lcd-muted">End of the set</span>
              }
            </span>
          </div>
        </div>
        <button
          type="button"
          xuiButton
          aria-label="Next song"
          data-testid="song-next-button"
          [disabled]="!player.next()"
          [xuiTooltip]="'Next song (⇧⌘PgDn)'"
          (click)="player.step(1)"
        >
          <ng-icon xui name="matSkipNextRound" size="sm" />
        </button>
        <a xuiButton variant="ghost" size="sm" routerLink="/setlists" [queryParams]="{ id: setlist.id }">Edit</a>
      } @else {
        <span class="text-foreground-muted text-sm">
          @if (library.setlists().length === 0) {
            No setlists yet: make one on the <a class="underline" routerLink="/setlists">Setlists</a> page.
          } @else {
            Pick a setlist to play it from here, with ⇧⌘PgDn, or from a footswitch.
          }
        </span>
      }
    </div>
    @if (player.setlist() && player.songs().length > 0) {
      <ol class="no-scrollbar flex gap-1.5 overflow-x-auto" aria-label="Songs">
        @for (song of player.songs(); track $index) {
          <li class="shrink-0">
            <button
              type="button"
              xuiButton
              size="sm"
              [variant]="song.index === player.index() ? 'default' : 'outline'"
              [disabled]="song.missing"
              [attr.aria-current]="song.index === player.index() ? 'true' : null"
              [attr.title]="song.missing ? 'Its preset is not in the library any more' : detail(song)"
              (click)="player.play(song.index)"
            >
              <span class="tabular-nums opacity-70">{{ song.index + 1 }}</span>
              <span class="max-w-40 truncate" [class.line-through]="song.missing">{{ song.title }}</span>
            </button>
          </li>
        }
      </ol>
    }
  `
})
export class SetlistBar {
  protected readonly player = inject(SetlistPlayer);
  protected readonly library = inject(LibraryStore);

  protected readonly none = NoSetlist;
  protected readonly choices = computed(() => [NoSetlist, ...this.library.setlists().map(s => s.id)]);
  protected readonly choiceName = (id: string) =>
    id === NoSetlist ? '(none)' : (this.library.setlists().find(s => s.id === id)?.name ?? id);

  constructor() {
    void this.library.refreshSetlists();
  }

  /** The preset and scene under a song's title, when the title is a label of its own. */
  protected detail(song: Song): string {
    const scene = `Scene ${song.entry.scene + 1}`;
    return song.presetName && song.title !== song.presetName ? `${song.presetName} · ${scene}` : scene;
  }

  protected pick(id: string | null): void {
    const setlist = id && id !== NoSetlist ? id : null;
    if (setlist !== (this.player.setlist()?.id ?? null)) {
      void this.player.pick(setlist);
    }
  }
}
