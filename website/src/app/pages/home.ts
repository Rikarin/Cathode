import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  matArrowForwardRound,
  matCheckRound,
  matChevronRightRound,
  matMicRound,
  matPhoneIphoneRound,
  matRouterRound,
  matSpeakerRound,
  matTerminalRound,
  matWavesRound
} from '@ng-icons/material-icons/round';
import { AMP_COUNT, BLOCK_COUNT } from '../catalog/catalog';
import {
  AMP_EXPERT,
  AMP_FAMILIES,
  BLOCK_GROUPS,
  CAB_FEATURES,
  CAB_FIGURES,
  FIGURES,
  GETTING_STARTED,
  GRID_BLOCKS,
  GRID_FEATURES,
  GRID_WIRES,
  HEADLESS_INSTALL,
  IO_FEATURES,
  LIBRARY_ITEMS,
  LIVE_FEATURES,
  MODIFIER_SOURCES,
  PLATFORMS,
  SCENE_BLOCKS,
  SCENES
} from '../content';
import { SiteFooter } from '../layout/site-footer';
import { SiteHeader } from '../layout/site-header';
import { SHOTS } from '../screenshots';
import { Seo } from '../seo';
import { Screenshot } from '../shared/screenshot';
import { HEADLESS_GUIDE, LICENCE, RELEASES, REPOSITORY, SITE_NAME, SITE_URL } from '../site';

/** The routing diagram's cell size and spacing, in SVG units. */
const CELL = { w: 60, h: 40, x: 72, y: 54 };
const GRID = { cols: 14, rows: 6 };

const STATUS = {
  ready: { label: 'Plays sound', dot: 'bg-emerald-400' },
  early: { label: 'Plays sound, early', dot: 'bg-amber-400' }
};

