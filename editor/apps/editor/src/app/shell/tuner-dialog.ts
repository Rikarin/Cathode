import { ChangeDetectionStrategy, Component, computed, effect, inject, model } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { type TunerOutputs } from '@cathode/api-client';
import {
  Chromatic,
  CthTuner,
  MaxStrings,
  type TunerView,
  Tunings,
  findTuning,
  strings,
  tuneTo
} from '@cathode/audio-ui';
import { XuiDialogImports } from '@xui/dialog';
import { XuiInputImports } from '@xui/input';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { XuiSelectImports } from '@xui/select';
import { NumberSteppers } from '../controls/number-steppers';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';
import { GlobalStore } from '../state/global.store';

type TunerOutput = (typeof TunerOutputs)[number];

/** The global Tuner Output choices, as the tuner offers them. */
export const TunerOutputOptions: { label: string; value: TunerOutput }[] = [
  { label: 'Mute', value: 'mute' },
  { label: 'Bypass', value: 'bypass' },
  { label: 'Pass through', value: 'passThrough' }
];

/** The tuner's views. */
export const TunerViewOptions: { label: string; value: TunerView }[] = [
  { label: 'Needle', value: 'needle' },
  { label: 'Strobe', value: 'strobe' }
];

/** The tuner's A4 reference range in Hz (GlobalValidation on the Server). */
export const ReferenceRange = { min: 430, max: 450 } as const;
/** How far a string's offset may move it, in cents (GlobalValidation on the Server). */
export const OffsetRange = 20;

/**
 * The tuner. The engine only runs pitch detection while a client has it open: the tuner is rig-wide, so this only
 * tells the Server when this dialog opens or closes (never at startup), as this client, and another editor's stays on.
 * The view (needle or strobe), the tuning, the A4 reference, the strings' offsets and the global Tuner Output (what
 * the outputs play while tuning) are global settings. The target and cents are worked out here from the detected
 * frequency (`tuneTo`), so a change of tuning or offset shows at once, with no round trip to the engine.
 */
@Component({
  selector: 'cth-tuner-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CthTuner, NumberSteppers, XuiDialogImports, XuiInputImports, XuiSegmentedControlImports, XuiSelectImports],
  template: `
    <xui-dialog title="Tuner" icon="matTuneRound" [(open)]="open">
      <xui-dialog-body>
        <cth-tuner class="py-4" [source]="reading" [view]="view()" />
        @if (globals.settings(); as s) {
          <div class="grid grid-cols-[auto_minmax(0,1fr)] items-center justify-items-start gap-x-3 gap-y-2">
            <span class="text-foreground-muted text-xs" id="tuner-view-label">View</span>
            <xui-segmented-control
              size="sm"
              aria-labelledby="tuner-view-label"
              data-testid="tuner-view"
              [options]="views"
              [value]="view()"
              (valueChange)="setView($event)"
            />

            <span class="text-foreground-muted text-xs" id="tuner-tuning-label">Tuning</span>
            <div class="flex items-center gap-3">
              <xui-select
                class="w-44"
                aria-labelledby="tuner-tuning-label"
                data-testid="tuner-tuning"
                [filterable]="false"
                [items]="tuningIds"
                [itemText]="tuningName"
                [value]="tuning()?.id ?? chromatic"
                (valueChange)="setTuning($event)"
              />
              <label class="text-foreground-muted flex items-center gap-1 text-xs">
                A4
                <input
                  #referenceField
                  xuiInput
                  size="sm"
                  type="number"
                  cthSteppers
                  class="w-20"
                  step="0.1"
                  data-testid="tuner-reference"
                  aria-label="Reference, A4 in hertz"
                  [min]="referenceRange.min"
                  [max]="referenceRange.max"
                  [value]="s.tunerReference"
                  (change)="setReference(referenceField)"
                />
                Hz
              </label>
            </div>

            @if (tuning(); as t) {
              <span class="text-foreground-muted text-xs" id="tuner-offsets-label">Offsets</span>
              <xui-segmented-control
                size="sm"
                aria-labelledby="tuner-offsets-label"
                data-testid="tuner-offsets"
                [options]="offsetModes"
                [value]="s.tunerOffsetsOn ? 'custom' : 'none'"
                (valueChange)="setOffsetsOn($event === 'custom')"
              />
              @if (s.tunerOffsetsOn) {
                <span></span>
                <div class="flex flex-wrap gap-2" data-testid="tuner-string-offsets">
                  @for (string of tuningStrings(); track string.number) {
                    <label class="text-foreground-muted flex flex-col items-center gap-0.5 text-xs">
                      {{ string.number }} · {{ string.note }}{{ string.octave }}
                      <input
                        #offsetField
                        xuiInput
                        size="sm"
                        type="number"
                        cthSteppers
                        class="w-14 text-center"
                        step="0.5"
                        [min]="-offsetRange"
                        [max]="offsetRange"
                        [attr.aria-label]="'String ' + string.number + ' offset in cents'"
                        [value]="s.tunerOffsets[string.number - 1] ?? 0"
                        (change)="setOffset(string.number, offsetField)"
                      />
                    </label>
                  }
                </div>
              }
            }

            <span class="text-foreground-muted text-xs" id="tuner-output-label">Output while tuning</span>
            <xui-segmented-control
              size="sm"
              aria-labelledby="tuner-output-label"
              data-testid="tuner-output"
              [options]="tunerOutputs"
              [value]="s.tunerOutput"
              (valueChange)="setTunerOutput($event)"
            />
          </div>
        }
      </xui-dialog-body>
    </xui-dialog>
  `
})
export class TunerDialog {
  private readonly connection = inject(ConnectionService);
  private readonly events = inject(EventStream);
  private readonly notify = inject(Notify);
  protected readonly globals = inject(GlobalStore);

