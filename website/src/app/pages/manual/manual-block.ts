import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  ampFamilies,
  blockById,
  defaultOn,
  type Knob,
  knobPages,
  labelOn,
  MANUAL_BLOCKS,
  namesByType,
  neighbours,
  plainText,
  snippet
} from '../../manual/manual';
import { renderMarkdown, slugify } from '../../manual/markdown';
import { Seo } from '../../seo';
import { SITE_URL } from '../../site';
import { ManualShell, type OutlineItem } from './manual-shell';
import { PrevNext } from './prev-next';
import { Prose } from './prose';

/** Lists longer than this fold away behind their count. */
const LONG_LIST = 16;

/** How a knob's name and presence change with the block's type, put as briefly as it can be. */
type ByType =
  | { readonly kind: 'only'; readonly types: string }
  | { readonly kind: 'not'; readonly types: string }
  | { readonly kind: 'names'; readonly rows: readonly { readonly name: string; readonly types: string }[] };

/** /manual/blocks/<typeId>: one block's reference, its knobs read from the engine and described by blocks/<typeId>.ts. */
@Component({
  selector: 'cw-manual-block',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ManualShell, NgTemplateOutlet, PrevNext, Prose, RouterLink],
  template: `
    <cw-manual-shell [here]="here()" [outline]="outline()">
      @if (block(); as block) {
        <article class="mx-auto max-w-[72ch] lg:mx-0">
          <p class="text-foreground-subtle text-sm">
            <a routerLink="/manual" class="hover:text-foreground">Manual</a>
            <span aria-hidden="true"> / </span>
            <a routerLink="/manual" fragment="blocks" class="hover:text-foreground">Block reference</a>
            <span aria-hidden="true"> / </span>{{ block.category }}
          </p>
          <h1 class="display mt-3 text-4xl sm:text-5xl">{{ block.name }}</h1>
          <p class="text-foreground-muted mt-4 flex flex-wrap gap-x-4 gap-y-1 text-sm">
            <span>{{ block.category }} block</span>
            <span>{{ block.channels > 1 ? 'Channels A–' + 'ABCD'[block.channels - 1] : 'One channel' }}</span>
            <span>Up to {{ block.maxInstances }} in a preset</span>
            <span>{{ block.params.length }} parameters</span>
          </p>

          @if (text().summary; as summary) {
            <div class="mt-8" [cwProse]="summary"></div>
          } @else {
            <p class="callout callout-note mt-8">
              This block's description is being written. The knobs below are read from the engine, so they are complete.
            </p>
          }

          @if (text().usage; as usage) {
            <h2 id="usage" class="section-heading">
              Using it<a class="anchor" [routerLink]="here()" fragment="usage" aria-label="Link to this section">#</a>
            </h2>
            <div [cwProse]="usage"></div>
          }

          <h2 id="knobs" class="section-heading">
            Knobs<a class="anchor" [routerLink]="here()" fragment="knobs" aria-label="Link to this section">#</a>
          </h2>

          @if (typeParam(); as typeParam) {
            @if (labelled()) {
              <div class="card mt-4 flex flex-col gap-3 rounded-2xl p-4 sm:flex-row sm:items-center">
                <label class="flex min-w-0 flex-1 flex-col gap-1.5 text-sm sm:flex-row sm:items-center sm:gap-3">
                  <span class="text-foreground-muted shrink-0">Show the knobs of</span>
                  <select
                    class="text-foreground h-10 min-w-0 flex-1 rounded-lg border border-white/12 bg-zinc-950 px-3 outline-none focus:border-white/25"
                    [value]="picked()"
                    (change)="picked.set(+$any($event.target).value)"
                  >
                    <option value="-1">Every {{ typeName() }}</option>
                    @if (block.typeId === 'circuitamp') {
                      @for (family of families(); track family.name) {
                        <optgroup [label]="family.name">
                          @for (model of family.models; track model.index) {
                            <option [value]="model.index">{{ model.name }}</option>
                          }
                        </optgroup>
                      }
                    } @else {
                      @for (option of typeParam.options; track $index) {
                        <option [value]="$index">{{ option }}</option>
                      }
                    }
                  </select>
                </label>
                <p class="text-foreground-subtle text-xs sm:max-w-[16rem]">
                  @if (picked() < 0) {
                    Some knobs are named differently, or are missing, on some types. Pick one to see its own panel.
                  } @else {
                    {{ hiddenCount() }} {{ hiddenCount() === 1 ? 'knob' : 'knobs' }} that this type does not have
                    {{ hiddenCount() === 1 ? 'is' : 'are' }} hidden.
                  }
                </p>
              </div>
            }
          }

          @for (page of pages(); track page.name) {
            @if (page.name) {
              <h3 class="page-heading" [id]="'page-' + slug(page.name)">
                {{ page.name }}
                <a
                  class="anchor"
                  [routerLink]="here()"
                  [fragment]="'page-' + slug(page.name)"
                  aria-label="Link to this page"
                  >#</a
                >
              </h3>
            }
            <dl class="knobs">
              @for (knob of page.knobs; track knob.id) {
                @let name = nameOf(knob);
                @if (name) {
                  <div class="knob" [id]="'knob-' + knob.id">
                    @for (alias of knob.ids.slice(1); track alias) {
                      <span class="knob-alias" [id]="'knob-' + alias"></span>
                    }
                    <dt>
                      <a class="knob-name" [routerLink]="here()" [fragment]="'knob-' + knob.id">{{ name }}</a>
                      @if (picked() >= 0 && name !== knob.name) {
                        <span class="text-foreground-subtle ms-2 text-xs font-normal">({{ knob.name }})</span>
                      }
                    </dt>
                    <dd>
                      <p class="knob-meta">
                        @if (knob.range) {
                          <span><span class="knob-label">Range</span> {{ knob.range }}</span>
                        } @else if (knob.param.kind === 'switch') {
                          <span><span class="knob-label">Switch</span> Off / On</span>
                        }
                        <span><span class="knob-label">Default</span> {{ defaultText(knob) }}</span>
                      </p>

                      @if (descriptions().get(knob.id); as description) {
                        <div class="knob-text" [cwProse]="description"></div>
                      }

                      @if (knob.choices.length) {
                        @let detailed = hasDetail(knob);
                        @if (knob.choices.length > longList) {
                          <details class="knob-more">
                            <summary>{{ knob.choices.length }} choices</summary>
                            <ng-container *ngTemplateOutlet="choiceList; context: { $implicit: knob, detailed }" />
                          </details>
                        } @else {
                          <ng-container *ngTemplateOutlet="choiceList; context: { $implicit: knob, detailed }" />
                        }
                      }

                      @if (picked() < 0) {
                        @if (byType().get(knob.id); as byType) {
                          @switch (byType.kind) {
                            @case ('only') {
                              <p class="knob-types"><span class="knob-label">Only on</span> {{ byType.types }}</p>
                            }
                            @case ('not') {
                              <p class="knob-types"><span class="knob-label">Not on</span> {{ byType.types }}</p>
                            }
                            @case ('names') {
                              <details class="knob-more">
                                <summary>Name on each {{ typeName() }}</summary>
                                <div class="table-wrap">
                                  <table>
                                    <tbody>
                                      @for (row of byType.rows; track row.name) {
                                        <tr>
                                          <th scope="row">{{ row.name || 'Not on' }}</th>
                                          <td>{{ row.types }}</td>
                                        </tr>
                                      }
                                    </tbody>
                                  </table>
                                </div>
                              </details>
                            }
                          }
                        }
                      }
                    </dd>
                  </div>
                }
              }
            </dl>
          }

          @if (text().tips; as tips) {
            <h2 id="tips" class="section-heading">
              Tips<a class="anchor" [routerLink]="here()" fragment="tips" aria-label="Link to this section">#</a>
            </h2>
            <div [cwProse]="tips"></div>
          }

          <cw-prev-next
            [prev]="prev() ? { title: prev()!.name, link: ['/manual/blocks', prev()!.typeId] } : undefined"
            [next]="next() ? { title: next()!.name, link: ['/manual/blocks', next()!.typeId] } : undefined"
          />
        </article>
      } @else {
        <div class="mx-auto max-w-[72ch] py-16 text-center lg:mx-0 lg:text-start">
          <h1 class="display text-4xl">No such block</h1>
          <p class="text-foreground-muted mt-4">
            Cathode has no block at this address.
            <a routerLink="/manual" fragment="blocks" class="underline">See them all</a>.
          </p>
        </div>
      }
    </cw-manual-shell>

    <ng-template #choiceList let-knob let-detailed="detailed">
      @if (detailed) {
        <dl class="choices">
          @for (choice of asKnob(knob).choices; track $index) {
            <div>
              <dt>{{ choice.label }}</dt>
              <dd>
                @if (choice.basedOn) {
                  <span class="text-foreground-subtle">Based on {{ choice.basedOn }}.</span>
                }
                @if (optionText().get(asKnob(knob).id + '/' + choice.label); as text) {
                  <div [cwProse]="text"></div>
                }
              </dd>
            </div>
          }
        </dl>
      } @else {
        <ul class="chips">
          @for (choice of asKnob(knob).choices; track $index) {
            <li>{{ choice.label }}</li>
          }
        </ul>
      }
    </ng-template>
  `
})
export class ManualBlock {
  private readonly seo = inject(Seo);

