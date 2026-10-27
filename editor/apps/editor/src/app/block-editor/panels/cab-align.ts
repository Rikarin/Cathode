import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import type { PresetBlock } from '@cathode/api-client';
import { unwrap } from '@cathode/api-client';
import { micColor } from '@cathode/audio-ui';
import { XuiButtonImports } from '@xui/button';
import { ConnectionService } from '../../core/connection';
import { LiveStore } from '../../state/live.store';
import { CabSlots, type SlotAlignment, alignedDelays, arrivalMs, clampDelay, slotParamId, waveformPath } from './cab';

/** The time span the Align page shows, ms: the slots' 10 ms heads and room for their delays. */
const SpanMs = 10;
const Width = 480;
const LaneHeight = 44;

/**
 * The Cab's Align page: each slot's IR head as the engine mixes it (`GET /api/v1/live/blocks/{id}/cab/align`), on a
 * shared time axis and shifted by its Delay (and Auto Delay), with its onset marked. Drag a waveform sideways to set
 * the slot's Delay, nudge it by 0.01 or 0.1 ms, or line every heard slot up with the latest onset.
 */
@Component({
  selector: 'cth-cab-align',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [XuiButtonImports],
  template: `
    <div class="flex flex-col gap-2" data-testid="cab-align">
      <svg
        class="bg-surface-sunken border-border w-full touch-none rounded-xl border select-none"
        role="img"
        aria-label="Slot alignment"
        [attr.viewBox]="'0 0 ' + width + ' ' + height()"
        (pointermove)="drag($event)"
        (pointerup)="dragging.set(null)"
        (pointerleave)="dragging.set(null)"
      >
        @for (ms of ticks; track ms) {
          <line [attr.x1]="x(ms)" [attr.x2]="x(ms)" y1="0" [attr.y2]="height()" class="stroke-border" />
          <text [attr.x]="x(ms) + 2" [attr.y]="height() - 3" class="fill-foreground-muted text-[9px]">{{ ms }} ms</text>
        }
        @for (lane of lanes(); track lane.slot.slot) {
          <g
            [attr.transform]="'translate(0,' + lane.top + ')'"
            [attr.opacity]="lane.slot.audible ? 1 : 0.35"
            class="cursor-ew-resize"
            (pointerdown)="grab($event, lane.slot)"
          >
            <rect x="0" y="0" [attr.width]="width" [attr.height]="laneHeight" fill="transparent" />
            <path [attr.d]="lane.path" fill="none" [attr.stroke]="lane.color" stroke-width="1.5" />
            <line
              [attr.x1]="x(lane.arrival)"
              [attr.x2]="x(lane.arrival)"
              y1="2"
              [attr.y2]="laneHeight - 2"
              [attr.stroke]="lane.color"
              stroke-dasharray="3 2"
            />
            <text x="4" y="11" class="text-[10px]" [attr.fill]="lane.color">
              Mic {{ lane.slot.slot + 1 }} · {{ lane.arrival.toFixed(2) }} ms
            </text>
          </g>
        }
      </svg>
      <div class="flex flex-wrap items-center gap-2">
        <span class="text-foreground-muted text-xs">Mic {{ selected() + 1 }} delay</span>
        @for (step of steps; track step) {
          <button xuiButton size="sm" variant="outline" type="button" (click)="nudge(step)">
            {{ step > 0 ? '+' : '' }}{{ step }}
          </button>
        }
        <button xuiButton size="sm" type="button" [disabled]="!slots().length" (click)="alignAll()">
          Align onsets
        </button>
      </div>
    </div>
  `
})
export class CabAlign {
  private readonly connection = inject(ConnectionService);
  private readonly live = inject(LiveStore);

  readonly block = input.required<PresetBlock>();
  /** The slot the nudge buttons move; dragging a lane selects it. */
  readonly selected = input(0);
  readonly selectedChange = output<number>();

  protected readonly width = Width;
  protected readonly laneHeight = LaneHeight;
  protected readonly ticks = [0, 2, 4, 6, 8];
  protected readonly steps = [-0.1, -0.01, 0.01, 0.1];
  protected readonly slots = signal<SlotAlignment[]>([]);
  private readonly sampleRate = signal(48_000);
  protected readonly dragging = signal<{ slot: number; startX: number; startDelay: number } | null>(null);

  protected readonly height = computed(() => Math.max(1, this.slots().length) * LaneHeight + 14);

  protected readonly lanes = computed(() =>
    this.slots().map((slot, i) => ({
      slot,
      top: i * LaneHeight,
      color: micColor(slot.slot),
      arrival: arrivalMs(slot),
      path: waveformPath(slot.head, this.sampleRate(), slot.autoDelayMs + slot.delayMs, SpanMs, Width, LaneHeight)
    }))
  );

  /** What the alignment depends on: the slots' parameters and the block's files. Refetched when it changes. */
  private readonly key = computed(() => {
    const block = this.block();
    return JSON.stringify([block.channels, block.assets]);
  });

  constructor() {
    effect(() => {
      this.key();
      void this.refresh();
    });
  }

  protected x(ms: number): number {
    return (ms / SpanMs) * Width;
  }

  protected grab(event: PointerEvent, slot: SlotAlignment): void {
    // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition -- jsdom (the unit tests) has no pointer capture
    (event.target as Element).setPointerCapture?.(event.pointerId);
    this.selectedChange.emit(slot.slot);
    this.dragging.set({ slot: slot.slot, startX: this.svgX(event), startDelay: slot.delayMs });
  }

  protected drag(event: PointerEvent): void {
    const drag = this.dragging();
    if (drag) {
      const deltaMs = ((this.svgX(event) - drag.startX) / Width) * SpanMs;
      this.setDelay(drag.slot, drag.startDelay + deltaMs);
    }
  }

  protected nudge(stepMs: number): void {
    const slot = this.slots().find(s => s.slot === this.selected());
    if (slot) {
      this.setDelay(slot.slot, slot.delayMs + stepMs);
    }
  }

  protected alignAll(): void {
    const delays = alignedDelays(this.slots());
    this.slots().forEach((slot, i) => this.setDelay(slot.slot, delays[i] ?? slot.delayMs)); // one delay per slot
  }

  private setDelay(slot: number, ms: number): void {
    const delay = clampDelay(ms);
    this.slots.update(slots => slots.map(s => (s.slot === slot ? { ...s, delayMs: delay } : s)));
    const block = this.block();
    this.live.setParam(block.id, this.live.channelOf(block.id), slotParamId(slot, 'delay'), delay);
  }

  /** The pointer's x in the view box. */
  private svgX(event: PointerEvent): number {
    const svg = (event.currentTarget as Element | null)?.closest('svg') ?? (event.target as Element).closest('svg');
    const box = svg?.getBoundingClientRect();
    return box && box.width > 0 ? ((event.clientX - box.left) / box.width) * Width : 0;
  }

  private async refresh(): Promise<void> {
    if (this.dragging()) {
      return; // the drag is setting the delay; the view already shows it
    }

    try {
      const alignment = unwrap(
        await this.connection.client.GET('/api/v1/live/blocks/{id}/cab/align', {
          params: { path: { id: this.block().id } }
        })
      );
      this.sampleRate.set(alignment.sampleRate);
      this.slots.set(
        alignment.slots
          .filter(s => s.plays)
          .slice(0, CabSlots)
          .map(s => ({ ...s, head: s.head }))
      );
    } catch {
      this.slots.set([]); // the engine reports real failures; the page just shows nothing
    }
  }
}
