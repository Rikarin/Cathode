import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import type { AssetInfo, ParameterInfo, PresetBlock } from '@cathode/api-client';
import { type ConeMove, CthSpeakerCone, MicColors } from '@cathode/audio-ui';
import { XuiButtonImports } from '@xui/button';
import { XuiCalloutImports } from '@xui/callout';
import { XuiEChartImports } from '@xui/echarts';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import type { EChartsOption } from 'echarts';
import { decodeWav, impulseResponseDb, logFrequencies } from '../../dsp/response';
import { CatalogStore } from '../../state/catalog.store';
import { LibraryStore } from '../../state/library.store';
import { LiveStore } from '../../state/live.store';
import { ParamControl } from '../param-control';
import { AssetPicker } from './asset-picker';
import {
  CabSlots,
  type SlotKey,
  Source,
  coneMics,
  cutsDb,
  isAudible,
  isMuted,
  relativeLevelsDb,
  slotAsset,
  slotParamId,
  sourceOf,
  unmuteOnEdit
} from './cab';
import { CabAlign } from './cab-align';

const Choices: readonly SlotKey[] = ['cab', 'speaker', 'mic'];
const Placement: readonly SlotKey[] = ['position', 'distance', 'angle'];
const PackPlacement: readonly SlotKey[] = ['position', 'distance'];
const Filters: readonly SlotKey[] = ['lowcut', 'lowslope', 'highcut', 'highslope', 'length'];
const Mixer: readonly SlotKey[] = ['level', 'pan', 'delay', 'mute', 'solo', 'invert'];
const Frequencies = logFrequencies(160);

/** Decoded responses are cached per asset: an IR never changes under its hash. */
const responses = new Map<string, Promise<number[]>>();

/**
 * The Cab's four slots, as the Axe-Fx III mixes Legacy IRs and DynaCabs: pick Mic 1-4, then its Source (an IR file, a
 * modelled DynaCab cab, speaker and mic, or the user's multi-position IR pack), its placement on the speaker cone and
 * its mixer controls. DynaCab and IR Pack slots are dragged across the cone (cap to edge) and away from the grille; IR
 * and IR Pack slots take a file. Each slot has its own cuts and IR length, and a relative level (the slots' levels set
 * the blend). Editing a muted slot unmutes it, so the edit is heard. The plot shows the IR files' responses with their
 * levels and the block's cuts; the global controls (input, air, mic preamp and its tone, master cuts, proximity and its
 * frequency, the room page, auto delay, smoothing, level) stay on the rack below.
 */
@Component({
  selector: 'cth-cab-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AssetPicker,
    CabAlign,
    CthSpeakerCone,
    ParamControl,
    XuiButtonImports,
    XuiCalloutImports,
    XuiEChartImports,
    XuiSegmentedControlImports
  ],
  template: `
    <div class="flex flex-wrap items-start gap-6">
      <div class="flex w-[480px] max-w-full shrink-0 flex-col gap-3">
        <div
          class="bg-surface-sunken border-border rounded-xl border p-3 shadow-[inset_0_1px_3px_rgb(0_0_0/0.35)]"
          data-testid="cab-cone"
        >
          <cth-speaker-cone
            label="Mic placement"
            [mics]="mics()"
            [selected]="selected()"
            (moved)="move($event)"
            (selectedChange)="selected.set($event)"
          />
        </div>
        <xui-segmented-control
          size="sm"
          aria-label="Cab view"
          data-testid="cab-view"
          [options]="viewOptions"
          [value]="view()"
          (valueChange)="view.set($event ?? 'response')"
        />
        @if (view() === 'align') {
          <cth-cab-align [block]="block()" [selected]="selected()" (selectedChange)="selected.set($event)" />
        } @else if (plotted()) {
          <xui-echart
            class="h-48 w-full"
            aria-label="IR frequency response"
            [option]="option()"
            [loading]="loading()"
          />
        }
      </div>

      <div class="flex min-w-80 flex-1 flex-col gap-4">
        <div class="flex flex-wrap items-center gap-3">
          <xui-segmented-control
            size="sm"
            aria-label="Cab slot"
            data-testid="cab-slot"
            [options]="slotOptions()"
            [value]="selected()"
            (valueChange)="$event !== null && selected.set($event)"
          />
          <span class="size-3 rounded-full" [style.background]="color()" aria-hidden="true"></span>
        </div>

        @if (muted()) {
          <xui-callout class="max-w-xl" minimal compact color="warning" data-testid="cab-muted">
            <div class="flex flex-wrap items-center gap-3">
              <span class="flex-1">Mic {{ selected() + 1 }} is muted. Editing it turns it on.</span>
              <button xuiButton size="sm" variant="outline" type="button" (click)="unmute()">Unmute</button>
            </div>
          </xui-callout>
        }

        <div class="flex flex-wrap gap-x-3 gap-y-5">
          @if (param('source'); as p) {
            <cth-param-control [block]="block()" [param]="p" [accent]="color()" (edited)="edited('source')" />
          }
          @for (key of soundKeys(); track key) {
            @if (param(key); as p) {
              <cth-param-control [block]="block()" [param]="p" [accent]="color()" (edited)="edited(key)" />
            }
          }
        </div>

        @if (source() !== Source.dynaCab) {
          <cth-asset-picker
            data-testid="cab-asset"
            [label]="assetLabel()"
            [accept]="source() === Source.ir ? '.wav' : '.cabgrid,.wav'"
            [assets]="source() === Source.ir ? library.impulseResponses() : library.cabGrids()"
            [reference]="block().assets[asset()]"
            (picked)="pick($event)"
          />
          @if (source() === Source.ir && fileIsPack()) {
            <p class="text-foreground-muted text-xs">This slot holds an IR pack: set Source to IR Pack to play it.</p>
          }
        }

        <div class="flex flex-wrap gap-x-3 gap-y-5" role="group" aria-label="Slot filters">
          @for (key of filters; track key) {
            @if (param(key); as p) {
              <cth-param-control [block]="block()" [param]="p" [accent]="color()" (edited)="edited(key)" />
            }
          }
        </div>

        <div class="flex flex-wrap gap-x-3 gap-y-5">
          @for (key of mixer; track key) {
            @if (param(key); as p) {
              <cth-param-control [block]="block()" [param]="p" [accent]="color()" (edited)="edited(key)" />
            }
          }
        </div>
      </div>
    </div>
  `
})
export class CabPanel {
  protected readonly live = inject(LiveStore);
  protected readonly library = inject(LibraryStore);
  private readonly catalog = inject(CatalogStore);

