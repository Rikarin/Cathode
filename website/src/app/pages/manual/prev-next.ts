import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { matArrowBackRound, matArrowForwardRound } from '@ng-icons/material-icons/round';

export interface PageLink {
  readonly title: string;
  readonly link: readonly string[];
}

/** The previous and next page at the foot of a chapter or a block. */
@Component({
  selector: 'cw-prev-next',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, RouterLink],
  providers: [provideIcons({ matArrowBackRound, matArrowForwardRound })],
  host: { class: 'mt-16 grid gap-3 border-t border-white/[0.06] pt-8 sm:grid-cols-2' },
  template: `
    @if (prev(); as prev) {
      <a [routerLink]="prev.link" class="card card-interactive flex flex-col gap-1 rounded-2xl px-5 py-4" rel="prev">
        <span class="text-foreground-subtle inline-flex items-center gap-1 text-xs">
          <ng-icon name="matArrowBackRound" size="0.875rem" /> Previous
        </span>
        <span class="font-medium">{{ prev.title }}</span>
      </a>
    } @else {
      <span class="hidden sm:block"></span>
    }
    @if (next(); as next) {
      <a
        [routerLink]="next.link"
        class="card card-interactive flex flex-col items-end gap-1 rounded-2xl px-5 py-4 text-end"
        rel="next"
      >
        <span class="text-foreground-subtle inline-flex items-center gap-1 text-xs">
          Next <ng-icon name="matArrowForwardRound" size="0.875rem" />
        </span>
        <span class="font-medium">{{ next.title }}</span>
      </a>
    }
  `
})
export class PrevNext {
  readonly prev = input<PageLink>();
  readonly next = input<PageLink>();
}
