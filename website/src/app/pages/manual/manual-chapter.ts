import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { chapterBySlug, MANUAL_CHAPTERS, neighbours, plainText, snippet } from '../../manual/manual';
import { renderMarkdown } from '../../manual/markdown';
import { Seo } from '../../seo';
import { SITE_URL } from '../../site';
import { ManualShell } from './manual-shell';
import { PrevNext } from './prev-next';
import { Prose } from './prose';

/** /manual/<slug>: one chapter of the guide, its Markdown rendered at build time. */
@Component({
  selector: 'cw-manual-chapter',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ManualShell, PrevNext, Prose, RouterLink],
  template: `
    <cw-manual-shell [here]="'/manual/' + slug()" [outline]="outline()">
      @if (chapter(); as chapter) {
        <article class="mx-auto max-w-[72ch] lg:mx-0">
          <p class="text-foreground-subtle text-sm">
            <a routerLink="/manual" class="hover:text-foreground">Manual</a>
            <span aria-hidden="true"> / </span>{{ chapter.part }}
          </p>
          <h1 class="display mt-3 text-4xl sm:text-5xl">{{ chapter.title }}</h1>
          <p class="text-foreground-muted mt-4 text-lg leading-relaxed">{{ chapter.description }}</p>

          @if (rendered(); as rendered) {
            <div class="mt-10" [cwProse]="rendered.html"></div>
          } @else {
            <p class="callout callout-note mt-10">This chapter is being written. Check back soon.</p>
          }

          <cw-prev-next
            [prev]="prev() ? { title: prev()!.title, link: ['/manual', prev()!.slug] } : undefined"
            [next]="next() ? { title: next()!.title, link: ['/manual', next()!.slug] } : undefined"
          />
        </article>
      } @else {
        <div class="mx-auto max-w-[72ch] py-16 text-center lg:mx-0 lg:text-start">
          <h1 class="display text-4xl">No such chapter</h1>
          <p class="text-foreground-muted mt-4">
            The manual has no chapter at this address. <a routerLink="/manual" class="underline">See the contents</a>.
          </p>
        </div>
      }
    </cw-manual-shell>
  `
})
export class ManualChapter {
  private readonly seo = inject(Seo);

  protected readonly slug = signal('');
  protected readonly chapter = computed(() => chapterBySlug(this.slug()));

  protected readonly rendered = computed(() => {
    const chapter = this.chapter();
    return chapter?.text ? renderMarkdown(chapter.text, `manual/${chapter.slug}`) : undefined;
  });

  protected readonly outline = computed(() => (this.rendered()?.headings ?? []).filter(heading => heading.depth === 2));

  private readonly around = computed(() =>
    neighbours(
      MANUAL_CHAPTERS,
      MANUAL_CHAPTERS.findIndex(chapter => chapter.slug === this.slug())
    )
  );
  protected readonly prev = computed(() => this.around().prev);
  protected readonly next = computed(() => this.around().next);

  constructor() {
    const subscription = inject(ActivatedRoute).paramMap.subscribe(params => {
      this.slug.set(params.get('slug') ?? '');
      this.describe();
    });
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }

  private describe(): void {
    const chapter = this.chapter();

    if (!chapter) {
      this.seo.set({
        title: 'Not found — Cathode manual',
        description: 'There is no chapter of the Cathode manual at this address.',
        path: `manual/${this.slug()}`,
        noindex: true
      });
      return;
    }

    const path = `manual/${chapter.slug}`;
    const description = snippet(chapter.description || plainText(chapter.text ?? ''));

    this.seo.set({
      title: `${chapter.title} — Cathode manual`,
      description,
      path,
      structuredData: {
        '@context': 'https://schema.org',
        '@type': 'TechArticle',
        headline: chapter.title,
        description,
        url: `${SITE_URL}/${path}`,
        isPartOf: { '@type': 'CreativeWork', name: 'The Cathode manual', url: `${SITE_URL}/manual` }
      }
    });
  }
}