  readonly block = input.required<PresetBlock>();

  protected readonly Source = Source;
  protected readonly filters = Filters;
  protected readonly mixer = Mixer;
  protected readonly selected = signal(0);
  /** The plot under the cone: the IR files' responses, or the Align page (every slot's onset in time). */
  protected readonly view = signal<'response' | 'align'>('response');
  protected readonly viewOptions = [
    { label: 'Response', value: 'response' as const },
    { label: 'Align', value: 'align' as const }
  ];
  protected readonly loading = signal(false);
  private readonly measured = signal<Partial<Record<string, number[]>>>({});

  protected readonly asset = computed(() => slotAsset(this.selected()));
  protected readonly color = computed(() => MicColors[this.selected() % MicColors.length]);

  private readonly params = computed(
    () => new Map((this.catalog.info(this.block().type)?.params ?? []).map(p => [p.id, p] as const))
  );

  /** The current value of a parameter on the block's channel, or its default. */
  private readonly value = computed(() => {
    const block = this.block();
    const channel = this.live.channelOf(block.id);
    const params = this.params();
    return (id: string) => this.live.valueOf(block, channel, id) ?? params.get(id)?.default ?? 0;
  });

  protected readonly source = computed(() => sourceOf(this.selected(), this.value()));
  protected readonly muted = computed(() => isMuted(this.selected(), this.value()));

  /** Mic 1-4, with each slot's state, so a muted slot is plain before it is picked. */
  protected readonly slotOptions = computed(() => {
    const value = this.value();
    return Array.from({ length: CabSlots }, (_, slot) => ({
      label: `Mic ${slot + 1}${isMuted(slot, value) ? ' · muted' : isAudible(slot, value) ? '' : ' · off'}`,
      value: slot
    }));
  });

  /** The controls that shape the selected slot's sound: none for an IR file, cab to angle for a DynaCab. */
  protected readonly soundKeys = computed<readonly SlotKey[]>(() =>
    this.source() === Source.dynaCab ? [...Choices, ...Placement] : this.source() === Source.pack ? PackPlacement : []
  );

  protected readonly assetLabel = computed(
    () => `Mic ${this.selected() + 1} ${this.source() === Source.ir ? 'IR' : 'IR pack (.cabgrid or one .wav)'}`
  );

  protected readonly fileIsPack = computed(() => this.assetInfo(this.selected())?.kind === 'cabgrid');

  protected readonly mics = computed(() =>
    coneMics(this.value(), this.params().get(slotParamId(0, 'mic'))?.options ?? [], slot => this.packName(slot))
  );

  /** The IR slots whose files are plotted: IR sources with a .wav loaded. */
  private readonly irSlots = computed(() => {
    const value = this.value();
    return Array.from({ length: CabSlots }, (_, slot) => slot).filter(
      slot => sourceOf(slot, value) === Source.ir && this.assetInfo(slot)?.kind === 'ir'
    );
  });

  protected readonly plotted = computed(() => this.irSlots().length > 0);

