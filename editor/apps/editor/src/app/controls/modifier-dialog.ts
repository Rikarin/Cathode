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
  ModifierOperations,
  ModifierSources,
  ModifierUpdateRates,
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
  manualE: 'Manual E',
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

/** The response settings a modifier has when it stores none (Cathode.Engine.Rig.ModShape.Identity). */
export const ResponseDefaults = { start: 0, mid: 0.5, end: 1, slope: 0.5, scale: 1, offset: 0 };

type Response = Pick<PresetModifier, 'start' | 'mid' | 'end' | 'slope' | 'scale' | 'offset'>;

const clamp01 = (v: number) => Math.min(1, Math.max(0, v));

/**
 * A modifier's response to its source, 0..1 to 0..1, as the engine applies it before the curve
 * (Cathode.Engine.Rig.ModShape.Apply): Slope bends the source around its centre, Start/Mid/End set the output at
 * rest, half way and full travel, then Scale multiplies and Offset adds.
 */
export function respond(m: Response, source: number): number {
  const r = {
    start: m.start ?? ResponseDefaults.start,
    mid: m.mid ?? ResponseDefaults.mid,
    end: m.end ?? ResponseDefaults.end,
    slope: m.slope ?? ResponseDefaults.slope,
    scale: m.scale ?? ResponseDefaults.scale,
    offset: m.offset ?? ResponseDefaults.offset
  };
  const k = (0.5 - r.slope) * 1.8;
  const u = 2 * clamp01(source) - 1;
  const x = 0.5 + (0.5 * (u - k * u)) / (k - 2 * k * Math.abs(u) + 1);
  const y = x < 0.5 ? r.start + (r.mid - r.start) * 2 * x : r.mid + (r.end - r.mid) * (2 * x - 1);
  return clamp01(y * r.scale + r.offset);
}

/** Whether a modifier source is an external controller (External 1-16): the ones PC Reset waits for. */
export const isExternal = (source: string | null | undefined): boolean => source?.startsWith('ext') === true;

/**
 * A modifier as it is stored: a second source's operation and scale only with a second source, and the source
 * scales, the response settings, the update rate and PC Reset only where they differ from the defaults (PC Reset only
 * with an external source, the one thing it acts on), so a plain modifier stays as presets have always stored it.
 */