  protected readonly longList = LONG_LIST;
  protected readonly typeId = signal('');
  protected readonly block = computed(() => blockById(this.typeId()));
  protected readonly here = computed(() => `/manual/blocks/${this.typeId()}`);

  protected readonly pages = computed(() => {
    const block = this.block();
    return block ? knobPages(block) : [];
  });

  /** The block's Type knob, when it has one with choices. */
  protected readonly typeParam = computed(() =>
    this.block()?.params.find(param => param.id === 'type' && param.kind === 'choice' && param.options?.length)
  );
  protected readonly typeName = computed(() =>
    this.block()?.typeId === 'circuitamp' ? 'model' : (this.typeParam()?.name.toLowerCase() ?? 'type')
  );

  /** Whether any knob changes its name or disappears with the type, which is when the type picker is worth having. */
  protected readonly labelled = computed(() => !!this.block()?.params.some(param => param.typeLabels));
  protected readonly families = computed(() => ampFamilies(this.typeParam()?.options ?? []));

  /** The type whose panel is shown, or -1 for every type at once. */
  protected readonly picked = signal(-1);

  protected readonly hiddenCount = computed(() => {
    const picked = this.picked();
    return this.pages().reduce((sum, page) => sum + page.knobs.filter(knob => !labelOn(knob, picked)).length, 0);
  });

