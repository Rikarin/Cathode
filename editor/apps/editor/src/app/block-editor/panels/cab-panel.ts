import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { type AssetInfo, type ParameterInfo, type PresetBlock, unwrap } from '@cathode/api-client';
import { type ConeMove, CthMeter, CthSpeakerCone, micColor, onAnimationFrame } from '@cathode/audio-ui';
import { XuiButtonImports } from '@xui/button';
import { XuiCalloutImports } from '@xui/callout';
import { XuiEChartImports } from '@xui/echarts';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import type { EChartsOption } from 'echarts';
import { ConnectionService } from '../../core/connection';
import { EventStream } from '../../core/events';
import { Notify } from '../../core/notify';
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
  delayLabel,
  isAudible,
  isMuted,
  relativeLevelsDb,
  slotAsset,
  slotParamId,
  sourceOf,
  takesFile,
  unmuteOnEdit
} from './cab';
import { CabAlign } from './cab-align';

const Choices: readonly SlotKey[] = ['cab', 'speaker', 'mic'];
const Placement: readonly SlotKey[] = ['position', 'distance', 'angle'];
const PackPlacement: readonly SlotKey[] = ['position', 'distance'];
const Filters: readonly SlotKey[] = ['lowcut', 'lowslope', 'highcut', 'highslope', 'length'];
/** A DynaCab's Distance sets its proximity, so it shows smoothing alone. */
const Shaping: readonly SlotKey[] = ['proximity', 'proxfreq', 'smoothing'];
const Mixer: readonly SlotKey[] = ['level', 'pan', 'delay', 'mute', 'solo', 'invert'];
/** Motor Drive's gain-reduction bar spans this many dB. */
const ReductionSpanDb = 12;
const Frequencies = logFrequencies(160);

/** Decoded responses are cached per asset: an IR never changes under its hash. */
const responses = new Map<string, Promise<number[]>>();
/** The responses that have finished decoding, so a plot of them needs no loading state. */
const decoded = new Map<string, number[]>();

const sameReferences = (a: readonly string[], b: readonly string[]): boolean =>
  a.length === b.length && a.every((reference, i) => reference === b[i]);

/**
 * The Cab's four slots, as the Axe-Fx III mixes Legacy IRs and DynaCabs: pick Mic 1-4, then its Source (an IR file, a
 * modelled DynaCab cab, speaker and mic, the user's multi-position IR pack, or Flat), its placement on the speaker cone
 * and its mixer controls. DynaCab and IR Pack slots are dragged across the cone (cap to edge) and away from the grille;
 * IR and IR Pack slots take a file. Each slot has its own cuts, IR length, proximity (not on a DynaCab, whose Distance
 * sets it) and smoothing, a delay shown as time and mic distance, Auto-Align on the modelled slots, and a relative
 * level (the slots' levels set the blend). Editing a muted slot unmutes it, so the edit is heard. Above the slot: the
 * preamp's input meter (0 dB is where the chosen type starts to clip), Motor Drive's gain reduction, and exporting the
 * block's mixed IR as a file or into the IR library. The plot shows the IR files' responses with their levels and the
 * block's cuts; the global controls (input, Motor Drive, air, mic preamp and its tone, master cuts, the room page,
 * level) stay on the rack below.
 */