export function sparseResponse(m: PresetModifier): PresetModifier {
  const { source2, operation, scale1, scale2, updateRate, pcReset, start, mid, end, slope, scale, offset, ...rest } = m;
  const given = { start, mid, end, slope, scale, offset };
  const kept = Object.fromEntries(
    Object.entries(given).filter(
      ([key, value]) => value != null && value !== ResponseDefaults[key as keyof typeof ResponseDefaults]
    )
  );
  return {
    ...rest,
    ...(scale1 != null && scale1 !== 1 ? { scale1 } : {}),
    ...(source2
      ? {
          source2,
          ...(operation === 'multiply' || operation === 'subtract' ? { operation } : {}),
          ...(scale2 != null && scale2 !== 1 ? { scale2 } : {})
        }
      : {}),
    ...kept,
    ...(updateRate === 'medium' || updateRate === 'slow' ? { updateRate } : {}),
    ...(pcReset === true && (isExternal(m.source) || isExternal(source2)) ? { pcReset } : {})
  };
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
export function engagedWhen(m: Pick<PresetModifier, 'mode' | 'threshold' | 'offDelayMs' | 'offAbove'>): string {
  const percent = `${Math.round((m.threshold ?? EngageDefaults.threshold) * 100)} %`;
  const delay = Math.round(m.offDelayMs ?? EngageDefaults.offDelayMs);
  // With the off region above the threshold the block is engaged at the threshold and below it.
  const [on, off] = m.offAbove ? ['below', 'above'] : ['above', 'below'];
  if (m.mode === 'moving') {
    return `as soon as it moves, off ${delay > 0 ? `after ${delay} ms` : 'once back'} ${off} ${percent}`;
  }

  return delay > 0 ? `at ${percent} and ${on}, off after ${delay} ms ${off} it` : `at ${percent} and ${on}`;
}

/** An engage modifier as it is stored: settings at their defaults are left out, as a preset stores them. */
export function sparseEngage(m: PresetModifier): PresetModifier {
  const { mode, threshold, offDelayMs, offAbove, ...rest } = m;
  return {
    ...rest,
    ...(mode === 'moving' ? { mode } : {}),
    ...(threshold != null && threshold !== EngageDefaults.threshold ? { threshold } : {}),
    ...(offDelayMs != null && offDelayMs > 0 ? { offDelayMs } : {}),
    ...(offAbove === true ? { offAbove } : {})
  };
}

/**
 * Edits the modifier on one parameter (docs/02-architecture.md §5.4): which controller drives it (or which two,
 * each with its own scale, added, multiplied or subtracted), with what response (Start/Mid/End, Slope, Scale,
 * Offset), over what range, through which curve, with what damping, how often it writes the parameter (Update Rate)
 * and, for an external controller, whether the stored value holds after a preset load until the controller moves
 * (PC Reset). The chart previews source to value. On a block's engage switch it also sets how the value engages the
 * block: by position or by movement, the threshold, which side of it is off, and the off delay.
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
          <div class="flex flex-wrap items-end gap-3">
            <label class="flex min-w-48 flex-1 flex-col gap-1">
              <span class="text-foreground-muted text-xs">Source</span>
              <xui-select
                aria-label="Modifier source"
                [items]="sources"
                [itemText]="sourceName"
                [value]="draft().source"
                (valueChange)="patch({ source: $event ?? 'lfo1' })"
              />
            </label>
            <cth-knob
              label="Source scale"
              unit="%"
              data-testid="modifier-scale1"
              [min]="0"
              [max]="100"
              [defaultValue]="100"
              [value]="(draft().scale1 ?? 1) * 100"
              (valueChange)="patch({ scale1: $event / 100 })"
            />
          </div>
          <div class="flex flex-wrap items-end gap-3">
            <label class="flex min-w-48 flex-1 flex-col gap-1">
              <span class="text-foreground-muted text-xs">Second source</span>
              <xui-select
                aria-label="Second modifier source"
                data-testid="modifier-source2"
                [items]="secondSources"
                [itemText]="secondSourceName"
                [value]="draft().source2 ?? 'none'"
                (valueChange)="patch({ source2: $event === 'none' || $event === null ? null : $event })"
              />
            </label>
            @if (draft().source2) {
              <xui-segmented-control
                size="sm"
                aria-label="How the two sources combine"
                data-testid="modifier-operation"
                [options]="operations"
                [value]="draft().operation ?? 'add'"
                (valueChange)="patch({ operation: $event === 'multiply' || $event === 'subtract' ? $event : null })"
              />
              <cth-knob
                label="Source 2 scale"
                unit="%"
                data-testid="modifier-scale2"
                [min]="0"
                [max]="100"
                [defaultValue]="100"
                [value]="(draft().scale2 ?? 1) * 100"
                (valueChange)="patch({ scale2: $event / 100 })"
              />
            }
          </div>
          <div class="flex flex-wrap gap-6" data-testid="modifier-response">
            @for (knob of responseKnobs; track knob.key) {
              <cth-knob
                [label]="knob.label"
                unit="%"
                [min]="knob.min"
                [max]="knob.max"
                [defaultValue]="knob.default * 100"
                [value]="(draft()[knob.key] ?? knob.default) * 100"
                (valueChange)="patchResponse(knob.key, $event / 100)"
              />
            }
          </div>
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
          @if (!engage()) {
            <label class="flex items-center gap-3">
              <span class="text-foreground-muted text-xs">Update rate</span>
              <xui-segmented-control
                size="sm"
                aria-label="How often the modifier writes the parameter"
                data-testid="modifier-update-rate"
                [options]="updateRates"
                [value]="draft().updateRate ?? 'fast'"
                (valueChange)="patch({ updateRate: $event === 'medium' || $event === 'slow' ? $event : null })"
              />
            </label>
          }
          @if (external()) {
            <label class="flex items-center gap-3">
              <span class="text-foreground-muted text-xs">After a preset load</span>
              <xui-segmented-control
                size="sm"
                aria-label="PC Reset: what the parameter does when the preset loads"
                data-testid="modifier-pc-reset"
                [options]="pcResets"
                [value]="draft().pcReset ? 'hold' : 'follow'"
                (valueChange)="patch({ pcReset: $event === 'hold' ? true : null })"
              />
            </label>
          }
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
              <label class="flex items-center gap-3">
                <span class="text-foreground-muted text-xs">Off</span>
                <xui-segmented-control
                  size="sm"
                  aria-label="Which side of the threshold bypasses the block"
                  data-testid="engage-off-side"
                  [options]="offSides"
                  [value]="draft().offAbove ? 'above' : 'below'"
                  (valueChange)="patch({ offAbove: $event === 'above' ? true : null })"
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
  protected readonly secondSources = ['none', ...ModifierSources];
  protected readonly secondSourceName = (s: string) => (s === 'none' ? 'None' : this.sourceName(s));
  protected readonly operations = ModifierOperations.map(o => ({
    value: o,
    label: o.charAt(0).toUpperCase() + o.slice(1)
  }));
  /** Fast writes the parameter every control block; the slower rates cost less and step more coarsely. */
  protected readonly updateRates = ModifierUpdateRates.map(r => ({
    value: r,
    label: r.charAt(0).toUpperCase() + r.slice(1)
  }));
  /** PC Reset: follow the controller's last value at once, or hold the stored value until the controller moves. */
  protected readonly pcResets = [
    { value: 'follow', label: 'Follow the controller' },
    { value: 'hold', label: 'Hold the stored value until it moves' }
  ];
  /** The side of the engage threshold on which the block is bypassed. */
  protected readonly offSides = [
    { value: 'below', label: 'Below the threshold' },
    { value: 'above', label: 'Above the threshold' }
  ];
  /** Whether a source is an external controller: PC Reset acts on those only. */
  protected readonly external = computed(() => isExternal(this.draft().source) || isExternal(this.draft().source2));
  /** The response settings, in percent; each at its default leaves the source as it is. */
  protected readonly responseKnobs = [
    { key: 'start', label: 'Start', min: 0, max: 100, default: ResponseDefaults.start },
    { key: 'mid', label: 'Mid', min: 0, max: 100, default: ResponseDefaults.mid },
    { key: 'end', label: 'End', min: 0, max: 100, default: ResponseDefaults.end },
    { key: 'slope', label: 'Slope', min: 0, max: 100, default: ResponseDefaults.slope },
    { key: 'scale', label: 'Scale', min: 0, max: 1000, default: ResponseDefaults.scale },
    { key: 'offset', label: 'Offset', min: -100, max: 100, default: ResponseDefaults.offset }
  ] as const;
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
      const s = shape(d.curve, respond(d, i / 40));
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
          // The engage threshold: the block is off on one side of it.
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

  protected patchResponse(key: keyof typeof ResponseDefaults, value: number): void {
    this.patch({ [key]: value });
  }

  protected async save(): Promise<void> {
    const draft = sparseResponse(this.engage() ? sparseEngage(this.draft()) : this.draft());
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
