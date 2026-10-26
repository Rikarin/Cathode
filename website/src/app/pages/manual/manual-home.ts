import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { matSearchRound } from '@ng-icons/material-icons/round';
import { CATEGORIES, knobPages, MANUAL_BLOCKS, MANUAL_CHAPTERS, PARTS, plainText, snippet } from '../../manual/manual';
import { Seo } from '../../seo';
import { SITE_URL } from '../../site';
import { ManualShell } from './manual-shell';

interface Hit {
  readonly title: string;
  /** Where it is: the chapter's part, the block's category, the knob's block. */
  readonly context: string;
  readonly summary: string;
  readonly link: readonly string[];
  readonly fragment?: string;
  /** Everything searched, lower case. */
  readonly haystack: string;
}

/** How many knobs a search lists before it says how many more there are. */
const KNOB_HITS = 40;

/** Builds the search index: every chapter, block and knob with the text a reader might look for. */
function buildIndex(): { chapters: Hit[]; blocks: Hit[]; knobs: Hit[] } {
  const chapters = MANUAL_CHAPTERS.map(chapter => ({
    title: chapter.title,
    context: chapter.part,
    summary: chapter.description,
    link: ['/manual', chapter.slug],
    haystack: [chapter.title, chapter.description, plainText(chapter.text ?? '')].join(' ').toLowerCase()
  }));

  const blocks = MANUAL_BLOCKS.map(block => {
    const summary = plainText(block.doc?.summary ?? '');
    return {
      title: block.name,
      context: block.category,
      summary: snippet(summary, 160),
      link: ['/manual/blocks', block.typeId],
      haystack: [block.name, block.category, block.typeId, summary, plainText(block.doc?.usage ?? '')]
        .join(' ')
        .toLowerCase()
    };
  });

  const knobs = MANUAL_BLOCKS.flatMap(block =>
    knobPages(block).flatMap(page =>
      page.knobs.map(knob => {
        const description = plainText(knob.description ?? '');
        const labels = [...new Set(knob.param.typeLabels ?? [])].join(' ');
        return {
          title: knob.name,
          context: page.name ? `${block.name} · ${page.name}` : block.name,
          summary: snippet(description, 160),
          link: ['/manual/blocks', block.typeId],
          fragment: `knob-${knob.id}`,
          haystack: [knob.name, knob.id, labels, description, knob.choices.map(c => c.label).join(' ')]
            .join(' ')
            .toLowerCase()
        };
      })
    )
  );

  return { chapters, blocks, knobs };
}

