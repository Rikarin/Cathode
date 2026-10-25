import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { matSearchRound } from '@ng-icons/material-icons/round';
import {
  AMP_COUNT,
  AMP_GROUPS,
  BLOCK_COUNT,
  BLOCK_GROUPS,
  CAB,
  DRIVE_COUNT,
  DRIVE_GROUP_LIST,
  type Entry,
  type Group,
  WAHS
} from '../catalog/catalog';
import { SiteFooter } from '../layout/site-footer';
import { SiteHeader } from '../layout/site-header';

const matches = (query: string, ...texts: (string | undefined)[]) =>
  !query || texts.some(text => text?.toLowerCase().includes(query));

const filterGroups = (groups: readonly Group[], query: string): Group[] =>
  groups
    .map(group => ({ ...group, entries: group.entries.filter(e => matches(query, e.name, e.basedOn, e.detail)) }))
    .filter(group => group.entries.length > 0);

@Component({
  selector: 'cw-catalog-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, NgTemplateOutlet, SiteHeader, SiteFooter],
  providers: [provideIcons({ matSearchRound })],
  host: { class: 'block min-h-svh' },
  template: `
    <cw-site-header />

    <main id="main" class="overflow-x-clip">
      <section class="relative">
        <div class="glow pointer-events-none absolute inset-x-0 top-0 h-[32rem]" aria-hidden="true"></div>

        <div class="relative mx-auto max-w-3xl px-4 pt-20 text-center sm:px-6 sm:pt-28">
          <h1 class="display gradient rise text-[2.75rem] sm:text-6xl md:text-7xl" style="--i: 0">The catalogue</h1>
          <p class="rise text-foreground-muted mx-auto mt-6 max-w-2xl text-lg" style="--i: 1">
            Every amp, drive, wah, cab part and block in Cathode, and the equipment each model is based on. The lists
            are read from the engine itself, so they are what the app has today.
          </p>

          <label class="rise relative mx-auto mt-9 block max-w-xl" style="--i: 2">
            <span class="sr-only">Search the catalogue</span>
            <ng-icon
              name="matSearchRound"
              size="1.25rem"
              class="text-foreground-subtle pointer-events-none absolute start-4 top-1/2 -translate-y-1/2"
            />
            <input
              type="search"
              class="text-foreground placeholder:text-foreground-subtle h-12 w-full rounded-xl border border-white/12 bg-white/5 ps-12 pe-4 text-base outline-none focus:border-white/25"
              placeholder="Search: Plexi, Klon, fuzz, Vintage 30…"
              autocomplete="off"
              [value]="query()"
              (input)="query.set($any($event.target).value)"
            />
          </label>

          <nav class="rise mt-6 flex flex-wrap justify-center gap-2" style="--i: 3" aria-label="Catalogue sections">
            @for (link of links(); track link.id) {
              <a
                class="text-foreground-muted hover:text-foreground rounded-full border border-white/10 bg-white/5 px-3 py-1.5 text-sm hover:border-white/20"
                [href]="'catalog#' + link.id"
                >{{ link.label }} <span class="text-foreground-subtle tabular-nums">{{ link.count }}</span></a
              >
            }
          </nav>
        </div>
      </section>

      @if (empty()) {
        <p class="text-foreground-muted mx-auto max-w-xl px-4 pt-20 text-center">
          Nothing in the catalogue matches “{{ query() }}”.
        </p>
      }

      <!-- ---------------------------------------------------------------- amps -->
      @if (amps().length) {
        <section id="amps" class="mx-auto max-w-[100rem] px-4 pt-20 sm:px-6 sm:pt-24">
          <h2 class="display gradient text-start text-3xl sm:text-4xl">Amps</h2>
          <p class="text-foreground-muted mt-3 max-w-3xl">
            {{ ampCount }} models in the Amp block, each built from its schematic. Variants of one amp (a jumpered
            Bassman, the TriAxis modes) are models of their own. NAM and AIDA-X captures load in the Amp NAM block.
          </p>
          <div class="mt-8 grid gap-4 lg:grid-cols-2">
            @for (group of amps(); track group.id) {
              <ng-container *ngTemplateOutlet="table; context: { $implicit: group }" />
            }
          </div>
        </section>
      }

      <!-- ---------------------------------------------------------------- drives -->
      @if (drives().length) {
        <section id="drives" class="mx-auto max-w-[100rem] px-4 pt-20 sm:px-6 sm:pt-24">
          <h2 class="display gradient text-start text-3xl sm:text-4xl">Drives</h2>
          <p class="text-foreground-muted mt-3 max-w-3xl">
            {{ driveCount }} types in the Drive block: pedal circuits modelled component by component, and a few designs
            of Cathode's and Fractal's own. The Dynamic Distortion block adds envelope-driven clipping curves.
          </p>
          <div class="mt-8 grid gap-4">
            @for (group of drives(); track group.id) {
              <ng-container *ngTemplateOutlet="table; context: { $implicit: group, detail: true }" />
            }
          </div>
        </section>
      }

      <!-- ---------------------------------------------------------------- wahs -->
      @if (wahs().length) {
        <section id="wahs" class="mx-auto max-w-[100rem] px-4 pt-20 sm:px-6 sm:pt-24">
          <h2 class="display gradient text-start text-3xl sm:text-4xl">Wahs</h2>
          <p class="text-foreground-muted mt-3 max-w-3xl">
            Inductor and optical circuits, plus filter voicings, on an expression pedal or any modifier.
          </p>
          <div class="mt-8 max-w-4xl">
            <ng-container
              *ngTemplateOutlet="table; context: { $implicit: { id: 'wah', name: 'Wah', entries: wahs() } }"
            />
          </div>
        </section>
      }

      <!-- ---------------------------------------------------------------- cab -->
      @if (cabParts().length) {
        <section id="cabs" class="mx-auto max-w-[100rem] px-4 pt-20 sm:px-6 sm:pt-24">
          <h2 class="display gradient text-start text-3xl sm:text-4xl">Cab</h2>
          <p class="text-foreground-muted mt-3 max-w-3xl">
            The Cab block builds a response from a speaker, a microphone and a cabinet, with up to four microphones at
            once. Your own impulse responses load beside them.
          </p>
          <div class="mt-8 grid gap-4 md:grid-cols-3">
            @for (part of cabParts(); track part.name) {
              <div class="card overflow-hidden">
                <h3 class="border-b border-white/[0.06] px-5 py-3 font-semibold">
                  {{ part.name }}
                  <span class="text-foreground-subtle text-sm tabular-nums">{{ part.items.length }}</span>
                </h3>
                <ul class="divide-y divide-white/[0.06]">
                  @for (item of part.items; track item) {
                    <li class="px-5 py-2.5 text-sm">{{ item }}</li>
                  }
                </ul>
              </div>
            }
          </div>
        </section>
      }

      <!-- ---------------------------------------------------------------- blocks -->
      @if (blocks().length) {
        <section id="blocks" class="mx-auto max-w-[100rem] px-4 pt-20 sm:px-6 sm:pt-24">
          <h2 class="display gradient text-start text-3xl sm:text-4xl">Blocks</h2>
          <p class="text-foreground-muted mt-3 max-w-3xl">
            Every block you can put on the grid, by group, with the types each one offers. Every block has four
            channels.
          </p>
          <div class="mt-8 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            @for (group of blocks(); track group.category) {
              <div class="card overflow-hidden">
                <h3 class="border-b border-white/[0.06] px-5 py-3 font-semibold">{{ group.category }}</h3>
                <ul class="divide-y divide-white/[0.06]">
                  @for (block of group.blocks; track block.name) {
                    <li class="px-5 py-3">
                      <p class="text-sm font-medium">{{ block.name }}</p>
                      @if (block.see) {
                        <a
                          class="text-foreground-muted hover:text-foreground mt-1 inline-block text-xs underline-offset-2 hover:underline"
                          [href]="'catalog#' + block.see"
                          >See the list above</a
                        >
                      } @else if (block.types.length) {
                        <ul class="mt-2 flex flex-wrap gap-1">
                          @for (type of block.types; track type) {
                            <li
                              class="text-foreground-muted rounded-full border border-white/10 bg-white/5 px-2 py-0.5 text-xs"
                            >
                              {{ type }}
                            </li>
                          }
                        </ul>
                      }
                    </li>
                  }
                </ul>
              </div>
            }
          </div>
        </section>
      }

      <p class="text-foreground-subtle mx-auto max-w-3xl px-4 pt-20 pb-24 text-center text-sm leading-relaxed sm:px-6">
        Amplifier, pedal, speaker and microphone names belong to their owners. They are used only to say which equipment
        a model is based on; Cathode is not affiliated with or endorsed by any of them.
      </p>
    </main>

    <cw-site-footer />

    <!-- One group as a table: the model, what it is based on, and (for drives) how the circuit is built. -->
    <ng-template #table let-group let-detail="detail">
      <div class="card overflow-hidden">
        <h3 class="border-b border-white/[0.06] px-5 py-3 font-semibold">
          {{ group.name }} <span class="text-foreground-subtle text-sm tabular-nums">{{ group.entries.length }}</span>
        </h3>
        <div class="overflow-x-auto">
          <table class="w-full text-left text-sm">
            <thead class="text-foreground-subtle text-xs">
              <tr>
                <th scope="col" class="px-5 py-2 font-medium">In Cathode</th>
                <th scope="col" class="px-5 py-2 font-medium">Based on</th>
                @if (detail) {
                  <th scope="col" class="hidden px-5 py-2 font-medium md:table-cell">The circuit</th>
                }
              </tr>
            </thead>
            <tbody class="divide-y divide-white/[0.06]">
              @for (entry of asEntries(group.entries); track entry.name) {
                <tr>
                  <th scope="row" class="px-5 py-2.5 align-top font-medium whitespace-nowrap">{{ entry.name }}</th>
                  <td class="text-foreground-muted px-5 py-2.5 align-top">{{ entry.basedOn || '—' }}</td>
                  @if (detail) {
                    <td class="text-foreground-subtle hidden px-5 py-2.5 align-top md:table-cell">
                      {{ entry.detail }}
                    </td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </div>
      </div>
    </ng-template>
  `
})
export class CatalogPage {
  protected readonly ampCount = AMP_COUNT;
  protected readonly driveCount = DRIVE_COUNT;