  /** The block's own text, rendered once. */
  protected readonly text = computed(() => {
    const block = this.block();
    const doc = block?.doc;
    const path = `manual/blocks/${block?.typeId}`;
    const taken = new Set(['usage', 'knobs', 'tips', ...this.pages().map(page => `page-${slugify(page.name)}`)]);
    const render = (markdown: string | undefined) =>
      markdown?.trim() ? renderMarkdown(markdown, path, taken).html : undefined;

    return { summary: render(doc?.summary), usage: render(doc?.usage), tips: render(doc?.tips) };
  });

  protected readonly descriptions = computed(() => {
    const path = `manual/blocks/${this.typeId()}`;
    const map = new Map<string, string>();

    for (const knob of this.pages().flatMap(page => page.knobs)) {
      if (knob.description) {
        map.set(knob.id, renderMarkdown(knob.description, path).html);
      }
    }

    return map;
  });

  protected readonly optionText = computed(() => {
    const path = `manual/blocks/${this.typeId()}`;
    const map = new Map<string, string>();

    for (const knob of this.pages().flatMap(page => page.knobs)) {
      for (const choice of knob.choices) {
        if (choice.description) {
          map.set(`${knob.id}/${choice.label}`, renderMarkdown(choice.description, path).html);
        }
      }
    }

    return map;
  });