@Component({
  selector: 'cw-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgIcon, SiteHeader, SiteFooter, Screenshot],
  providers: [
    provideIcons({
      matArrowForwardRound,
      matCheckRound,
      matChevronRightRound,
      matMicRound,
      matPhoneIphoneRound,
      matRouterRound,
      matSpeakerRound,
      matTerminalRound,
      matWavesRound
    })
  ],
  host: { class: 'block min-h-svh' },
  template: `
    <cw-site-header />

    <main id="main" class="overflow-x-clip">
      <!-- ---------------------------------------------------------------- hero -->
      <section class="relative">
        <div class="glow pointer-events-none absolute inset-x-0 top-0 h-[44rem]" aria-hidden="true"></div>

        <div class="relative mx-auto max-w-4xl px-4 pt-20 pb-14 text-center sm:px-6 sm:pt-28">
          <a [href]="releases" class="pill rise" style="--i: 0" rel="noreferrer noopener" target="_blank">
            Early access · free and open source
            <ng-icon name="matChevronRightRound" size="1rem" class="text-foreground-muted" />
          </a>

          <h1 class="display gradient rise mt-7 text-[2.75rem] sm:text-6xl md:text-7xl" style="--i: 1">
            Your whole rig, on your computer.
          </h1>

          <!-- The icon's waveform, drawn once under the headline. -->
          <svg
            class="trace rise mx-auto mt-6 h-10 w-full max-w-md"
            style="--i: 2"
            viewBox="0 0 400 40"
            fill="none"
            aria-hidden="true"
          >
            <path
              d="M4 22 H150 L162 12 L172 28 L184 4 L198 36 L210 10 L222 26 L232 18 L242 22 H396"
              pathLength="1"
              stroke="url(#trace)"
              stroke-width="3"
              stroke-linecap="round"
              stroke-linejoin="round"
            />
            <defs>
              <linearGradient id="trace" x1="0" x2="1">
                <stop offset="0" stop-color="#1ab3ff" stop-opacity="0" />
                <stop offset="0.3" stop-color="#1ab3ff" />
                <stop offset="0.5" stop-color="#5ff0ff" />
                <stop offset="0.7" stop-color="#1ab3ff" />
                <stop offset="1" stop-color="#1ab3ff" stop-opacity="0" />
              </linearGradient>
            </defs>
          </svg>

          <p class="rise text-foreground-muted mx-auto mt-6 max-w-3xl text-lg sm:text-xl" style="--i: 3">
            Cathode is a guitar processor in the spirit of the Axe-Fx III. Plug into your audio interface and build a
            rig from circuit-modelled amps, cabinets, drives and effects on a grid, with scenes, channels and modifiers
            for playing live.
          </p>

          <div class="rise mt-9 flex flex-col items-center justify-center gap-3 sm:flex-row" style="--i: 4">
            <a href="#download" class="cta">
              Download Cathode
              <ng-icon name="matArrowForwardRound" size="1.25rem" />
            </a>
            <a [href]="repository" class="ghost" rel="noreferrer noopener" target="_blank">View the source</a>
          </div>

          <p class="rise text-foreground-subtle mt-7 text-sm" style="--i: 5">
            For macOS, Windows and Linux, and a headless rig for a Raspberry Pi 5. GPL-3.0, no account, no paid tier.
          </p>
        </div>

        <div class="rise relative mx-auto max-w-6xl px-4 sm:px-6" style="--i: 6">
          <cw-screenshot [shot]="shots.grid" [eager]="true" />
        </div>

        <dl class="relative mx-auto mt-4 grid max-w-6xl grid-cols-2 gap-4 px-4 sm:px-6 lg:grid-cols-4">
          @for (figure of figures; track figure.label) {
            <div class="card p-5">
              <dt class="text-foreground-muted text-sm">{{ figure.label }}</dt>
              <dd class="mt-1 text-3xl font-semibold tracking-tight tabular-nums">{{ figure.value }}</dd>
            </div>
          }
        </dl>
      </section>

      <!-- ---------------------------------------------------------------- amps -->
      <section id="amps" class="mx-auto max-w-[100rem] px-4 pt-24 sm:px-6 sm:pt-32">
        <div class="mx-auto max-w-3xl text-center">
          <h2 class="display gradient text-4xl sm:text-5xl md:text-6xl">Amps built from their schematics</h2>
          <p class="text-foreground-muted mx-auto mt-5 max-w-2xl text-lg">
            Each of the {{ ampCount }} models is the circuit, stage by stage: the preamp, the tone stack, the power amp,
            the output transformer and the load of the speaker. The front panel is the amp's own, so the knobs do what
            they did.
          </p>
        </div>

        <div class="mt-12 grid gap-4">
          <cw-screenshot [shot]="shots.amp" />

          <div class="grid gap-4 md:grid-cols-2">
            <div class="card p-6 sm:p-8">
              <h3 class="text-xl font-semibold">Behind the front panel</h3>
              <p class="text-foreground-muted mt-2 text-sm leading-relaxed">
                The expert pages open up what the circuit is made of, for when the panel is not enough.
              </p>
              <ul class="mt-5 flex flex-col gap-2.5">
                @for (item of ampExpert; track item) {
                  <li class="flex items-start gap-2.5 text-sm">
                    <span class="bg-signal/15 text-signal mt-0.5 grid size-5 shrink-0 place-items-center rounded-full">
                      <ng-icon name="matCheckRound" size="0.8rem" />
                    </span>
                    {{ item }}
                  </li>
                }
              </ul>
            </div>

            <div class="card p-6 sm:p-8">
              <h3 class="text-xl font-semibold">Neural captures too</h3>
              <p class="text-foreground-muted mt-2 text-sm leading-relaxed">
                Load NAM (<code class="font-mono text-xs">.nam</code>) and AIDA-X models in the Amp NAM block, and keep
                them in the Library beside your presets.
              </p>
            </div>
          </div>
        </div>

        <div class="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-5">
          @for (family of ampFamilies; track family.name) {
            <article class="card flex flex-col p-5">
              <h3 class="font-semibold">{{ family.name }}</h3>
              <p class="text-foreground-muted mt-2 text-sm leading-relaxed">{{ family.description }}</p>
              <div class="mt-auto pt-5">
                <p class="text-foreground-subtle text-xs">Based on</p>
                <ul class="mt-2 flex flex-wrap gap-1.5">
                  @for (amp of family.based; track amp) {
                    <li class="rounded-full border border-white/10 bg-white/5 px-2.5 py-1 text-xs">{{ amp }}</li>
                  }
                </ul>
              </div>
            </article>
          }
        </div>

        <p class="mt-6 text-center">
          <a routerLink="/catalog" fragment="amps" class="pill">
            Every amp and what it is based on
            <ng-icon name="matChevronRightRound" size="1rem" class="text-foreground-muted" />
          </a>
        </p>
      </section>

      <!-- ---------------------------------------------------------------- cabs -->
      <section id="cabs" class="mx-auto max-w-[100rem] px-4 pt-24 sm:px-6 sm:pt-32">
        <div class="mx-auto max-w-3xl text-center">
          <h2 class="display gradient-warm text-4xl sm:text-5xl md:text-6xl">Move the mic, not the cab</h2>
          <p class="text-foreground-muted mx-auto mt-5 max-w-2xl text-lg">
            The Cab block builds the response from a speaker, a microphone and a cabinet, and where you put the mic. Or
            load the impulse responses you already have.
          </p>
        </div>

        <div class="mt-12 grid gap-4">
          <cw-screenshot class="mx-auto w-full max-w-6xl" [shot]="shots.cab" />

          <div class="grid gap-4">
            <dl class="grid grid-cols-3 gap-4">
              @for (figure of cabFigures; track figure.label) {
                <div class="card p-5 text-center">
                  <dd class="text-3xl font-semibold tracking-tight tabular-nums">{{ figure.value }}</dd>
                  <dt class="text-foreground-muted mt-1 text-sm">{{ figure.label }}</dt>
                </div>
              }
            </dl>

            <div class="grid gap-4 md:grid-cols-3">
              @for (feature of cabFeatures; track feature.title; let i = $index) {
                <div class="card flex gap-4 p-6">
                  <ng-icon
                    [name]="i === 0 ? 'matMicRound' : i === 1 ? 'matWavesRound' : 'matSpeakerRound'"
                    size="1.25rem"
                    class="text-signal mt-0.5 shrink-0"
                  />
                  <div>
                    <h3 class="font-semibold">{{ feature.title }}</h3>
                    <p class="text-foreground-muted mt-1.5 text-sm leading-relaxed">{{ feature.body }}</p>
                  </div>
                </div>
              }
            </div>
          </div>
        </div>
      </section>

      <!-- ---------------------------------------------------------------- effects -->
      <section id="effects" class="mx-auto max-w-[100rem] px-4 pt-24 sm:px-6 sm:pt-32">
        <div class="mx-auto max-w-3xl text-center">
          <h2 class="display gradient text-4xl sm:text-5xl md:text-6xl">Every effect after the amp, and before it</h2>
          <p class="text-foreground-muted mx-auto mt-5 max-w-2xl text-lg">
            {{ blockCount }} block types, each with four channels and its own pages of controls, and a Block Library of
            factory presets to start from.
          </p>
        </div>

        <div class="mt-12 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          @for (group of blockGroups; track group.name) {
            <article class="card flex flex-col p-5">
              <div class="flex items-baseline justify-between gap-3">
                <h3 class="font-semibold">{{ group.name }}</h3>
                <span class="text-foreground-subtle text-sm tabular-nums">{{ group.blocks.length }}</span>
              </div>
              <p class="text-foreground-muted mt-2 text-sm leading-relaxed">{{ group.blocks.join(', ') }}</p>
              <p class="text-foreground-subtle mt-auto pt-4 text-sm leading-relaxed">{{ group.note }}</p>
            </article>
          }
        </div>

        <cw-screenshot class="mx-auto mt-4 max-w-6xl" [shot]="shots.effects" />

        <p class="mt-6 text-center">
          <a routerLink="/catalog" fragment="drives" class="pill">
            Every drive, wah and block in the catalogue
            <ng-icon name="matChevronRightRound" size="1rem" class="text-foreground-muted" />
          </a>
        </p>
      </section>

      <!-- ---------------------------------------------------------------- grid -->
      <section id="grid" class="relative mx-auto max-w-[100rem] px-4 pt-24 sm:px-6 sm:pt-32">
        <div class="dots pointer-events-none absolute inset-0 -z-10" aria-hidden="true"></div>

        <div class="mx-auto max-w-3xl text-center">
          <h2 class="display gradient text-4xl sm:text-5xl md:text-6xl">A grid like the hardware's</h2>
          <p class="text-foreground-muted mx-auto mt-5 max-w-2xl text-lg">
            Six rows of fourteen cells. Put a block in any of them and wire it to its neighbours: one chain, or a dozen
            paths that split and meet again.
          </p>
        </div>

        <figure class="card mt-12 overflow-x-auto p-4 sm:p-8">
          <svg
            class="mx-auto block w-full min-w-[40rem]"
            [attr.viewBox]="'-4 -4 ' + (gridSize.width + 8) + ' ' + (gridSize.height + 8)"
            role="img"
            aria-label="A preset on the grid: input, compressor, drive, amp, cab and EQ in a row, then a split into chorus and delay above, pitch and reverb below, and a dry path, mixed back together before the volume block and the output."
          >
            @for (cell of gridCells; track cell.key) {
              <rect
                [attr.x]="cell.x"
                [attr.y]="cell.y"
                [attr.width]="cellSize.w"
                [attr.height]="cellSize.h"
                rx="8"
                fill="rgb(255 255 255 / 0.03)"
                stroke="rgb(255 255 255 / 0.07)"
              />
            }
            @for (wire of gridWires; track wire.key) {
              <path
                [attr.d]="wire.d"
                fill="none"
                stroke="oklch(80% 0.14 220 / 0.7)"
                stroke-width="2"
                stroke-linejoin="round"
              />
            }
            @for (block of gridBlocks; track block.id) {
              <g>
                <rect
                  [attr.x]="block.x"
                  [attr.y]="block.y"
                  [attr.width]="cellSize.w"
                  [attr.height]="cellSize.h"
                  rx="8"
                  fill="#0b1118"
                  stroke="oklch(80% 0.14 220 / 0.8)"
                />
                <text
                  [attr.x]="block.x + cellSize.w / 2"
                  [attr.y]="block.y + cellSize.h / 2 + 4.5"
                  text-anchor="middle"
                  fill="#f5f5f5"
                  font-size="13"
                  font-weight="600"
                  letter-spacing="0.04em"
                >
                  {{ block.label }}
                </text>
              </g>
            }
          </svg>
        </figure>

        <div class="mt-4 grid gap-4 md:grid-cols-3">
          @for (feature of gridFeatures; track feature.title) {
            <div class="card p-6">
              <h3 class="font-semibold">{{ feature.title }}</h3>
              <p class="text-foreground-muted mt-2 text-sm leading-relaxed">{{ feature.body }}</p>
            </div>
          }
        </div>
      </section>

      <!-- ---------------------------------------------------------------- live -->
      <section id="live" class="mx-auto max-w-[100rem] px-4 pt-24 sm:px-6 sm:pt-32">
        <div class="mx-auto max-w-3xl text-center">
          <h2 class="display gradient text-4xl sm:text-5xl md:text-6xl">Built for playing live</h2>
          <p class="text-foreground-muted mx-auto mt-5 max-w-2xl text-lg">
            A preset is a whole song. Scenes change which blocks are on and which channel each one plays, in one press,
            without a gap in the sound.
          </p>
        </div>

        <div class="mt-12 grid gap-4 lg:grid-cols-2">
          <!-- A working scene switcher: the eight scenes of one preset and what each does to four of its blocks. -->
          <div class="card p-6 sm:p-8">
            <div class="flex items-baseline justify-between gap-3">
              <h3 class="text-xl font-semibold">Eight scenes, four channels</h3>
              <span class="text-foreground-subtle shrink-0 text-sm">Try it</span>
            </div>
            <p class="text-foreground-muted mt-2 text-sm leading-relaxed">
              Every block has channels A to D, each with its own settings. A scene picks one for each block and switches
              blocks on or off.
            </p>

            <div class="mt-6 grid grid-cols-4 gap-2 sm:grid-cols-8" role="group" aria-label="Scenes">
              @for (item of scenes; track item.name; let i = $index) {
                <button
                  type="button"
                  class="flex h-14 flex-col items-center justify-center rounded-xl border text-sm transition-colors"
                  [class]="
                    scene() === i
                      ? 'border-signal/70 bg-signal/15 text-foreground'
                      : 'text-foreground-muted hover:text-foreground border-white/10 bg-white/5 hover:border-white/20'
                  "
                  [attr.aria-pressed]="scene() === i"
                  (click)="scene.set(i)"
                >
                  <span class="font-semibold tabular-nums">{{ i + 1 }}</span>
                  <span class="text-[0.6875rem]">{{ item.name }}</span>
                </button>
              }
            </div>

            <ul class="mt-4 grid grid-cols-4 gap-2" aria-live="polite">
              @for (block of sceneState(); track block.name) {
                <li
                  class="flex flex-col items-center gap-1 rounded-xl border py-3 transition-colors"
                  [class]="
                    block.channel ? 'border-signal/50 bg-black/40' : 'border-white/[0.07] bg-black/40 opacity-50'
                  "
                >
                  <span class="text-sm font-semibold">{{ block.name }}</span>
                  <span class="text-foreground-muted text-xs">
                    {{ block.channel ? 'Channel ' + block.channel : 'Bypassed' }}
                  </span>
                </li>
              }
            </ul>
          </div>

          <div class="card flex flex-col p-6 sm:p-8">
            <h3 class="text-xl font-semibold">Modifiers on any knob</h3>
            <p class="text-foreground-muted mt-2 text-sm leading-relaxed">
              Right-click a control and drive it from a source: a filter that breathes with an LFO, a wah on your
              picking, a delay mix on an expression pedal. Set its range, attack and release, its curve, and the
              channels it applies on.
            </p>
            <svg
              class="mt-6 h-24 w-full"
              viewBox="0 0 400 80"
              preserveAspectRatio="none"
              fill="none"
              aria-hidden="true"
            >
              <path d="M0 40 H400" stroke="rgb(255 255 255 / 0.08)" />
              <path
                d="M0 40 C25 5 50 5 75 40 S125 75 150 40 S200 5 225 40 S275 75 300 40 S350 5 375 40 S400 60 400 60"
                stroke="oklch(80% 0.14 220)"
                stroke-width="2.5"
                stroke-linecap="round"
                class="trace"
              />
            </svg>
            <ul class="mt-auto flex flex-wrap gap-1.5 pt-6">
              @for (source of modifierSources; track source) {
                <li class="rounded-full border border-white/10 bg-white/5 px-3 py-1 text-sm">{{ source }}</li>
              }
            </ul>
          </div>

          <cw-screenshot [shot]="shots.perform" />
          <cw-screenshot [shot]="shots.modifiers" />

          <div class="grid gap-4 md:grid-cols-3 lg:col-span-2">
            @for (feature of liveFeatures; track feature.title) {
              <div class="card p-6">
                <h3 class="font-semibold">{{ feature.title }}</h3>
                <p class="text-foreground-muted mt-2 text-sm leading-relaxed">{{ feature.body }}</p>
              </div>
            }
          </div>
        </div>
      </section>

      <!-- ---------------------------------------------------------------- library and I/O -->
      <section id="library" class="mx-auto max-w-[100rem] px-4 pt-24 sm:px-6 sm:pt-32">
        <div class="mx-auto max-w-3xl text-center">
          <h2 class="display gradient text-4xl sm:text-5xl md:text-6xl">Every tone in one Library</h2>
          <p class="text-foreground-muted mx-auto mt-5 max-w-2xl text-lg">
            Presets, the blocks inside them, your captures and your impulse responses, kept together and arranged for
            the gig in front of you.
          </p>
        </div>

        <div class="mt-12 grid gap-4 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
          <cw-screenshot [shot]="shots.library" />

          <div class="card p-6 sm:p-8">
            <h3 class="text-xl font-semibold">What it keeps</h3>
            <ul class="mt-5 flex flex-col gap-3">
              @for (item of libraryItems; track item) {
                <li class="flex items-start gap-2.5 text-sm">
                  <span class="bg-signal/15 text-signal mt-0.5 grid size-5 shrink-0 place-items-center rounded-full">
                    <ng-icon name="matCheckRound" size="0.8rem" />
                  </span>
                  {{ item }}
                </li>
              }
            </ul>
          </div>
        </div>
      </section>

      <section id="io" class="mx-auto max-w-[100rem] px-4 pt-24 sm:px-6 sm:pt-32">
        <div class="mx-auto max-w-3xl text-center">
          <h2 class="display gradient text-4xl sm:text-5xl md:text-6xl">Ins and outs for a real rig</h2>
          <p class="text-foreground-muted mx-auto mt-5 max-w-2xl text-lg">
            Use every channel your interface has. Audio setup shows where the latency goes, from the interface to the
            buffer and the preset, and calibrates the input so the amp and drive models read your guitar in volts. 64
            samples at 48 kHz is the target.
          </p>
        </div>

        <div class="mt-12 grid gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]">
          <div class="grid gap-4 sm:grid-cols-2">
            @for (feature of ioFeatures; track feature.title) {
              <div class="card flex flex-col p-6">
                <span class="text-signal text-3xl font-semibold tracking-tight">{{ feature.value }}</span>
                <h3 class="mt-2 font-semibold">{{ feature.title }}</h3>
                <p class="text-foreground-muted mt-1.5 text-sm leading-relaxed">{{ feature.body }}</p>
              </div>
            }
          </div>

          <cw-screenshot [shot]="shots.setup" />
        </div>
      </section>

      <!-- ---------------------------------------------------------------- headless rig -->
      <section id="rig" class="mx-auto max-w-[100rem] px-4 pt-24 sm:px-6 sm:pt-32">
        <div class="mx-auto max-w-3xl text-center">
          <h2 class="display gradient text-4xl sm:text-5xl md:text-6xl">Leave the laptop at home</h2>
          <p class="text-foreground-muted mx-auto mt-5 max-w-2xl text-lg">
            Run Cathode on a Raspberry Pi 5 or a mini PC in your rig, with your audio interface and no display, and play
            and edit from a phone, a tablet or any laptop's browser.
          </p>
        </div>

        <div class="mt-12 grid gap-4 lg:grid-cols-[minmax(0,3fr)_minmax(0,1fr)]">
          <div class="grid gap-4">
            <div class="grid gap-4 md:grid-cols-3">
              <div class="card p-6">
                <ng-icon name="matRouterRound" size="1.25rem" class="text-signal" />
                <h3 class="mt-3 font-semibold">Starts with the power</h3>
                <p class="text-foreground-muted mt-1.5 text-sm leading-relaxed">
                  The installer starts Cathode at every boot, and prints the address to open and an access token.
                </p>
              </div>
              <div class="card p-6">
                <ng-icon name="matPhoneIphoneRound" size="1.25rem" class="text-signal" />
                <h3 class="mt-3 font-semibold">The editor in a browser</h3>
                <p class="text-foreground-muted mt-1.5 text-sm leading-relaxed">
                  The same editor as the desktop app, served to your network. Or connect the desktop app to the rig.
                </p>
              </div>
              <div class="card p-6">
                <ng-icon name="matTerminalRound" size="1.25rem" class="text-signal" />
                <h3 class="mt-3 font-semibold">x64 and ARM64</h3>
                <p class="text-foreground-muted mt-1.5 text-sm leading-relaxed">
                  One package for Linux on either, with systemd services for the engine and the server.
                </p>
              </div>
            </div>

            <div class="card overflow-hidden">
              <div class="flex items-center gap-2 border-b border-white/[0.06] px-5 py-3 text-sm">
                <ng-icon name="matTerminalRound" size="1rem" class="text-foreground-muted" />
                Install on the rig
              </div>
              <pre
                class="overflow-x-auto bg-black/40 px-5 py-4 font-mono text-sm leading-relaxed"
              ><code>{{ headlessInstall }}</code></pre>
              <div class="border-t border-white/[0.06] px-5 py-3 text-sm">
                <a
                  class="text-foreground-muted hover:text-foreground inline-flex items-center gap-1"
                  [href]="headlessGuide"
                  rel="noreferrer noopener"
                  target="_blank"
                >
                  Choosing the interface, updates and logs
                  <ng-icon name="matChevronRightRound" size="1rem" />
                </a>
              </div>
            </div>
          </div>

          <cw-screenshot class="mx-auto w-full max-w-xs lg:max-w-none" [shot]="shots.remote" />
        </div>
      </section>

      <!-- ---------------------------------------------------------------- download -->
      <section id="download" class="mx-auto max-w-[100rem] px-4 pt-24 sm:px-6 sm:pt-32">
        <div class="mx-auto max-w-3xl text-center">
          <h2 class="display gradient text-4xl sm:text-5xl md:text-6xl">Download</h2>
          <p class="text-foreground-muted mx-auto mt-5 max-w-2xl text-lg">
            Cathode is early: it has no stable release yet, so expect rough edges and changes to the preset format.
            Every build is on the Releases page, with SHA256SUMS to check it against.
          </p>
        </div>

        <div class="mt-12 grid gap-4 md:grid-cols-3">
          @for (platform of platforms; track platform.name) {
            <article class="card flex flex-col p-6 sm:p-8">
              <div class="flex items-center justify-between gap-3">
                <h3 class="text-xl font-semibold">{{ platform.name }}</h3>
                <span class="text-foreground-muted inline-flex items-center gap-2 text-sm">
                  <span class="size-2 rounded-full" [class]="status[platform.status].dot"></span>
                  {{ status[platform.status].label }}
                </span>
              </div>
              <p class="text-foreground-muted mt-1 text-sm">{{ platform.for }} · {{ platform.audio }}</p>
              <code class="mt-5 block truncate rounded-xl bg-black/40 px-3 py-2.5 font-mono text-xs">{{
                platform.package
              }}</code>
              <p class="text-foreground-subtle mt-4 text-sm leading-relaxed">{{ platform.note }}</p>
              <div class="mt-auto pt-6">
                <a
                  class="text-foreground hover:border-signal/60 flex h-11 items-center justify-center gap-2 rounded-xl border border-white/12 bg-white/5 text-sm font-semibold transition-colors"
                  [href]="releases"
                  rel="noreferrer noopener"
                  target="_blank"
                >
                  Get it from Releases
                  <ng-icon name="matArrowForwardRound" size="1rem" />
                </a>
              </div>
            </article>
          }
        </div>

        <div class="card mt-4 p-6 sm:p-8">
          <h3 class="text-xl font-semibold">Your first sound</h3>
          <ol class="mt-5 grid gap-4 md:grid-cols-5">
            @for (step of gettingStarted; track $index) {
              <li class="flex gap-3 text-sm leading-relaxed md:flex-col">
                <span
                  class="text-signal grid size-7 shrink-0 place-items-center rounded-full border border-white/12 bg-white/5 text-xs font-semibold tabular-nums"
                  >{{ $index + 1 }}</span
                >
                <span class="text-foreground-muted">{{ step }}</span>
              </li>
            }
          </ol>
          <p class="text-foreground-muted mt-6 text-sm">
            The manual takes it from there: setting up your audio, building a rig, and what every knob does.
            <a routerLink="/manual/getting-started" class="text-foreground underline-offset-2 hover:underline"
              >Read Getting started</a
            >
            or <a routerLink="/manual" class="text-foreground underline-offset-2 hover:underline">browse the manual</a>.
          </p>
        </div>
      </section>

      <!-- ---------------------------------------------------------------- closer -->
      <section class="mx-auto max-w-[100rem] px-4 py-24 sm:px-6 sm:py-32">
        <div class="card relative overflow-hidden px-6 py-16 text-center sm:px-12 sm:py-24">
          <div class="glow pointer-events-none absolute inset-0" aria-hidden="true"></div>
          <img src="logo.png" alt="" width="72" height="72" class="relative mx-auto size-18 rounded-2xl" />
          <h2 class="display gradient relative mt-8 text-4xl sm:text-5xl md:text-6xl">Plug in and play</h2>
          <p class="text-foreground-muted relative mx-auto mt-5 max-w-2xl text-lg">
            Free and open source, under the GPL. Bring an audio interface and a guitar; Cathode brings the rest.
          </p>
          <div class="relative mt-9 flex flex-col items-center justify-center gap-3 sm:flex-row">
            <a href="#download" class="cta">
              Download Cathode
              <ng-icon name="matArrowForwardRound" size="1.25rem" />
            </a>
            <a [href]="repository" class="ghost" rel="noreferrer noopener" target="_blank">Star on GitHub</a>
          </div>
        </div>
      </section>
    </main>

    <cw-site-footer />
  `
})
export class Home {
  constructor() {
    inject(Seo).set({
      title: 'Cathode — a free guitar amp and effects processor',
      description: `Free, open-source guitar amp and effects software for macOS, Windows and Linux: ${AMP_COUNT} amps modelled from their schematics, cabs, pedals, effects and NAM.`,
      path: '',
      structuredData: {
        '@context': 'https://schema.org',
        '@graph': [
          { '@type': 'WebSite', name: SITE_NAME, url: `${SITE_URL}/` },
          {
            '@type': 'SoftwareApplication',
            name: SITE_NAME,
            description:
              'A guitar processor for your computer: circuit-modelled amps, cabinets, drives and effects on a grid, with scenes, channels and modifiers for playing live.',
            url: `${SITE_URL}/`,
            applicationCategory: 'MultimediaApplication',
            operatingSystem: 'macOS, Windows, Linux',
            image: `${SITE_URL}/social.jpg`,
            screenshot: `${SITE_URL}/screenshots/grid.webp`,
            downloadUrl: RELEASES,
            license: LICENCE,
            isAccessibleForFree: true,
            offers: { '@type': 'Offer', price: '0', priceCurrency: 'USD' }
          }
        ]
      }
    });
  }

