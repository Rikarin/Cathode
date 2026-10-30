import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  model,
  signal,
  untracked
} from '@angular/core';
import {
  EngageModes,
  ModifierCurves,
  ModifierSources,
  type ParameterInfo,
  type PresetModifier
} from '@cathode/api-client';
import { CthKnob, formatValue } from '@cathode/audio-ui';
import { XuiButtonImports } from '@xui/button';
import { XuiDialogImports } from '@xui/dialog';
import { XuiEChartImports } from '@xui/echarts';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { XuiSelectImports } from '@xui/select';
import type { EChartsOption } from 'echarts';
import { LiveStore } from '../state/live.store';

const SourceNames: Record<string, string> = {
  lfo1: 'LFO 1',
  lfo2: 'LFO 2',
  adsr1: 'ADSR 1',
  adsr2: 'ADSR 2',
  seq: 'Sequencer',
  env: 'Envelope',
  pitch: 'Pitch',
  manualA: 'Manual A',
  manualB: 'Manual B',
  manualC: 'Manual C',
  manualD: 'Manual D',
  scene1: 'Scene Controller 1',
  scene2: 'Scene Controller 2',
  scene3: 'Scene Controller 3',
  scene4: 'Scene Controller 4'
};

/** The modifier curves, as the engine applies them (Cathode.Engine.Rig.Modulation.Map). */
export function shape(curve: string, s: number): number {
  switch (curve) {
    case 'exp':
      return (Math.exp(3 * s) - 1) / (Math.exp(3) - 1);
    case 'log':
      return Math.log(1 + 19 * s) / Math.log(20);
    case 'scurve':
      return s * s * (3 - 2 * s);
    default:
      return s;
  }
}

/** The engage settings a modifier has when it stores none (Cathode.Engine.Graph.EngageSwitch). */
const EngageDefaults = { threshold: 0.5, offDelayMs: 0 };

/**
 * The theme's colours the preview chart needs, read from the document as they stand (a canvas chart cannot resolve
 * CSS variables itself): the accent for the curve, the well's lines for the grid, and the subtle text for the labels.
 */
function themeColors(): { accent: string; line: string; label: string } {
  const style = typeof document === 'undefined' ? null : getComputedStyle(document.documentElement);
  const read = (name: string, fallback: string) => style?.getPropertyValue(name).trim() || fallback;
  return {
    accent: read('--color-primary', '#0071e3'),
    line: read('--well-line', 'rgb(255 255 255 / 0.06)'),
    label: read('--color-foreground-subtle', '#98989f')
  };
}

/**
 * When an engage modifier engages its block, to follow "Engaged": "at 50 % and above", "as soon as it moves, off
 * after 300 ms below 5 %".
 */
export function engagedWhen(m: Pick<PresetModifier, 'mode' | 'threshold' | 'offDelayMs'>): string {
  const percent = `${Math.round((m.threshold ?? EngageDefaults.threshold) * 100)} %`;
  const delay = Math.round(m.offDelayMs ?? EngageDefaults.offDelayMs);
  if (m.mode === 'moving') {
    return `as soon as it moves, off ${delay > 0 ? `after ${delay} ms` : 'once back'} below ${percent}`;
  }

  return delay > 0 ? `at ${percent} and above, off after ${delay} ms below it` : `at ${percent} and above`;
}

/** An engage modifier as it is stored: settings at their defaults are left out, as a preset stores them. */
export function sparseEngage(m: PresetModifier): PresetModifier {
  const { mode, threshold, offDelayMs, ...rest } = m;
  return {
    ...rest,
    ...(mode === 'moving' ? { mode } : {}),
    ...(threshold != null && threshold !== EngageDefaults.threshold ? { threshold } : {}),
    ...(offDelayMs != null && offDelayMs > 0 ? { offDelayMs } : {})
  };
}

/**
 * Edits the modifier on one parameter (docs/02-architecture.md §5.4): which controller drives it, over what range,
 * through which curve, with what damping. The chart previews source to value. On a block's engage switch it also
 * sets how the value engages the block: by position or by movement, the threshold and the off delay.
 */
