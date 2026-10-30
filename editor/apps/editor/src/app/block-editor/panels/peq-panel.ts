import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, input, signal } from '@angular/core';
import type { PresetBlock } from '@cathode/api-client';
import { XuiKonvaImports, type XuiKonvaEventObject } from '@xui/konva';
import { logFrequencies } from '../../dsp/response';
import { LiveStore } from '../../state/live.store';
import {
  EqWellClass,
  clampDb as clampTo,
  eqArea,
  eqChartColors,
  eqCurve,
  eqGeometry,
  eqGrid,
  measureEqTile
} from './eq-chart';
import { peqGainFor, peqTotalDb, type PeqBand } from './peq';

const MaxDb = 24;
const Bands = [1, 2, 3, 4, 5] as const;
const Frequencies = logFrequencies(240);

const clampDb = (db: number) => clampTo(db, MaxDb);

/**
 * The parametric EQ as a curve (docs/02-architecture.md §7.3, via @xui/konva). Each band's handle sits on the curve,
 * at the whole EQ's response at its frequency, so a boost nearby lifts its neighbours' handles with the line. Drag a
 * handle to set its frequency and gain (the gain that puts the curve where it is dropped); scroll over it to change
 * its Q. The knobs beside it show the same parameters.
 *
 * It is a tile at the right of the block's Basic page, among the knobs: it measures the grid area it is given and
 * draws the chart to fill it, laid over the area rather than in it, so it never sets the rows' height itself.
 */
@Component({
  selector: 'cth-peq-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [XuiKonvaImports],
  host: { class: 'relative block min-h-48 self-stretch justify-self-stretch' },
  template: `
    <xui-konva-stage [class]="well" role="img" [attr.aria-label]="description()" [config]="stage()">
      <xui-konva-layer>
        @for (line of grid(); track $index) {
          <xui-konva-line [config]="line" />
        }
        <xui-konva-line [config]="area()" />
        <xui-konva-line [config]="curve()" />
        @for (handle of handles(); track handle.band) {
          <xui-konva-circle
            [config]="handle.config"
            (dragmove)="drag(handle.band, $event)"
            (wheel)="wheel(handle.band, $event)"
          />
        }
      </xui-konva-layer>
    </xui-konva-stage>
  `
})
export class PeqPanel {
  private readonly live = inject(LiveStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly block = input.required<PresetBlock>();

  protected readonly well = EqWellClass;
  /** The stage's size: the tile's area less its 1 px border, as the grid gives it. */
  private readonly size = signal({ width: 640, height: 220 });
  private readonly geo = computed(() => eqGeometry(this.size().width, this.size().height, MaxDb));
  protected readonly stage = computed(() => ({ width: this.size().width, height: this.size().height }));
  private readonly colors = signal(eqChartColors(this.host.nativeElement));

  constructor() {
    measureEqTile(this.host, this.size);
  }

  protected readonly bands = computed(() => {
    const block = this.block();
    const channel = this.live.channelOf(block.id);
    const value = (id: string, fallback: number) => this.live.valueOf(block, channel, id) ?? fallback;
    const defaults = [80, 400, 1_000, 3_000, 8_000];
    return Bands.map((band): PeqBand => ({
      band,
      type: band === 1 ? value('lowtype', 0) : band === 5 ? value('hightype', 0) : 1,
      frequency: value(`freq${band}`, defaults[band - 1] ?? 1_000),
      gain: value(`gain${band}`, 0),
      q: value(`q${band}`, 0.7071),
      slope: band === 1 ? value('lowslope', 1) : value('highslope', 1)
    }));
  });

  private readonly points = computed(() => {
    const bands = this.bands();
    const { xOf, yOf } = this.geo();
    return Frequencies.flatMap(f => [xOf(f), yOf(clampDb(peqTotalDb(bands, f)))]);
  });

  protected readonly curve = computed(() => eqCurve(this.points(), this.colors()));

  /** A faint fill between the curve and 0 dB, so boosts and cuts read at a glance. */
  protected readonly area = computed(() => eqArea(this.points(), this.geo(), this.colors()));

  protected readonly handles = computed(() => {
    const { xOf, yOf } = this.geo();
    return this.bands().map(b => ({
      band: b.band,
      config: {
        x: xOf(b.frequency),
        y: yOf(clampDb(peqTotalDb(this.bands(), b.frequency))),
        radius: 7,
        fill: this.colors().curve,
        stroke: this.colors().handle,
        strokeWidth: 1.5,
        shadowColor: 'black',
        shadowBlur: 4,
        shadowOpacity: 0.5,
        shadowOffsetY: 1,
        draggable: true
      }
    }));
  });

  protected readonly grid = computed(() => eqGrid(this.geo(), this.colors()));

  protected readonly description = computed(
    () =>
      'Parametric EQ curve: ' +
      this.bands()
        .map(b => `band ${b.band} ${Math.round(b.frequency)} Hz ${b.gain.toFixed(1)} dB`)
        .join(', ')
  );

  protected drag(band: number, event: XuiKonvaEventObject<MouseEvent>): void {
    const node = event.event.target;
    const block = this.block();
    const channel = this.live.channelOf(block.id);
    const { frequencyOf, dbOf } = this.geo();
    const frequency = frequencyOf(node.x());
    this.live.setParam(block.id, channel, `freq${band}`, frequency);
    const gain = peqGainFor(this.bands(), band, frequency, dbOf(node.y()));
    if (gain !== null) {
      this.live.setParam(block.id, channel, `gain${band}`, clampDb(gain));
    }
  }

  protected wheel(band: number, event: XuiKonvaEventObject<WheelEvent>): void {
    event.event.evt.preventDefault();
    const block = this.block();
    const current = this.bands().find(b => b.band === band)?.q;
    if (current === undefined) {
      return;
    }

    const next = Math.max(0.1, Math.min(10, current * (event.event.evt.deltaY < 0 ? 1.1 : 1 / 1.1)));
    this.live.setParam(block.id, this.live.channelOf(block.id), `q${band}`, next);
  }
}