  readonly open = model(false);
  protected readonly tunerOutputs = TunerOutputOptions;
  protected readonly views = TunerViewOptions;
  protected readonly offsetModes = [
    { label: 'None', value: 'none' },
    { label: 'Custom', value: 'custom' }
  ];
  protected readonly chromatic = Chromatic;
  protected readonly tuningIds = [Chromatic, ...Tunings.map(t => t.id)];
  protected readonly tuningName = (id: string) => (id === Chromatic ? 'Chromatic' : (findTuning(id)?.name ?? id));
  protected readonly referenceRange = ReferenceRange;
  protected readonly offsetRange = OffsetRange;

  protected readonly view = computed<TunerView>(() =>
    this.globals.settings()?.tunerView === 'strobe' ? 'strobe' : 'needle'
  );
  /** The tuning targeted; undefined for chromatic, and for a tuning this editor does not know. */
  protected readonly tuning = computed(() => findTuning(this.globals.settings()?.tuning));
  protected readonly tuningStrings = computed(() => {
    const t = this.tuning();
    return t ? strings(t) : [];
  });

  /** The latest reading against the global tuning, reference and offsets; polled on animation frames. */
  protected readonly reading = () => {
    const detected = this.events.latest?.tuner;
    if (!detected) {
      return null;
    }

    const s = this.globals.settings();
    const offsets = s?.tunerOffsetsOn ? s.tunerOffsets : undefined;
    return tuneTo(detected.frequency, s?.tunerReference ?? 440, this.tuning(), offsets);
  };

  /** What this client last told the Server; nothing yet means off. */
  private sent = false;

  constructor() {
    effect(() => {
      const on = this.open();
      if (on !== this.sent) {
        this.sent = on;
        this.send(on);
      }
    });
    // A reconnect means the Server released what this client had on (its socket closed): turn it on again.
    this.events.connected$.pipe(takeUntilDestroyed()).subscribe(() => {
      if (this.sent) {
        this.send(true);
      }
    });
  }

  /** Stores the global Tuner Output; the engine applies it at once while the tuner is on. */
  protected setTunerOutput(value: string | null | undefined): void {
    if (value && value !== this.globals.settings()?.tunerOutput) {
      void this.globals.update(g => (g.tunerOutput = value));
    }
  }

  protected setView(value: string | null | undefined): void {
    if (value && value !== this.globals.settings()?.tunerView) {
      void this.globals.update(g => (g.tunerView = value));
    }
  }

  protected setTuning(id: string | null | undefined): void {
    if (id && id !== this.globals.settings()?.tuning) {
      void this.globals.update(g => (g.tuning = id));
    }
  }

  /** Stores the A4 reference typed, clamped to its range to a tenth of a hertz; the field shows what is stored. */
  protected setReference(field: HTMLInputElement): void {
    const current = this.globals.settings()?.tunerReference ?? 440;
    const typed = field.valueAsNumber;
    const value = Number.isFinite(typed)
      ? Math.round(Math.min(ReferenceRange.max, Math.max(ReferenceRange.min, typed)) * 10) / 10
      : current;
    field.value = String(value);
    if (value !== current) {
      void this.globals.update(g => (g.tunerReference = value));
    }
  }

  protected setOffsetsOn(on: boolean): void {
    if (on !== this.globals.settings()?.tunerOffsetsOn) {
      void this.globals.update(g => (g.tunerOffsetsOn = on));
    }
  }

  /** Stores string `number`'s offset (1 for the highest), clamped to ±{@link OffsetRange} cents in half cents. */
  protected setOffset(number: number, field: HTMLInputElement): void {
    const offsets = this.globals.settings()?.tunerOffsets ?? [];
    const current = offsets[number - 1] ?? 0;
    const typed = field.valueAsNumber;
    const value = Number.isFinite(typed)
      ? Math.round(Math.min(OffsetRange, Math.max(-OffsetRange, typed)) * 2) / 2
      : current;
    field.value = String(value);
    if (value !== current) {
      void this.globals.update(g => {
        const next = Array.from({ length: MaxStrings }, (_, i) => g.tunerOffsets[i] ?? 0);
        next[number - 1] = value;
        g.tunerOffsets = next;
      });
    }
  }

  private send(on: boolean): void {
    void this.notify.run(async () => {
      await this.connection.client.PUT('/api/v1/tuner', {
        params: { query: { client: this.events.clientId } },
        body: { bypassed: !on }
      });
    });
  }
}
