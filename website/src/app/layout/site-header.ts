import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { matDownloadRound } from '@ng-icons/material-icons/round';
import { REPOSITORY, SECTIONS } from '../site';

@Component({
  selector: 'cw-site-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, NgIcon],
  providers: [provideIcons({ matDownloadRound })],
  host: { class: 'sticky top-0 z-30 block border-b border-white/[0.06] bg-black/60 backdrop-blur-md' },
  template: `
    <div class="relative mx-auto flex h-14 max-w-[100rem] items-center gap-2 px-4 sm:px-6">
      <a routerLink="/" class="flex items-center gap-2.5 text-lg font-semibold tracking-tight">
        <img src="logo.png" alt="" width="28" height="28" class="size-7 rounded-md" />
        Cathode
      </a>

      <nav
        class="absolute start-1/2 hidden -translate-x-1/2 items-center gap-0.5 rounded-full border border-white/10 bg-white/5 p-1 xl:flex"
        aria-label="Main"
      >
        @for (item of sections; track item.id) {
          <a
            class="text-foreground-muted hover:text-foreground rounded-full px-3 py-1.5 text-sm font-medium whitespace-nowrap transition-colors hover:bg-white/10"
            routerLink="/"
            [fragment]="item.id"
            >{{ item.label }}</a
          >
        }
        <a
          class="text-foreground-muted hover:text-foreground rounded-full px-3 py-1.5 text-sm font-medium whitespace-nowrap transition-colors hover:bg-white/10"
          routerLink="/catalog"
          routerLinkActive="text-foreground bg-white/10"
          >Catalogue</a
        >
        <a
          class="text-foreground-muted hover:text-foreground rounded-full px-3 py-1.5 text-sm font-medium whitespace-nowrap transition-colors hover:bg-white/10"
          routerLink="/manual"
          routerLinkActive="text-foreground bg-white/10"
          >Manual</a
        >
      </nav>

      <div class="ms-auto flex items-center gap-1">
        <a
          class="text-foreground-muted hover:text-foreground px-3 py-1.5 text-sm font-medium transition-colors"
          [href]="repository"
          rel="noreferrer noopener"
          target="_blank"
          >GitHub</a
        >
        <a routerLink="/" fragment="download" class="buy">
          <ng-icon name="matDownloadRound" size="1rem" />
          Download
        </a>
      </div>
    </div>

    <!-- Below the desktop breakpoint the pill nav has no room, so the same links run as a scrollable row of their own
         under the bar. -->
    <nav
      class="flex [scrollbar-width:none] gap-1 overflow-x-auto px-3 pb-2 xl:hidden [&::-webkit-scrollbar]:hidden"
      aria-label="Sections"
    >
      @for (item of sections; track item.id) {
        <a
          class="text-foreground-muted hover:text-foreground shrink-0 rounded-full px-3 py-1.5 text-sm font-medium hover:bg-white/10"
          routerLink="/"
          [fragment]="item.id"
          >{{ item.label }}</a
        >
      }
      <a
        class="text-foreground-muted hover:text-foreground shrink-0 rounded-full px-3 py-1.5 text-sm font-medium hover:bg-white/10"
        routerLink="/catalog"
        routerLinkActive="text-foreground bg-white/10"
        >Catalogue</a
      >
      <a
        class="text-foreground-muted hover:text-foreground shrink-0 rounded-full px-3 py-1.5 text-sm font-medium hover:bg-white/10"
        routerLink="/manual"
        routerLinkActive="text-foreground bg-white/10"
        >Manual</a
      >
    </nav>
  `
})
export class SiteHeader {
  protected readonly sections = SECTIONS;
  protected readonly repository = REPOSITORY;
}