  protected readonly query = signal('');
  private readonly needle = computed(() => this.query().trim().toLowerCase());

  protected readonly amps = computed(() => filterGroups(AMP_GROUPS, this.needle()));
  protected readonly drives = computed(() => filterGroups(DRIVE_GROUP_LIST, this.needle()));
  protected readonly wahs = computed(() => WAHS.filter(wah => matches(this.needle(), wah.name, wah.basedOn)));

  protected readonly cabParts = computed(() =>
    [
      { name: 'Speakers', items: CAB.speakers },
      { name: 'Microphones', items: CAB.microphones },
      { name: 'Cabinets', items: CAB.cabinets }
    ]
      .map(part => ({ ...part, items: part.items.filter(item => matches(this.needle(), item, part.name)) }))
      .filter(part => part.items.length > 0)
  );

  protected readonly blocks = computed(() =>
    BLOCK_GROUPS.map(group => ({
      ...group,
      blocks: group.blocks.filter(block => matches(this.needle(), block.name, group.category, ...block.types))
    })).filter(group => group.blocks.length > 0)
  );

  protected readonly links = computed(() =>
    [
      { id: 'amps', label: 'Amps', count: count(this.amps()) },
      { id: 'drives', label: 'Drives', count: count(this.drives()) },
      { id: 'wahs', label: 'Wahs', count: this.wahs().length },
      { id: 'cabs', label: 'Cab', count: this.cabParts().reduce((sum, part) => sum + part.items.length, 0) },
      {
        id: 'blocks',
        label: 'Blocks',
        count: this.needle() ? this.blocks().reduce((sum, g) => sum + g.blocks.length, 0) : BLOCK_COUNT
      }
    ].filter(link => link.count > 0)
  );

  protected readonly empty = computed(() => this.links().length === 0);

  /** The template's context is untyped; this gives the rows their type back. */
  protected asEntries(entries: unknown): readonly Entry[] {
    return entries as readonly Entry[];
  }
}

const count = (groups: readonly Group[]) => groups.reduce((sum, group) => sum + group.entries.length, 0);
