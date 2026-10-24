import { ChangeDetectionStrategy, Component, computed, inject, model, signal } from '@angular/core';
import { onAnimationFrame } from '@cathode/audio-ui';
import { XuiButtonImports } from '@xui/button';
import { XuiDialogImports } from '@xui/dialog';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { EventStream } from '../core/events';
import { CatalogStore } from '../state/catalog.store';
import { LiveStore } from '../state/live.store';
import { LoudnessIntegrator, levelTarget, suggestLevel } from '../tools/leveling';

const formatLufs = (lufs: number | null | undefined) =>
  lufs === null || lufs === undefined || lufs <= -70 ? '—' : `${lufs.toFixed(1)} LUFS`;

/**
 * Preset Leveling: play the preset for a while, then set the amp's (or the Output's) level so it plays at a chosen
 * loudness. The engine meters the output with BS.1770; this gates and averages the momentary readings it sends.
 */
@Component({
  selector: 'cth-leveling-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [XuiButtonImports, XuiDialogImports, XuiSegmentedControlImports],
  template: `
    <xui-dialog title="Preset Leveling" icon="matGraphicEqRound" [(open)]="open">
      <xui-dialog-body class="flex flex-col gap-4">
        <p class="text-foreground-muted text-sm">
          Press Measure and play the preset as you would on stage, then stop and apply the suggested level.
        </p>
        <div class="grid grid-cols-3 gap-3 text-center">
          <div>
            <div class="text-foreground-muted text-[11px] uppercase">Momentary</div>
            <div class="text-sm tabular-nums" data-testid="lufs-momentary">{{ momentary() }}</div>
          </div>
          <div>
            <div class="text-foreground-muted text-[11px] uppercase">Short-term</div>
            <div class="text-sm tabular-nums">{{ shortTerm() }}</div>
          </div>
          <div>
            <div class="text-foreground-muted text-[11px] uppercase">Measured</div>
            <div class="text-sm font-semibold tabular-nums" data-testid="lufs-measured">{{ measuredText() }}</div>
          </div>
        </div>
        <div class="flex items-center gap-3">
          <span class="text-foreground-muted text-xs">Target</span>
          <xui-segmented-control
            size="sm"
            aria-label="Target loudness"
            [options]="targets"
            [value]="target()"
            (valueChange)="$event !== null && target.set($event)"
          />
        </div>
        @if (destination(); as goal) {
          @if (suggestion(); as s) {
            <p class="text-sm" data-testid="leveling-suggestion">
              {{ goal.label }}: {{ s.change >= 0 ? '+' : '' }}{{ s.change }} dB, to {{ s.next }} dB
              @if (s.clamped) {
                <span class="text-warning">(the level's range stops it short)</span>
              }
            </p>
          }
        } @else {
          <p class="text-warning text-sm">This preset has no amp or Output level to set.</p>
        }
      </xui-dialog-body>
      <xui-dialog-footer>
        <button xuiButton variant="outline" type="button" (click)="toggle()">
          {{ measuring() ? 'Stop' : 'Measure' }}
        </button>
        <button
          xuiButton
          type="button"
          data-testid="leveling-apply"
          [disabled]="!suggestion() || measuring()"
          (click)="apply()"
        >
          Apply
        </button>
      </xui-dialog-footer>
    </xui-dialog>
  `
})
export class LevelingDialog {
  private readonly events = inject(EventStream);
  private readonly live = inject(LiveStore);
  private readonly catalog = inject(CatalogStore);
  private readonly integrator = new LoudnessIntegrator();
  private lastFrame: unknown = null;

  readonly open = model(false);
  protected readonly measuring = signal(false);
  protected readonly measured = signal<number | null>(null);
  protected readonly momentary = signal('—');
  protected readonly shortTerm = signal('—');
  protected readonly target = signal(-18);
  protected readonly targets = [-23, -18, -14].map(v => ({ label: `${v}`, value: v }));
  protected readonly measuredText = computed(() => formatLufs(this.measured()));

  protected readonly destination = computed(() =>
    levelTarget(
      this.live.preset()?.blocks ?? [],
      type => this.catalog.info(type)?.params.some(p => p.id === 'level') ?? false,
      type => this.catalog.info(type)?.name ?? type
    )
  );

  private readonly range = computed(() => {
    const goal = this.destination();
    const block = goal && this.live.preset()?.blocks.find(b => b.id === goal.block);
    const param = block && this.catalog.info(block.type)?.params.find(p => p.id === goal.param);
    if (!goal || !block || !param) {
      return null;
    }

    const current = this.live.valueOf(block, this.live.channelOf(block.id), param.id) ?? param.default;
    return { block, current, min: param.min, max: param.max };
  });

  protected readonly suggestion = computed(() => {
    const measured = this.measured();
    const range = this.range();
    return measured === null || !range
      ? null
      : suggestLevel(measured, this.target(), range.current, range.min, range.max);
  });

  constructor() {
    onAnimationFrame(() => {
      const frame = this.events.latest;
      if (!frame || frame === this.lastFrame) {
        return;
      }

      this.lastFrame = frame;
      this.momentary.set(formatLufs(frame.loudnessMomentary));
      this.shortTerm.set(formatLufs(frame.loudnessShortTerm));
      if (this.measuring()) {
        this.integrator.add(frame.loudnessMomentary);
        this.measured.set(this.integrator.integrated());
      }
    });
  }

  protected toggle(): void {
    if (!this.measuring()) {
      this.integrator.reset();
      this.measured.set(null);
    }

    this.measuring.update(on => !on);
  }

  protected apply(): void {
    const s = this.suggestion();
    const range = this.range();
    const goal = this.destination();
    if (s && range && goal) {
      this.live.setParam(range.block.id, this.live.channelOf(range.block.id), goal.param, s.next);
      this.measured.set(null);
    }
  }
}