  protected readonly repository = REPOSITORY;
  protected readonly releases = RELEASES;
  protected readonly headlessGuide = HEADLESS_GUIDE;
  protected readonly shots = SHOTS;
  protected readonly ampCount = AMP_COUNT;
  protected readonly blockCount = BLOCK_COUNT;

  protected readonly figures = FIGURES;
  protected readonly ampFamilies = AMP_FAMILIES;
  protected readonly ampExpert = AMP_EXPERT;
  protected readonly cabFigures = CAB_FIGURES;
  protected readonly cabFeatures = CAB_FEATURES;
  protected readonly blockGroups = BLOCK_GROUPS;
  protected readonly gridFeatures = GRID_FEATURES;
  protected readonly liveFeatures = LIVE_FEATURES;
  protected readonly modifierSources = MODIFIER_SOURCES;
  protected readonly libraryItems = LIBRARY_ITEMS;
  protected readonly ioFeatures = IO_FEATURES;
  protected readonly platforms = PLATFORMS;
  protected readonly status = STATUS;
  protected readonly gettingStarted = GETTING_STARTED;
  protected readonly headlessInstall = HEADLESS_INSTALL;

  // ------------------------------------------------------------------ the routing diagram
  protected readonly cellSize = CELL;
  protected readonly gridSize = {
    width: (GRID.cols - 1) * CELL.x + CELL.w,
    height: (GRID.rows - 1) * CELL.y + CELL.h
  };