@Component({
  selector: 'cth-cab-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AssetPicker,
    CthMeter,
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
          class="border-hairline-strong rounded-lg border bg-(--well) p-3 shadow-[inset_0_1px_3px_rgb(0_0_0/0.6),0_1px_0_var(--highlight)]"
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
        <div class="flex flex-wrap items-center gap-x-4 gap-y-2" data-testid="cab-meters">
          <span class="text-foreground-muted text-xs">Preamp in</span>
          <cth-meter
            class="w-32"
            orientation="horizontal"
            label="Preamp input, 0 dB at the onset of clipping"
            [sources]="preampSources"
          />
          <span class="text-foreground-muted text-xs">Motor</span>
          <div
            class="bg-surface-sunken ring-border h-1.5 w-20 overflow-hidden rounded-full ring-1 ring-inset"
            role="meter"
            aria-label="Motor Drive gain reduction"
            aria-valuemin="0"
            [attr.aria-valuemax]="reductionSpan"
            [attr.aria-valuenow]="reduction()"
          >
            <div class="bg-warning h-full" [style.width.%]="reductionPercent()"></div>
          </div>
          <span class="text-foreground-muted w-14 text-xs tabular-nums">-{{ reduction() }} dB</span>
          <span class="flex-1"></span>
          <button xuiButton size="sm" variant="ghost" type="button" data-testid="cab-export" (click)="exportIr()">
            Export IR
          </button>
          <button
            xuiButton
            size="sm"
            variant="outline"
            type="button"
            data-testid="cab-save-ir"
            [loading]="saving()"
            (click)="saveIr()"
          >
            Save as IR
          </button>
        </div>

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

        @if (takesFile(source())) {
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
          @for (key of filters(); track key) {
            @if (param(key); as p) {
              <cth-param-control [block]="block()" [param]="p" [accent]="color()" (edited)="edited(key)" />
            }
          }
        </div>

        <div class="flex flex-wrap gap-x-3 gap-y-5">
          @for (key of mixer(); track key) {
            @if (param(key); as p) {
              <cth-param-control [block]="block()" [param]="p" [accent]="color()" (edited)="edited(key)" />
            }
          }
        </div>
        <p class="text-foreground-muted text-xs tabular-nums" data-testid="cab-delay">
          Delay {{ delay() }}{{ unaligned() ? ', plus the mic’s distance (Auto-Align off)' : '' }}
        </p>
      </div>
    </div>
  `
})
export class CabPanel {
  protected readonly live = inject(LiveStore);
  protected readonly library = inject(LibraryStore);
  private readonly catalog = inject(CatalogStore);
  private readonly events = inject(EventStream);
  private readonly connection = inject(ConnectionService);
  private readonly notify = inject(Notify);

  readonly block = input.required<PresetBlock>();

  protected readonly Source = Source;
  protected readonly takesFile = takesFile;
  protected readonly reductionSpan = ReductionSpanDb;
  protected readonly selected = signal(0);
  protected readonly saving = signal(false);

  /** The preamp's input level as a linear peak for the meter: its 0 dB mark is the type's onset of clipping. */
  protected readonly preampSources = [() => 10 ** (this.meter('preampIn', -96) / 20)];

  /** Motor Drive's gain reduction, dB, to one decimal: read from telemetry on animation frames. */
  protected readonly reduction = signal(0);
  protected readonly reductionPercent = computed(() => Math.min(100, (this.reduction() / ReductionSpanDb) * 100));
  /** The plot under the cone: the IR files' responses, or the Align page (every slot's onset in time). */
  protected readonly view = signal<'response' | 'align'>('response');
  protected readonly viewOptions = [
    { label: 'Response', value: 'response' as const },
    { label: 'Align', value: 'align' as const }
  ];
  protected readonly loading = signal(false);
  private readonly measured = signal<Partial<Record<string, number[]>>>({});
  private measuring = 0;

  protected readonly asset = computed(() => slotAsset(this.selected()));
  protected readonly color = computed(() => micColor(this.selected()));

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

  /** The cuts and IR length, then proximity (not on a DynaCab, whose Distance sets it) and smoothing. */
  protected readonly filters = computed<readonly SlotKey[]>(() => [
    ...Filters,
    ...(this.source() === Source.dynaCab ? Shaping.slice(2) : Shaping)
  ]);

  /** The mixer, plus Auto-Align on the slots that have a mic distance to align (DynaCab and IR pack). */
  protected readonly mixer = computed<readonly SlotKey[]>(() =>
    this.source() === Source.dynaCab || this.source() === Source.pack ? [...Mixer, 'align'] : Mixer
  );

  protected readonly delay = computed(() => delayLabel(this.value()(slotParamId(this.selected(), 'delay'))));
  protected readonly unaligned = computed(
    () =>
      (this.source() === Source.dynaCab || this.source() === Source.pack) &&
      this.value()(slotParamId(this.selected(), 'align')) < 0.5
  );

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

  /**
   * The files to measure. Knob moves and state answers produce new values (and a new block object) but rarely new
   * files, so this only changes when the list of references does, and the measuring effect follows it alone.
   */
  private readonly irReferences = computed(
    () => {
      const assets = this.block().assets;
      return this.irSlots().flatMap(slot => assets[slotAsset(slot)] ?? []); // irSlots have a file loaded
    },
    { equal: sameReferences }
  );

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
      const relative = levels[slot] ?? -Infinity;
      const level = Number.isFinite(relative) ? relative : 0;
      return curve
        ? [
            {
              type: 'line' as const,
              name: `Mic ${slot + 1}`,
              showSymbol: false,
              color: micColor(slot),
              lineStyle: { type: isAudible(slot, value) ? ('solid' as const) : ('dashed' as const) },
              data: Frequencies.map((f, n) => [f, (curve[n] ?? 0) + level + (own[n] ?? 0) + (master[n] ?? 0)])
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
    effect(() => void this.measure(this.irReferences()));
    onAnimationFrame(() => {
      const reduction = Math.round(this.meter('motorReduction', 0) * 10) / 10;
      if (reduction !== this.reduction()) {
        this.reduction.set(reduction);
      }
    });
  }

  /** Downloads the block's current mixed IR (every slot that plays, cuts and smoothing baked in) as a .wav. */
  protected exportIr(): void {
    const id = this.block().id;
    void this.library.download(`/api/v1/live/blocks/${encodeURIComponent(id)}/ir`, `${id}.wav`);
  }

  /** Saves the block's current mixed IR into the asset store, to load in any IR slot. */
  protected async saveIr(): Promise<void> {
    const id = this.block().id;
    const stamp = new Date().toISOString().slice(0, 19).replace('T', ' ').replaceAll(':', '.');
    this.saving.set(true);
    const asset = await this.notify.run(async () =>
      unwrap(
        await this.connection.client.POST('/api/v1/live/blocks/{id}/ir', {
          params: { path: { id } },
          body: { name: `${id} mix ${stamp}` }
        })
      )
    );
    this.saving.set(false);
    if (asset) {
      await this.library.refreshAssets();
      this.notify.success(`Saved “${asset.name}”: pick it in an IR slot.`);
    }
  }

  /** One of the block's own meters from the latest telemetry, dB, or `fallback` before any arrives. */
  private meter(id: string, fallback: number): number {
    return this.events.blockMeter(this.block().id, id) ?? fallback;
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

  /** Whether a slot's source has something to play: a DynaCab or Flat always, an IR or IR pack once it has a file. */
  private plays(slot: number): boolean {
    return !takesFile(sourceOf(slot, this.value())) || this.block().assets[slotAsset(slot)] !== undefined;
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

  private async measure(references: readonly string[]): Promise<void> {
    const run = ++this.measuring;
    if (references.every(ref => decoded.has(ref))) {
      this.measured.set(Object.fromEntries(references.map(ref => [ref, decoded.get(ref)])));
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    let measured: Partial<Record<string, number[]>>;
    try {
      const curves = await Promise.all(references.map(ref => this.response(ref)));
      measured = Object.fromEntries(references.map((ref, i) => [ref, curves[i]]));
    } catch {
      measured = {}; // an unreadable IR plots nothing; the engine reports real load failures
    }

    if (run === this.measuring) {
      // A later measure (other files picked meanwhile) wins over this one.
      this.measured.set(measured);
      this.loading.set(false);
    }
  }

  private response(reference: string): Promise<number[]> {
    let curve = responses.get(reference);
    if (!curve) {
      curve = this.library.content(reference).then(bytes => {
        const wav = decodeWav(bytes);
        const [first] = wav.channels;
        if (!first) {
          throw new Error('The WAV file has no channels.');
        }

        const samples = first.subarray(0, Math.min(first.length, 8_192));
        // The engine normalises IRs to unit energy; do the same so curves compare.
        const energy = Math.sqrt(samples.reduce((sum, s) => sum + s * s, 0)) || 1;
        return impulseResponseDb(
          samples.map(s => s / energy),
          wav.sampleRate,
          Frequencies
        );
      });
      responses.set(reference, curve);
      curve.then(
        db => decoded.set(reference, db),
        () => responses.delete(reference) // not cached, so the file is read again next time
      );
    }

    return curve;
  }
}