/** /manual: what the manual is, its contents, and a search over chapters, blocks and knobs. */
@Component({
  selector: 'cw-manual-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ManualShell, NgIcon, RouterLink],
  providers: [provideIcons({ matSearchRound })],
  template: `
    <cw-manual-shell here="/manual">
      <div class="max-w-[72ch]">
        <h1 class="display gradient rise text-start text-5xl sm:text-6xl" style="--i: 0">The manual</h1>
        <p class="rise text-foreground-muted mt-5 text-lg leading-relaxed" style="--i: 1">
          How to set Cathode up, build a rig and find your sound, and what every knob on every block does. The guide
          reads from start to finish; the block reference is for looking things up. Ranges and defaults are read from
          the engine itself, so they are what the app has today.
        </p>

        <label class="rise relative mt-8 block" style="--i: 2">
          <span class="sr-only">Search the manual</span>
          <ng-icon
            name="matSearchRound"
            size="1.25rem"
            class="text-foreground-subtle pointer-events-none absolute start-4 top-1/2 -translate-y-1/2"
          />
          <input
            type="search"
            class="text-foreground placeholder:text-foreground-subtle h-12 w-full rounded-xl border border-white/12 bg-white/5 ps-12 pe-4 text-base outline-none focus:border-white/25"
            placeholder="Search: scenes, sag, Plexi, tap tempo…"
            autocomplete="off"
            [value]="query()"
            (input)="query.set($any($event.target).value)"
          />
        </label>
      </div>

      @if (results(); as results) {
        <section class="mt-10 max-w-[72ch]" aria-live="polite">
          @if (!results.chapters.length && !results.blocks.length && !results.knobs.length) {
            <p class="text-foreground-muted">Nothing in the manual matches “{{ query() }}”.</p>
          }
          @if (results.chapters.length) {
            <h2 class="toc-heading">Chapters</h2>
            <ul class="hits">
              @for (hit of results.chapters; track hit.link) {
                <li>
                  <a [routerLink]="hit.link">
                    <span class="hit-title">{{ hit.title }}</span>
                    <span class="hit-context">{{ hit.context }}</span>
                    <span class="hit-summary">{{ hit.summary }}</span>
                  </a>
                </li>
              }
            </ul>
          }
          @if (results.blocks.length) {
            <h2 class="toc-heading">Blocks</h2>
            <ul class="hits">
              @for (hit of results.blocks; track hit.link) {
                <li>
                  <a [routerLink]="hit.link">
                    <span class="hit-title">{{ hit.title }}</span>
                    <span class="hit-context">{{ hit.context }}</span>
                    @if (hit.summary) {
                      <span class="hit-summary">{{ hit.summary }}</span>
                    }
                  </a>
                </li>
              }
            </ul>
          }
          @if (results.knobs.length) {
            <h2 class="toc-heading">Knobs</h2>
            <ul class="hits">
              @for (hit of results.knobs; track hit.fragment + hit.context) {
                <li>
                  <a [routerLink]="hit.link" [fragment]="hit.fragment">
                    <span class="hit-title">{{ hit.title }}</span>
                    <span class="hit-context">{{ hit.context }}</span>
                    @if (hit.summary) {
                      <span class="hit-summary">{{ hit.summary }}</span>
                    }
                  </a>
                </li>
              }
            </ul>
            @if (results.moreKnobs) {
              <p class="text-foreground-subtle mt-3 text-sm">
                And {{ results.moreKnobs }} more knobs; add a word to narrow it down.
              </p>
            }
          }
        </section>
      } @else {
        <section id="guide" class="mt-14">
          <h2 class="display text-3xl">The guide</h2>
          <p class="text-foreground-muted mt-3 max-w-[72ch]">
            Start with the first part if Cathode is new to you. The later chapters stand on their own.
          </p>
          @if (!parts.length) {
            <p class="text-foreground-subtle mt-6">The guide is being written.</p>
          }
          <div class="mt-6 grid gap-4 xl:grid-cols-2">
            @for (part of parts; track part.name) {
              <div class="card overflow-hidden">
                <h3 class="border-b border-white/[0.06] px-5 py-3 font-semibold">{{ part.name }}</h3>
                <ol class="divide-y divide-white/[0.06]">
                  @for (chapter of part.chapters; track chapter.slug) {
                    <li>
                      <a
                        [routerLink]="['/manual', chapter.slug]"
                        class="block px-5 py-3 transition-colors hover:bg-white/[0.04]"
                      >
                        <span class="block font-medium">{{ chapter.title }}</span>
                        <span class="text-foreground-muted mt-0.5 block text-sm leading-relaxed">{{
                          chapter.description
                        }}</span>
                      </a>
                    </li>
                  }
                </ol>
              </div>
            }
          </div>
        </section>

        <section id="blocks" class="mt-16">
          <h2 class="display text-3xl">The block reference</h2>
          <p class="text-foreground-muted mt-3 max-w-[72ch]">
            Every block you can put on the grid, with each of its knobs: what it does, its range and its default, page
            by page as the editor shows them.
          </p>
          <div class="mt-6 grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
            @for (category of categories; track category.name) {
              <div class="card overflow-hidden">
                <h3 class="border-b border-white/[0.06] px-5 py-3 font-semibold">{{ category.name }}</h3>
                <ul class="divide-y divide-white/[0.06]">
                  @for (block of category.blocks; track block.typeId) {
                    <li>
                      <a
                        [routerLink]="['/manual/blocks', block.typeId]"
                        class="flex items-baseline justify-between gap-3 px-5 py-2.5 text-sm transition-colors hover:bg-white/[0.04]"
                      >
                        <span class="font-medium">{{ block.name }}</span>
                        <span class="text-foreground-subtle text-xs tabular-nums">{{ block.params.length }} knobs</span>
                      </a>
                    </li>
                  }
                </ul>
              </div>
            }
          </div>
        </section>
      }
    </cw-manual-shell>
  `
})
export class ManualHome {
  protected readonly parts = PARTS;
  protected readonly categories = CATEGORIES;
  protected readonly query = signal('');

  private index?: ReturnType<typeof buildIndex>;

  protected readonly results = computed(() => {
    const words = this.query().trim().toLowerCase().split(/\s+/).filter(Boolean);

    if (!words.length) {
      return undefined;
    }

    this.index ??= buildIndex();
    const match = (hit: Hit) => words.every(word => hit.haystack.includes(word));
    // Hits whose title has the words come first; the rest keep the manual's order.
    const inTitle = (hit: Hit) => (words.every(word => hit.title.toLowerCase().includes(word)) ? 0 : 1);
    const find = (hits: readonly Hit[]) => hits.filter(match).sort((a, b) => inTitle(a) - inTitle(b));
    const knobs = find(this.index.knobs);

    return {
      chapters: find(this.index.chapters),
      blocks: find(this.index.blocks),
      knobs: knobs.slice(0, KNOB_HITS),
      moreKnobs: Math.max(0, knobs.length - KNOB_HITS)
    };
  });

  constructor() {
    inject(Seo).set({
      title: 'The Cathode manual — guide and block reference',
      description:
        'How to set up Cathode, build a rig and dial in your tone, and what every knob on every amp, cab and effect block does.',
      path: 'manual',
      structuredData: {
        '@context': 'https://schema.org',
        '@type': 'TechArticle',
        headline: 'The Cathode manual',
        url: `${SITE_URL}/manual`
      }
    });
  }
}
