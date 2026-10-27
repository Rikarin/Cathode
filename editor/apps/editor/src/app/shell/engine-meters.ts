import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CthMeter, onAnimationFrame } from '@cathode/audio-ui';
import { XuiStatusImports } from '@xui/status';
import { XuiTooltipImports } from '@xui/tooltip';
import { ClipHold, InputClipLevel, clippedOutputs } from '../core/clip';
import { SidecarStatuses, StaleTelemetryMs, engineHealth } from '../core/engine-health';
import { EventStream } from '../core/events';
import { CatalogStore } from '../state/catalog.store';
import { LiveStore } from '../state/live.store';

/**
 * Input and output meters, CPU and the connection light, all fed straight from telemetry frames, plus the preset's
 * estimated DSP load from the catalog's measured block costs. The light is green only while the engine itself runs:
 * the desktop app's host is up and telemetry keeps coming (engineHealth). A clip mark lights beside a meter (and holds
 * for a moment) when the input comes within 0.5 dB of full scale or an output reaches −1 dBFS.
 */
@Component({
  selector: 'cth-engine-meters',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CthMeter, XuiStatusImports, XuiTooltipImports],
  host: { class: 'flex h-8 items-center gap-2' },
  template: `
    <div class="flex flex-col justify-center gap-1">
      @for (meter of meters; track meter.label) {
        <div class="flex items-center gap-2">
          <!-- The caption turns into the clip mark, so the warning takes no room in the full top bar. -->
          <span
            class="w-6 rounded-sm text-center font-semibold tracking-wide"
            [class]="meter.clip() ? 'bg-error text-[9px] text-white' : 'text-foreground-muted text-[10px]'"
            [attr.data-testid]="meter.caption === 'IN' ? 'input-clip' : 'output-clip'"
            [attr.data-clipping]="meter.clip()"
            [attr.aria-hidden]="meter.clip() ? null : 'true'"
            [attr.role]="meter.clip() ? 'status' : null"
            [attr.aria-label]="meter.clip() ? meter.clipText() : null"
            [xuiTooltip]="meter.clip() ? meter.clipText() : meter.label"
          >
            {{ meter.clip() ? 'CLIP' : meter.caption }}
          </span>
          <cth-meter class="w-24" orientation="horizontal" [label]="meter.label" [sources]="meter.sources" />
        </div>
      }
    </div>
    <span
      class="text-foreground-muted w-16 text-xs whitespace-nowrap tabular-nums max-xl:hidden"
      [xuiTooltip]="'Engine CPU, 99.9th percentile of callbacks'"
    >
      CPU {{ cpu() }} %
    </span>
    <span
      class="text-foreground-muted text-xs whitespace-nowrap tabular-nums max-xl:hidden"
      data-testid="cpu-estimate"
      [xuiTooltip]="'Estimated DSP load of this preset, from its blocks’ measured costs at 48 kHz'"
    >
      Est. {{ estimate() }} %
    </span>
    <span class="flex items-center gap-1.5 text-xs" data-testid="engine-status" [xuiTooltip]="health().text">
      <xui-status [presence]="health().presence" size="sm" />
      <span class="sr-only">{{ health().text }}</span>
    </span>
  `
})
export class EngineMeters {
  private readonly events = inject(EventStream);
  private readonly live = inject(LiveStore);
  private readonly catalog = inject(CatalogStore);
  private readonly sidecars = inject(SidecarStatuses);
  /** No telemetry for a while although the socket is open. */
  private readonly stale = signal(false);

  private readonly inputHold = new ClipHold();
  private readonly outputHold = new ClipHold();
  private readonly inputClip = signal(false);
  private readonly outputClip = signal<number[]>([]);
  private lastClipped: number[] = [];

  protected readonly meters = [
    {
      caption: 'IN',
      label: 'Input level',
      sources: [() => this.events.latest?.inputPeak ?? 0],
      clip: this.inputClip,
      clipText: () => 'Input 1 within 0.5 dB of full scale: turn the interface gain down (and update the calibration)'
    },
    {
      caption: 'OUT',
      label: 'Output level',
      sources: [() => this.events.latest?.outputPeakLeft ?? 0, () => this.events.latest?.outputPeakRight ?? 0],
      clip: computed(() => this.outputClip().length > 0),
      clipText: () =>
        this.outputClip().length > 0
          ? `Output ${this.outputClip().join(', ')} at -1 dBFS or above: lower its Boost/Pad or the preset level`
          : 'No output above -1 dBFS'
    }
  ];
  protected readonly cpu = signal(0);
  protected readonly estimate = computed(() => {
    const total = (this.live.preset()?.blocks ?? []).reduce(
      (sum, block) => sum + (this.catalog.info(block.type)?.cpuCost ?? 0),
      0
    );
    return total < 10 ? total.toFixed(1) : Math.round(total).toString();
  });
  protected readonly health = computed(() => engineHealth(this.events.status(), this.sidecars.host(), this.stale()));

  constructor() {
    onAnimationFrame(() => {
      const latest = this.events.latest;
      const cpu = Math.round(latest?.cpuP999 ?? 0);
      if (cpu !== this.cpu()) {
        this.cpu.set(cpu);
      }

      const now = performance.now();
      const stale = now - Math.max(this.events.latestAt, this.events.connectedAt) > StaleTelemetryMs;
      if (stale !== this.stale()) {
        this.stale.set(stale);
      }

      const input = this.inputHold.update((latest?.inputPeak ?? 0) >= InputClipLevel, now);
      if (input !== this.inputClip()) {
        this.inputClip.set(input);
      }

      const clipped = clippedOutputs(latest);
      if (clipped.length > 0) {
        this.lastClipped = clipped;
      }

      const output = this.outputHold.update(clipped.length > 0, now) ? this.lastClipped : [];
      if (output.join() !== this.outputClip().join()) {
        this.outputClip.set(output);
      }
    });
  }
}