  protected readonly option = computed<EChartsOption>(() => {
    const value = this.value();
    const cuts = (prefix: string) =>
      Frequencies.map(f =>
        cutsDb(
          value(`${prefix}lowcut`),
          value(`${prefix}lowslope`),
          value(`${prefix}highcut`),
          value(`${prefix}highslope`),
          f
        )
      );
    const master = cuts('');
    const levels = relativeLevelsDb(value, slot => this.plays(slot));
    const series = this.irSlots().flatMap(slot => {
      const curve = this.measured()[this.block().assets[slotAsset(slot)] ?? ''];
      const own = cuts(`m${slot + 1}`);
      // A slot that is not heard is drawn dashed at its level as if it played alone.
      const level = Number.isFinite(levels[slot]) ? levels[slot] : 0;
      return curve
        ? [
            {
              type: 'line' as const,
              name: `Mic ${slot + 1}`,
              showSymbol: false,
              color: MicColors[slot % MicColors.length],
              lineStyle: { type: isAudible(slot, value) ? ('solid' as const) : ('dashed' as const) },
              data: Frequencies.map((f, n) => [f, curve[n] + level + own[n] + master[n]])
            }
          ]
        : [];
    });
    return {
      grid: { left: 48, right: 16, top: 24, bottom: 32 },
      legend: { show: series.length > 1, top: 0 },
      tooltip: { trigger: 'axis', valueFormatter: v => `${Number(v).toFixed(1)} dB` },
      xAxis: { type: 'log', min: 20, max: 20_000, name: 'Hz', nameGap: 4 },
      yAxis: { type: 'value', name: 'dB', min: -36, max: 18, interval: 6 },
      series
    };
  });

  constructor() {
    effect(() => {
      const assets = this.block().assets;
      void this.measure(this.irSlots().map(slot => assets[slotAsset(slot)]!));
    });
  }

  protected param(key: SlotKey): ParameterInfo | undefined {
    return this.params().get(slotParamId(this.selected(), key));
  }

  /** After any edit of the selected slot: unmute it if it was muted (but not when the edit is the mute switch). */
  protected edited(key: SlotKey | 'asset', slot = this.selected()): void {
    const mute = unmuteOnEdit(slot, key, this.value());
    if (mute) {
      const block = this.block();
      this.live.setParam(block.id, this.live.channelOf(block.id), mute, 0);
    }
  }

  protected unmute(): void {
    this.edited('level');
  }

  protected move(move: ConeMove): void {
    const block = this.block();
    const channel = this.live.channelOf(block.id);
    this.selected.set(move.slot);
    this.live.setParam(block.id, channel, slotParamId(move.slot, 'position'), move.position);
    this.live.setParam(block.id, channel, slotParamId(move.slot, 'distance'), move.distance);
    this.edited('position', move.slot);
  }

  protected pick(asset: AssetInfo): void {
    const slot = this.selected();
    void this.live.setAsset(this.block().id, slotAsset(slot), asset.ref);
    this.edited('asset', slot);
  }

  /** Whether a slot's source has something to play: a DynaCab always, an IR or IR pack once it has a file. */
  private plays(slot: number): boolean {
    return sourceOf(slot, this.value()) === Source.dynaCab || this.block().assets[slotAsset(slot)] !== undefined;
  }

  private assetInfo(slot: number): AssetInfo | undefined {
    const reference = this.block().assets[slotAsset(slot)];
    return reference ? this.library.assets().find(a => a.ref === reference) : undefined;
  }

  private packName(slot: number): string | undefined {
    const reference = this.block().assets[slotAsset(slot)];
    if (!reference) {
      return undefined;
    }

    return (this.assetInfo(slot)?.name ?? 'IR pack').replace(/\.(cabgrid|wav)$/i, '');
  }

  private async measure(references: string[]): Promise<void> {
    this.loading.set(true);
    try {
      const curves = await Promise.all(references.map(ref => this.response(ref)));
      this.measured.set(Object.fromEntries(references.map((ref, i) => [ref, curves[i]])));
    } catch {
      this.measured.set({}); // an unreadable IR plots nothing; the engine reports real load failures
    } finally {
      this.loading.set(false);
    }
  }

  private response(reference: string): Promise<number[]> {
    let curve = responses.get(reference);
    if (!curve) {
      curve = this.library.content(reference).then(bytes => {
        const wav = decodeWav(bytes);
        const samples = wav.channels[0].subarray(0, Math.min(wav.channels[0].length, 8_192));
        // The engine normalises IRs to unit energy; do the same so curves compare.
        const energy = Math.sqrt(samples.reduce((sum, s) => sum + s * s, 0)) || 1;
        return impulseResponseDb(
          samples.map(s => s / energy),
          wav.sampleRate,
          Frequencies
        );
      });
      responses.set(reference, curve);
    }

    return curve;
  }
}