@Component({
  selector: 'cth-modifier-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CthKnob,
    XuiButtonImports,
    XuiDialogImports,
    XuiEChartImports,
    XuiSegmentedControlImports,
    XuiSelectImports
  ],
  template: `
    <xui-dialog [title]="'Modifier: ' + param().name" icon="matTuneRound" [(open)]="open">
      <xui-dialog-body>
        <div class="flex flex-col gap-4">
          <label class="flex flex-col gap-1">
            <span class="text-foreground-muted text-xs">Source</span>
            <xui-select
              aria-label="Modifier source"
              [items]="sources"
              [itemText]="sourceName"
              [value]="draft().source"
              (valueChange)="patch({ source: $event ?? 'lfo1' })"
            />
          </label>
          <div class="flex flex-wrap gap-6">
            <cth-knob
              label="Min"
              [unit]="param().unit"
              [min]="param().min"
              [max]="param().max"
              [taper]="log() ? 'log' : 'linear'"
              [value]="draft().min"
              (valueChange)="patch({ min: $event })"
            />
            <cth-knob
              label="Max"
              [unit]="param().unit"
              [min]="param().min"
              [max]="param().max"
              [taper]="log() ? 'log' : 'linear'"
              [value]="draft().max"
              (valueChange)="patch({ max: $event })"
            />
            <cth-knob
              label="Attack"
              unit="ms"
              [min]="0"
              [max]="2000"
              [value]="draft().attackMs"
              (valueChange)="patch({ attackMs: $event })"
            />
            <cth-knob
              label="Release"
              unit="ms"
              [min]="0"
              [max]="2000"
              [value]="draft().releaseMs"
              (valueChange)="patch({ releaseMs: $event })"
            />
          </div>
          <label class="flex items-center gap-3">
            <span class="text-foreground-muted text-xs">Applies on</span>
            <xui-segmented-control
              size="sm"
              aria-label="Channels the modifier applies on"
              data-testid="modifier-scope"
              [options]="scopes"
              [value]="draft().channel ?? 'all'"
              (valueChange)="patch({ channel: $event === 'all' || $event === null ? null : $event })"
            />
          </label>
          <xui-segmented-control
            size="sm"
            aria-label="Curve"
            [options]="curves"
            [value]="draft().curve"
            (valueChange)="patch({ curve: $event ?? 'linear' })"
          />
          @if (engage()) {
            <div class="border-border flex flex-col gap-3 border-t pt-4" data-testid="engage-settings">
              <label class="flex items-center gap-3">
                <span class="text-foreground-muted text-xs">Engages by</span>
                <xui-segmented-control
                  size="sm"
                  aria-label="Engage mode"
                  data-testid="engage-mode"
                  [options]="engageModes"
                  [value]="draft().mode ?? 'position'"
                  (valueChange)="patch({ mode: $event === 'moving' ? 'moving' : null })"
                />
              </label>
              <div class="flex flex-wrap gap-6">
                <cth-knob
                  label="Threshold"
                  unit="%"
                  [min]="0"
                  [max]="100"
                  [defaultValue]="50"
                  [value]="threshold() * 100"
                  (valueChange)="patch({ threshold: $event / 100 })"
                />
                <cth-knob
                  label="Off delay"
                  unit="ms"
                  [min]="0"
                  [max]="5000"
                  [defaultValue]="0"
                  [value]="draft().offDelayMs ?? 0"
                  (valueChange)="patch({ offDelayMs: $event })"
                />
              </div>
              <span class="text-foreground-muted text-[11px]">Engaged {{ engagedWhen() }}.</span>
            </div>
          }
          <div
            class="border-hairline-strong rounded-lg border bg-(--well) p-1 shadow-[inset_0_1px_3px_rgb(0_0_0/0.6),0_1px_0_var(--highlight)]"
          >
            <xui-echart class="h-40 w-full" aria-label="Source to value" [option]="preview()" />
          </div>
        </div>
      </xui-dialog-body>
      <xui-dialog-footer align="between">
        <button xuiButton variant="ghost" color="error" type="button" [disabled]="!existing()" (click)="remove()">
          Remove
        </button>
        <div class="flex gap-2">
          <button xuiButton variant="ghost" type="button" (click)="open.set(false)">Cancel</button>
          <button xuiButton type="button" data-testid="modifier-save" (click)="save()">Save</button>
        </div>
      </xui-dialog-footer>
    </xui-dialog>
  `
})
export class ModifierDialog {
  private readonly live = inject(LiveStore);

  readonly open = model(false);
  readonly block = input.required<string>();
  readonly param = input.required<ParameterInfo>();

