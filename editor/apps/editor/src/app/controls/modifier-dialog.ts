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
import { ModifierCurves, ModifierSources, type ParameterInfo, type PresetModifier } from '@cathode/api-client';
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

/**
 * Edits the modifier on one parameter (docs/02-architecture.md §5.4): which controller drives it, over what range,
 * through which curve, with what damping. The chart previews source to value.
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
          <xui-echart class="h-40 w-full" aria-label="Source to value" [option]="preview()" />
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
    return {
      grid: { left: 64, right: 12, top: 12, bottom: 28 },
      xAxis: { type: 'value', min: 0, max: 1, name: 'source', nameGap: 4 },
      yAxis: {
        type: this.log() && d.min > 0 && d.max > 0 ? 'log' : 'value',
        axisLabel: { formatter: (v: number) => formatValue(v, this.param().unit) }
      },
      series: [{ type: 'line', showSymbol: false, data: points }]
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
    if (await this.live.putModifier(this.block(), this.param().id, this.draft())) {
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