  protected readonly gridCells = Array.from({ length: GRID.cols * GRID.rows }, (_, i) => ({
    key: i,
    x: (i % GRID.cols) * CELL.x,
    y: Math.floor(i / GRID.cols) * CELL.y
  }));

  protected readonly gridBlocks = GRID_BLOCKS.map(block => ({
    ...block,
    x: block.col * CELL.x,
    y: block.row * CELL.y
  }));

  /** Each wire leaves the right edge of one block and enters the left edge of the next, turning halfway between. */
  protected readonly gridWires = GRID_WIRES.map(([from, to]) => {
    const a = this.gridBlocks.find(block => block.id === from)!;
    const b = this.gridBlocks.find(block => block.id === to)!;
    const x1 = a.x + CELL.w;
    const y1 = a.y + CELL.h / 2;
    const x2 = b.x;
    const y2 = b.y + CELL.h / 2;
    // A wire that turns does so in the gap just before its target, so wires sharing a source share their run.
    const turn = y1 === y2 ? x2 : x2 - (CELL.x - CELL.w) / 2;

    return { key: `${from}-${to}`, d: `M${x1} ${y1} H${turn} V${y2} H${x2}` };
  });

  // ------------------------------------------------------------------ the scene demo
  protected readonly scenes = SCENES;
  protected readonly scene = signal(4);
  protected readonly sceneState = computed(() =>
    SCENE_BLOCKS.map((name, i) => ({ name, channel: this.scenes[this.scene()].blocks[i] }))
  );
}
