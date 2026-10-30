import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  signal
} from '@angular/core';
import { type FrequencyResponse, type PresetBlock, unwrap } from '@cathode/api-client';
import { XuiKonvaImports } from '@xui/konva';
import { ConnectionService } from '../../core/connection';
import { LiveStore } from '../../state/live.store';
import { EqWellClass, clampDb, eqArea, eqChartColors, eqCurve, eqGeometry, eqGrid, measureEqTile } from './eq-chart';

/** The chart's range, dB: the bands' ±12 with room for the passive types' overshoot and the level. */
const MaxDb = 18;
/** How long the values must rest before the response is asked for again, ms. */
export const RefetchDelayMs = 100;

/**
 * The block types with a graphic EQ the Server measures, the page its chart sits on (null for the first page) and
 * what the chart is called: the Graphic EQ itself, the Drive's EQ 2 and the Amp's Output EQ.
 */
const EqResponses: Readonly<Record<string, { page: string | null; name: string }>> = {
  geq: { page: null, name: 'Graphic EQ' },
  drive: { page: 'Graphic EQ', name: 'EQ 2' },
  circuitamp: { page: 'EQ', name: 'Output EQ' }
};

/**
 * Whether a block of `type` shows its EQ response on `page`, of its shown `pages`: the Graphic EQ on its first page
 * (or its only one), the Drive on its Graphic EQ page, the Amp on its EQ page.
 */
export function showsEqResponse(type: string, page: string | null, pages: readonly string[]): boolean {
  const on = EqResponses[type]?.page;
  if (on === undefined) {
    return false;
  }

  return on === null ? pages.length <= 1 || page === pages[0] : page === on;
}

/**
 * A block's graphic EQ's frequency response, as a tile four knobs wide and two high at the right of the page with the
 * EQ's controls: the Graphic EQ's own, the Drive's EQ 2 or the Amp's Output EQ. The Server measures it on the engine's
 * own equaliser for that block (the Graphic EQ's passive types on their component-level networks, whose bands
 * interact; the Amp's on its own approximation), so the chart shows what is heard; an EQ that is off reads flat. It
 * is asked for again a moment after the block's stored values change, and the last curve stays up while the next is
 * on its way; an answer overtaken by a newer request is dropped. Faint lines mark the layout's band centres.
 *
 * Like the Parametric EQ's chart, it measures the grid area it is given and draws to fill it, laid over the area
 * rather than in it, so it never sets the rows' height itself.
 */
@Component({
  selector: 'cth-eq-response',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [XuiKonvaImports],
  host: { class: 'relative block min-h-48 self-stretch justify-self-stretch', 'data-testid': 'eq-response' },
  template: `
    <xui-konva-stage [class]="well" role="img" [attr.aria-label]="description()" [config]="stage()">
      <xui-konva-layer>
        @for (line of grid(); track $index) {
          <xui-konva-line [config]="line" />
        }
        @for (line of centres(); track $index) {
          <xui-konva-line [config]="line" />
        }
        @if (response()) {
          <xui-konva-line [config]="area()" />
          <xui-konva-line [config]="curve()" />
        }
      </xui-konva-layer>
    </xui-konva-stage>
  `
})
export class EqResponse {
  private readonly connection = inject(ConnectionService);
  private readonly live = inject(LiveStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly block = input.required<PresetBlock>();

  protected readonly well = EqWellClass;
  /** The stage's size: the tile's area less its 1 px border, as the grid gives it. */
  private readonly size = signal({ width: 640, height: 220 });
  private readonly geo = computed(() => eqGeometry(this.size().width, this.size().height, MaxDb));
  protected readonly stage = computed(() => ({ width: this.size().width, height: this.size().height }));
  private readonly colors = signal(eqChartColors(this.host.nativeElement));

  /** The latest response the Server gave; null until the first arrives. */
  protected readonly response = signal<FrequencyResponse | null>(null);
  /** Counts requests, so an answer to one that a later request overtook is dropped. */
  private request = 0;

  /**
   * What the response depends on: the block, the channel it runs and that channel's stored values. The Server has
   * these once it has confirmed them, so a knob's drag is followed as its values land.
   */
  private readonly settings = computed(() => {
    const block = this.block();
    const channel = this.live.channelOf(block.id);
    return JSON.stringify([block.id, channel, block.channels[channel] ?? {}]);
  });

  constructor() {
    measureEqTile(this.host, this.size);
    let timer: ReturnType<typeof setTimeout> | undefined;
    effect(() => {
      this.settings();
      const id = this.block().id;
      clearTimeout(timer);
      timer = setTimeout(() => void this.fetch(id), RefetchDelayMs);
    });
    inject(DestroyRef).onDestroy(() => {
      clearTimeout(timer);
      this.request++; // nothing that is still on its way lands after this
    });
  }

  private async fetch(id: string): Promise<void> {
    const request = ++this.request;
    try {
      const response = unwrap(
        await this.connection.client.GET('/api/v1/live/blocks/{id}/response', { params: { path: { id } } })
      );
      if (request === this.request) {
        this.response.set(response);
      }
    } catch {
      // The curve is a view: a failed read keeps the last one, and the next change asks again.
    }
  }

  private readonly points = computed(() => {
    const response = this.response();
    const { xOf, yOf } = this.geo();
    return (response?.frequencies ?? []).flatMap((f, i) => [xOf(f), yOf(clampDb(response?.db[i] ?? 0, MaxDb))]);
  });

  protected readonly curve = computed(() => eqCurve(this.points(), this.colors()));
  protected readonly area = computed(() => eqArea(this.points(), this.geo(), this.colors()));
  protected readonly grid = computed(() => eqGrid(this.geo(), this.colors()));

  /** A faint line at each of the layout's band centres; none while the EQ is off. */
  protected readonly centres = computed(() => {
    const { xOf, height } = this.geo();
    const color = this.colors().grid;
    return (this.response()?.centers ?? []).map(f => ({
      points: [xOf(f), 0, xOf(f), height],
      stroke: color,
      strokeWidth: 1,
      dash: [2, 4],
      listening: false
    }));
  });

  protected readonly description = computed(() => {
    const response = this.response();
    const name = `${EqResponses[this.block().type]?.name ?? 'EQ'} response`;
    if (!response) {
      return name;
    }

    const furthest = response.db.reduce((a, b) => (Math.abs(b) > Math.abs(a) ? b : a), 0);
    return `${name}, ${furthest.toFixed(1)} dB at its furthest from flat`;
  });
}