  protected readonly sources = [...ModifierSources];
  protected readonly curves = ModifierCurves.map(c => ({
    value: c,
    label: c === 'scurve' ? 'S-curve' : c.charAt(0).toUpperCase() + c.slice(1)
  }));
  /** All channels of the block, or one (the modifier then leaves the parameter alone on the others). */
  protected readonly scopes = [
    { value: 'all', label: 'All channels' },
    ...['A', 'B', 'C', 'D'].map(c => ({ value: c, label: c }))
  ];
  protected readonly sourceName = (s: string) => SourceNames[s] ?? `External ${s.slice(3)}`;
  protected readonly existing = computed(() => this.live.modifierOf(this.block(), this.param().id));
  protected readonly log = computed(() => this.param().taper === 'log');
  /** The block's engage switch (ModNames.EngageParam), which also has a mode, a threshold and an off delay. */
  protected readonly engage = computed(() => this.param().id === 'engage');
  protected readonly engageModes = EngageModes.map(m => ({ value: m, label: m === 'moving' ? 'Moving' : 'Position' }));
  protected readonly threshold = computed(() => this.draft().threshold ?? EngageDefaults.threshold);
  protected readonly engagedWhen = computed(() => engagedWhen(this.draft()));
  protected readonly draft = signal<PresetModifier>({
    block: '',
    param: '',
    source: 'lfo1',
    min: 0,
    max: 1,
    curve: 'linear',
    attackMs: 0,
    releaseMs: 0
  });

  protected readonly preview = computed<EChartsOption>(() => {
    const d = this.draft();
    const points = Array.from({ length: 41 }, (_, i) => {
      const s = shape(d.curve, i / 40);
      const value =
        this.log() && d.min > 0 && d.max > 0 ? d.min * Math.pow(d.max / d.min, s) : d.min + (d.max - d.min) * s;
      return [i / 40, value];
    });
    // The chart draws on a canvas, which cannot resolve CSS variables: the theme's colours as they stand now.
    const theme = themeColors();
    const label = { color: theme.label, fontSize: 10 };
    const grid = { lineStyle: { color: theme.line } };
    return {
      backgroundColor: 'transparent',
      grid: { left: 60, right: 14, top: 12, bottom: 22 },
      // Across: how far the source is through its range; up: the knob's value in its own unit.
      xAxis: {
        type: 'value',
        min: 0,
        max: 1,
        interval: 0.5,
        axisLabel: { ...label, formatter: (v: number) => `${Math.round(v * 100)} %` },
        axisLine: { show: false },
        axisTick: { show: false },
        splitLine: { show: true, ...grid }
      },
      yAxis: {
        type: this.log() && d.min > 0 && d.max > 0 ? 'log' : 'value',
        axisLabel: { ...label, formatter: (v: number) => formatValue(v, this.param().unit) },
        axisLine: { show: false },
        axisTick: { show: false },
        splitLine: grid
      },
      series: [
        {
          type: 'line',
          showSymbol: false,
          data: points,
          lineStyle: { color: theme.accent, width: 2 },
          itemStyle: { color: theme.accent },
          areaStyle: { color: theme.accent, opacity: 0.12 },
          // The engage threshold: the block is off below it.
          ...(this.engage()
            ? {
                markLine: {
                  silent: true,
                  symbol: 'none',
                  label: { formatter: 'threshold', position: 'insideEndTop', ...label },
                  lineStyle: { type: 'dashed', color: theme.label },
                  data: [{ yAxis: this.threshold() }]
                }
              }
            : {})
        }
      ]
    };
  });

  constructor() {
    // Seeded when the dialog opens, and only then: every state the store adopts is a fresh object, so tracking
    // existing() would throw the draft away on any rig event (a MIDI CC, another editor) while it is being edited.
    effect(() => {
      if (this.open()) {
        untracked(() => this.draft.set(structuredClone(this.existing() ?? this.blank())));
      }
    });
  }

  protected patch(change: Partial<PresetModifier>): void {
    this.draft.update(d => ({ ...d, ...change }));
  }

  protected async save(): Promise<void> {
    const draft = this.engage() ? sparseEngage(this.draft()) : this.draft();
    if (await this.live.putModifier(this.block(), this.param().id, draft)) {
      this.open.set(false);
    }
  }

  protected async remove(): Promise<void> {
    if (await this.live.putModifier(this.block(), this.param().id, null)) {
      this.open.set(false);
    }
  }

  /** A new modifier spanning the parameter's whole range; only called once the inputs are bound. */
  private blank(): PresetModifier {
    const p = this.param();
    return {
      block: this.block(),
      param: p.id,
      source: 'lfo1',
      min: p.min,
      max: p.max,
      curve: 'linear',
      attackMs: 0,
      releaseMs: 0
    };
  }
}
