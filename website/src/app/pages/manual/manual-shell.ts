import { NgTemplateOutlet, ViewportScroller } from '@angular/common';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  inject,
  Injector,
  input,
  signal
} from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { matMenuBookRound } from '@ng-icons/material-icons/round';
import { SiteFooter } from '../../layout/site-footer';
import { SiteHeader } from '../../layout/site-header';
import { CATEGORIES, PARTS } from '../../manual/manual';

/** A section of the page being read, listed under its link in the contents. */
export interface OutlineItem {
  readonly id: string;
  readonly text: string;
}

/**
 * The frame of every manual page: the site's header and footer, the manual's contents in a sticky sidebar on a wide
 * screen and in a drawer above the page on a phone, and the page itself projected beside it.
 */
@Component({
  selector: 'cw-manual-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, NgTemplateOutlet, RouterLink, RouterLinkActive, SiteHeader, SiteFooter],
  providers: [provideIcons({ matMenuBookRound })],
  host: { class: 'block min-h-svh' },
  template: `
    <cw-site-header />

    <div class="relative overflow-x-clip">
      <div class="glow pointer-events-none absolute inset-x-0 top-0 h-[28rem] opacity-60" aria-hidden="true"></div>

      <div
        class="manual-page relative mx-auto max-w-[90rem] px-4 sm:px-6 lg:grid lg:grid-cols-[15.5rem_minmax(0,1fr)] lg:gap-10 xl:gap-14"
      >
        <aside class="hidden lg:block" aria-label="Manual">
          <nav
            class="toc-scroll sticky top-26 max-h-[calc(100svh-6.5rem)] [scrollbar-width:thin] overflow-y-auto overscroll-contain py-10 pe-2 xl:top-14 xl:max-h-[calc(100svh-3.5rem)]"
          >
            <ng-container *ngTemplateOutlet="contents" />
          </nav>
        </aside>

        <div class="min-w-0">
          <details
            class="card mt-6 overflow-hidden rounded-2xl lg:hidden"
            [open]="drawerOpen()"
            (toggle)="toggled($any($event.target).open)"
          >
            <summary
              class="flex cursor-pointer list-none items-center gap-2 px-4 py-3 text-sm font-medium select-none [&::-webkit-details-marker]:hidden"
            >
              <ng-icon name="matMenuBookRound" size="1.125rem" class="text-signal" />
              Manual contents
              <span class="text-foreground-subtle ms-auto text-xs">{{ drawerOpen() ? 'Close' : 'Open' }}</span>
            </summary>
            <nav
              class="toc-scroll relative max-h-[60svh] overflow-y-auto border-t border-white/[0.06] px-4 py-4"
              aria-label="Manual"
            >
              <ng-container *ngTemplateOutlet="contents" />
            </nav>
          </details>

          <main id="main" class="pt-8 pb-24 lg:pt-12">
            <ng-content />
          </main>
        </div>
      </div>
    </div>

    <cw-site-footer />

    <ng-template #contents>
      <a
        routerLink="/manual"
        class="toc-link font-medium"
        routerLinkActive="toc-active"
        [routerLinkActiveOptions]="{ exact: true }"
        >Manual home</a
      >

      <p class="toc-title">Guide</p>
      @for (part of parts; track part.name) {
        <p class="toc-heading">{{ part.name }}</p>
        <ul>
          @for (chapter of part.chapters; track chapter.slug) {
            <li>
              <a class="toc-link" [routerLink]="['/manual', chapter.slug]" routerLinkActive="toc-active">{{
                chapter.title
              }}</a>
              @if (here() === '/manual/' + chapter.slug) {
                <ng-container *ngTemplateOutlet="outlineList" />
              }
            </li>
          }
        </ul>
      }

      <p class="toc-title">Block reference</p>
      @for (category of categories; track category.name) {
        <p class="toc-heading">{{ category.name }}</p>
        <ul>
          @for (block of category.blocks; track block.typeId) {
            <li>
              <a class="toc-link" [routerLink]="['/manual/blocks', block.typeId]" routerLinkActive="toc-active">{{
                block.name
              }}</a>
              @if (here() === '/manual/blocks/' + block.typeId) {
                <ng-container *ngTemplateOutlet="outlineList" />
              }
            </li>
          }
        </ul>
      }
    </ng-template>

    <ng-template #outlineList>
      @if (outline().length) {
        <ul class="ms-3 mt-1 mb-2 border-s border-white/10">
          @for (item of outline(); track item.id) {
            <li>
              <a class="toc-sub" [routerLink]="here()" [fragment]="item.id">{{ item.text }}</a>
            </li>
          }
        </ul>
      }
    </ng-template>
  `
})
export class ManualShell {
  /** The page's path ('/manual/the-grid'), so its outline shows under its own link. */
  readonly here = input('');
  readonly outline = input<readonly OutlineItem[]>([]);

  protected readonly parts = PARTS;
  protected readonly categories = CATEGORIES;
  protected readonly drawerOpen = signal(false);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  constructor() {
    const router = inject(Router);
    const scroller = inject(ViewportScroller);
    const injector = inject(Injector);
    const subscription = router.events.subscribe(event => {
      if (event instanceof NavigationEnd) {
        this.drawerOpen.set(false);
        afterNextRender(() => this.revealActive(), { injector });
      }
    });
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());

    // Anchors land below the sticky header rather than under it.
    afterNextRender(() => {
      scroller.setOffset(() => [0, (document.querySelector<HTMLElement>('cw-site-header')?.offsetHeight ?? 56) + 16]);
      this.revealActive();
    });
  }

  protected toggled(open: boolean): void {
    this.drawerOpen.set(open);

    if (open) {
      this.revealActive();
    }
  }

  /** Scrolls the contents so the page being read is in view, when it is not: the list is longer than a screen. */
  private revealActive(): void {
    for (const nav of this.host.nativeElement.querySelectorAll<HTMLElement>('.toc-scroll')) {
      const active = nav.querySelector<HTMLElement>(`a[href="${this.here()}"]`);

      if (active && (active.offsetTop < nav.scrollTop || active.offsetTop > nav.scrollTop + nav.clientHeight - 40)) {
        nav.scrollTop = active.offsetTop - nav.clientHeight / 3;
      }
    }
  }
}