  protected readonly byType = computed(() => {
    const types = this.typeParam()?.options ?? [];
    const map = new Map<string, ByType>();

    for (const knob of this.pages().flatMap(page => page.knobs)) {
      const groups = namesByType(knob, types);

      if (groups.length < 2) {
        continue;
      }

      const shown = groups.filter(group => group.name);
      const hidden = groups.find(group => !group.name)?.types ?? [];

      if (shown.length === 1 && shown[0].name === knob.name) {
        const on = shown[0].types;
        map.set(
          knob.id,
          on.length <= hidden.length
            ? { kind: 'only', types: on.join(', ') }
            : { kind: 'not', types: hidden.join(', ') }
        );
      } else {
        map.set(knob.id, {
          kind: 'names',
          rows: groups
            .sort((a, b) => (a.name ? 0 : 1) - (b.name ? 0 : 1))
            .map(group => ({ name: group.name, types: group.types.join(', ') }))
        });
      }
    }

    return map;
  });

  protected readonly outline = computed<OutlineItem[]>(() => [
    ...(this.text().usage ? [{ id: 'usage', text: 'Using it' }] : []),
    ...this.pages().map(page =>
      page.name ? { id: `page-${slugify(page.name)}`, text: page.name } : { id: 'knobs', text: 'Knobs' }
    ),
    ...(this.text().tips ? [{ id: 'tips', text: 'Tips' }] : [])
  ]);

  private readonly around = computed(() =>
    neighbours(
      MANUAL_BLOCKS,
      MANUAL_BLOCKS.findIndex(block => block.typeId === this.typeId())
    )
  );
  protected readonly prev = computed(() => this.around().prev);
  protected readonly next = computed(() => this.around().next);

  constructor() {
    const subscription = inject(ActivatedRoute).paramMap.subscribe(params => {
      this.typeId.set(params.get('typeId') ?? '');
      this.picked.set(-1);
      this.describe();
    });
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }

  /** The knob's name on the picked type, or '' when that type does not have it. */
  protected nameOf(knob: Knob): string {
    return this.picked() < 0 ? knob.name : labelOn(knob, this.picked());
  }

  /** The knob's default: on the picked type, or for every type at once. */
  protected defaultText(knob: Knob): string {
    return this.picked() < 0 ? knob.defaultText : defaultOn(knob, this.picked());
  }

  protected hasDetail(knob: Knob): boolean {
    return knob.choices.some(choice => choice.description || choice.basedOn);
  }

  protected slug(text: string): string {
    return slugify(text);
  }

  /** The template's context is untyped; this gives the knob its type back. */
  protected asKnob(knob: unknown): Knob {
    return knob as Knob;
  }

  private describe(): void {
    const block = this.block();
    const path = `manual/blocks/${this.typeId()}`;

    if (!block) {
      this.seo.set({
        title: 'Not found — Cathode manual',
        description: 'Cathode has no block at this address.',
        path,
        noindex: true
      });
      return;
    }

    const summary = plainText(block.doc?.summary ?? '');
    const description = snippet(
      summary ||
        `The ${block.name} block in Cathode: every one of its ${block.params.length} parameters, with its range and default.`
    );

    this.seo.set({
      title: `${block.name} block — Cathode manual`,
      description,
      path,
      structuredData: {
        '@context': 'https://schema.org',
        '@type': 'TechArticle',
        headline: `The ${block.name} block`,
        description,
        url: `${SITE_URL}/${path}`,
        isPartOf: { '@type': 'CreativeWork', name: 'The Cathode manual', url: `${SITE_URL}/manual` }
      }
    });
  }
}
