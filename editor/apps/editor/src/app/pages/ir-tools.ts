import { ChangeDetectionStrategy, Component, Injectable, computed, inject, model, signal } from '@angular/core';
import { type AssetInfo, unwrap } from '@cathode/api-client';
import { XuiButtonImports } from '@xui/button';
import { XuiCalloutImports } from '@xui/callout';
import { XuiDialogImports } from '@xui/dialog';
import { XuiInputImports } from '@xui/input';
import { XuiSelectImports } from '@xui/select';
import { XuiSwitchImports } from '@xui/switch';
import { XuiTableImports } from '@xui/table';
import { ConnectionService } from '../core/connection';
import { Notify } from '../core/notify';
import { type IrImportOptions, LibraryStore } from '../state/library.store';

interface Choice<T> {
  value: T;
  label: string;
}

type ProcessingChoice = Choice<NonNullable<IrImportOptions['processing']>>;
const Processing: readonly [ProcessingChoice, ...ProcessingChoice[]] = [
  { value: 'asIs', label: 'As is' },
  { value: 'autoTrim', label: 'Auto-trim (remove leading silence)' },
  { value: 'minimumPhase', label: 'Minimum phase' }
];

type ChannelsChoice = Choice<NonNullable<IrImportOptions['channels']>>;
const Channels: readonly [ChannelsChoice, ...ChannelsChoice[]] = [
  { value: 'both', label: 'Both' },
  { value: 'left', label: 'Left' },
  { value: 'right', label: 'Right' },
  { value: 'sum', label: 'Sum (L+R)' }
];

const choiceLabel = (choice: Choice<string>) => choice.label;

/** How .wav IRs are processed when they are uploaded to the library; kept for the session. */
@Injectable({ providedIn: 'root' })
export class IrImportSettings {
  readonly processing = signal(Processing[0]);
  readonly channels = signal(Channels[0]);
  readonly polarity = signal(true);
  readonly maxLengthMs = signal(0);

  /** The settings as the Server's upload takes them. */
  options(): IrImportOptions {
    const maxLengthMs = this.maxLengthMs();
    return {
      processing: this.processing().value,
      channels: this.channels().value,
      polarity: this.polarity(),
      ...(maxLengthMs >= 5 ? { maxLengthMs } : {})
    };
  }
}

/** Edits {@link IrImportSettings}: trim or minimum phase, polarity, a stereo file's channel, a maximum length. */
@Component({
  selector: 'cth-ir-import-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [XuiButtonImports, XuiDialogImports, XuiInputImports, XuiSelectImports, XuiSwitchImports],
  template: `
    <xui-dialog title="IR import settings" class="w-[min(32rem,calc(100vw-2rem))] max-w-none" [(open)]="open">
      <xui-dialog-body class="flex flex-col gap-4">
        <p class="text-foreground-muted text-sm">
          Applied to .wav files as they are uploaded. Auto-trim removes the silence before the IR starts; minimum phase
          keeps its frequency response and moves its energy as early as it can go, so IRs line up without a delay.
          Polarity makes each IR's main peak positive, so blended IRs do not cancel.
        </p>
        <label class="flex flex-col gap-1">
          <span class="text-foreground-muted text-xs">Processing</span>
          <xui-select
            aria-label="Import processing"
            [items]="processingChoices"
            [itemText]="choiceLabel"
            [value]="settings.processing()"
            (valueChange)="$event && settings.processing.set($event)"
          />
        </label>
        <div class="flex flex-wrap items-end gap-4">
          <label class="flex flex-col gap-1">
            <span class="text-foreground-muted text-xs">Stereo files</span>
            <xui-select
              class="w-36"
              aria-label="Stereo channels"
              [items]="channelChoices"
              [itemText]="choiceLabel"
              [value]="settings.channels()"
              (valueChange)="$event && settings.channels.set($event)"
            />
          </label>
          <label class="flex flex-col gap-1">
            <span class="text-foreground-muted text-xs">Max length (ms, 0 = all)</span>
            <input
              #maxLengthMsField
              xuiInput
              type="number"
              min="0"
              max="10000"
              class="w-28"
              aria-label="Maximum IR length"
              [value]="settings.maxLengthMs()"
              (change)="settings.maxLengthMs.set(Math.max(0, +maxLengthMsField.value || 0))"
            />
          </label>
          <label class="flex items-center gap-2 pb-2 text-sm">
            <xui-switch
              aria-label="Correct polarity"
              [checked]="settings.polarity()"
              (checkedChange)="settings.polarity.set($event)"
            />
            Correct polarity
          </label>
        </div>
      </xui-dialog-body>
      <xui-dialog-footer>
        <button xuiButton type="button" (click)="open.set(false)">Done</button>
      </xui-dialog-footer>
    </xui-dialog>
  `
})
export class IrImportDialog {
  readonly open = model(false);
  protected readonly settings = inject(IrImportSettings);
  protected readonly processingChoices = Processing;
  protected readonly channelChoices = Channels;
  protected readonly choiceLabel = choiceLabel;
  protected readonly Math = Math;
}

interface MixRow {
  asset: AssetInfo | null;
  levelDb: number;
  delayMs: number;
  invert: boolean;
}

/**
 * A Cab-Lab-style mixer: blends impulse responses (two mics on one cab, two cabs) into a new IR in the library.
 * Alignment moves each IR's peak to the same sample before its delay applies, so the blend starts phase-coherent.
 */
@Component({
  selector: 'cth-ir-mixer-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [XuiButtonImports, XuiDialogImports, XuiInputImports, XuiSelectImports, XuiSwitchImports, XuiTableImports],
  template: `
    @if (open()) {
      <xui-dialog title="Mix IRs" class="w-[min(48rem,calc(100vw-2rem))] max-w-none" [(open)]="open">
        <xui-dialog-body class="flex flex-col gap-4">
          <p class="text-foreground-muted text-sm">
            Blend impulse responses (two mics on one cab, two cabs) into a new IR. Alignment moves each IR's peak to the
            same sample before its delay applies, so the blend starts phase-coherent.
          </p>
          <xui-table>
            <xui-tr>
              <xui-th class="min-w-0 flex-1">IR</xui-th>
              <xui-th class="w-28">Level (dB)</xui-th>
              <xui-th class="w-28">Delay (ms)</xui-th>
              <xui-th class="w-16">Invert</xui-th>
              <xui-th class="w-24" />
            </xui-tr>
            @for (row of rows(); track $index; let i = $index) {
              <xui-tr>
                <xui-td class="min-w-0 flex-1">
                  <xui-select
                    class="w-full"
                    placeholder="Pick an IR"
                    [attr.aria-label]="'IR ' + (i + 1)"
                    [items]="library.impulseResponses()"
                    [itemText]="name"
                    [value]="row.asset"
                    (valueChange)="edit(i, { asset: $event })"
                  />
                </xui-td>
                <xui-td class="w-28">
                  <input
                    #levelDbField
                    xuiInput
                    size="sm"
                    type="number"
                    step="0.5"
                    class="w-24"
                    [attr.aria-label]="'IR ' + (i + 1) + ' level'"
                    [value]="row.levelDb"
                    (change)="edit(i, { levelDb: +levelDbField.value || 0 })"
                  />
                </xui-td>
                <xui-td class="w-28">
                  <input
                    #delayMsField
                    xuiInput
                    size="sm"
                    type="number"
                    step="0.01"
                    min="0"
                    class="w-24"
                    [attr.aria-label]="'IR ' + (i + 1) + ' delay'"
                    [value]="row.delayMs"
                    (change)="edit(i, { delayMs: Math.max(0, +delayMsField.value || 0) })"
                  />
                </xui-td>
                <xui-td class="w-16">
                  <xui-switch
                    [attr.aria-label]="'IR ' + (i + 1) + ' polarity invert'"
                    [checked]="row.invert"
                    (checkedChange)="edit(i, { invert: $event })"
                  />
                </xui-td>
                <xui-td class="w-24 justify-end">
                  <button
                    type="button"
                    xuiButton
                    variant="ghost"
                    size="sm"
                    [disabled]="rows().length <= 1"
                    (click)="remove(i)"
                  >
                    Remove
                  </button>
                </xui-td>
              </xui-tr>
            }
          </xui-table>
          <div class="flex flex-wrap items-end gap-4">
            <button type="button" xuiButton variant="outline" size="sm" [disabled]="rows().length >= 8" (click)="add()">
              Add IR
            </button>
            <label class="flex flex-col gap-1">
              <span class="text-foreground-muted text-xs">Name</span>
              <input
                #mixNameField
                xuiInput
                class="w-56"
                aria-label="Mix name"
                [value]="mixName()"
                (input)="mixName.set(mixNameField.value)"
              />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-foreground-muted text-xs">Length (ms)</span>
              <input
                #lengthMsField
                xuiInput
                type="number"
                min="5"
                max="2000"
                class="w-24"
                aria-label="Mix length"
                [value]="lengthMs()"
                (change)="lengthMs.set(+lengthMsField.value || 170)"
              />
            </label>
            <label class="flex items-center gap-2 pb-2 text-sm">
              <xui-switch aria-label="Align peaks" [checked]="align()" (checkedChange)="align.set($event)" />
              Align peaks
            </label>
            <label class="flex items-center gap-2 pb-2 text-sm">
              <xui-switch aria-label="Normalise" [checked]="normalize()" (checkedChange)="normalize.set($event)" />
              Normalise
            </label>
          </div>
        </xui-dialog-body>
        <xui-dialog-footer>
          <button xuiButton variant="ghost" type="button" (click)="open.set(false)">Close</button>
          <button
            type="button"
            xuiButton
            data-testid="mix-irs"
            [disabled]="!canMix()"
            [loading]="mixing()"
            (click)="mix()"
          >
            Mix
          </button>
        </xui-dialog-footer>
      </xui-dialog>
    }
  `
})
export class IrMixerDialog {
  readonly open = model(false);
  private readonly connection = inject(ConnectionService);
  private readonly notify = inject(Notify);
  protected readonly library = inject(LibraryStore);
  protected readonly Math = Math;

  protected readonly rows = signal<MixRow[]>([this.emptyRow(), this.emptyRow()]);
  protected readonly mixName = signal('IR mix');
  protected readonly lengthMs = signal(170);
  protected readonly align = signal(true);
  protected readonly normalize = signal(true);
  protected readonly mixing = signal(false);
  protected readonly canMix = computed(
    () => this.rows().every(r => r.asset !== null) && this.mixName().trim().length > 0
  );
  protected readonly name = (asset: AssetInfo) => asset.name;

  protected edit(index: number, change: Partial<MixRow>): void {
    this.rows.update(rows => rows.map((row, i) => (i === index ? { ...row, ...change } : row)));
  }

  protected add(): void {
    this.rows.update(rows => [...rows, this.emptyRow()]);
  }

  protected remove(index: number): void {
    this.rows.update(rows => rows.filter((_, i) => i !== index));
  }

  protected async mix(): Promise<void> {
    this.mixing.set(true);
    const result = await this.notify.run(async () =>
      unwrap(
        await this.connection.client.POST('/api/v1/assets/mix', {
          body: {
            name: this.mixName().trim(),
            // canMix (the button's guard) holds every row to a file.
            sources: this.rows().flatMap(({ asset, ...r }) =>
              asset === null
                ? []
                : {
                    asset: asset.ref,
                    levelDb: r.levelDb,
                    delayMs: r.delayMs,
                    invert: r.invert
                  }
            ),
            align: this.align(),
            lengthMs: this.lengthMs(),
            sampleRate: 48_000,
            normalize: this.normalize()
          }
        })
      )
    );
    this.mixing.set(false);
    if (result) {
      await this.library.refreshAssets();
      this.notify.success(`Saved “${result.name}” to the library.`);
      this.open.set(false);
    }
  }

  private emptyRow(): MixRow {
    return { asset: null, levelDb: 0, delayMs: 0, invert: false };
  }
}

/**
 * Measures an IR with a sine sweep (desktop app, real interface): plays it on outputs 1 and 2, records input 1, and
 * adds the result to the library.
 */
@Component({
  selector: 'cth-ir-capture-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [XuiButtonImports, XuiCalloutImports, XuiDialogImports, XuiInputImports],
  template: `
    <xui-dialog title="Capture an IR" class="w-[min(36rem,calc(100vw-2rem))] max-w-none" [(open)]="open">
      <xui-dialog-body class="flex flex-col gap-4">
        <p class="text-foreground-muted text-sm">
          Plays a sine sweep on outputs 1 and 2 and records input 1: cable the outputs to the power amp or speaker, and
          the mic to input 1. The engine stops while it measures. Start quiet: the sweep is loud.
        </p>
        <div class="flex flex-wrap items-end gap-4">
          <label class="flex flex-col gap-1">
            <span class="text-foreground-muted text-xs">Sweep (s)</span>
            <input
              #sweepSecondsField
              xuiInput
              type="number"
              min="1"
              max="30"
              class="w-20"
              aria-label="Sweep length"
              [value]="sweepSeconds()"
              (change)="sweepSeconds.set(+sweepSecondsField.value || 5)"
            />
          </label>
          <label class="flex flex-col gap-1">
            <span class="text-foreground-muted text-xs">Level (dBFS)</span>
            <input
              #sweepLevelField
              xuiInput
              type="number"
              min="-60"
              max="0"
              class="w-20"
              aria-label="Sweep level"
              [value]="sweepLevel()"
              (change)="sweepLevel.set(+sweepLevelField.value || -18)"
            />
          </label>
          <label class="flex flex-col gap-1">
            <span class="text-foreground-muted text-xs">IR length (ms)</span>
            <input
              #captureLengthField
              xuiInput
              type="number"
              min="20"
              max="2000"
              class="w-24"
              aria-label="Captured IR length"
              [value]="captureLength()"
              (change)="captureLength.set(+captureLengthField.value || 500)"
            />
          </label>
        </div>
        @if (output(); as text) {
          <xui-callout [color]="ok() ? 'success' : 'warning'">
            <pre class="text-xs whitespace-pre-wrap">{{ text }}</pre>
          </xui-callout>
        }
      </xui-dialog-body>
      <xui-dialog-footer>
        <button xuiButton variant="ghost" type="button" (click)="open.set(false)">Close</button>
        <button type="button" xuiButton [loading]="capturing()" (click)="capture()">Capture</button>
      </xui-dialog-footer>
    </xui-dialog>
  `
})
export class IrCaptureDialog {
  readonly open = model(false);
  private readonly connection = inject(ConnectionService);
  private readonly notify = inject(Notify);
  private readonly library = inject(LibraryStore);

  protected readonly sweepSeconds = signal(5);
  protected readonly sweepLevel = signal(-18);
  protected readonly captureLength = signal(500);
  protected readonly capturing = signal(false);
  protected readonly output = signal<string | null>(null);
  protected readonly ok = signal(false);

  protected async capture(): Promise<void> {
    const desktop = this.connection.desktop;
    if (!desktop) {
      return;
    }

    this.capturing.set(true);
    const result = await this.notify.run(() =>
      desktop.audio.irCapture({
        seconds: this.sweepSeconds(),
        levelDb: this.sweepLevel(),
        lengthMs: this.captureLength()
      })
    );
    this.capturing.set(false);
    this.output.set(result?.output ?? null);
    this.ok.set(!!result?.ok);
    if (result?.ok && result.wav) {
      const name = `Capture ${new Date().toISOString().slice(0, 19).replace('T', ' ').replaceAll(':', '.')}.wav`;
      const asset = await this.library.upload(new File([result.wav as BlobPart], name, { type: 'audio/wav' }));
      if (asset) {
        this.notify.success(`Saved “${asset.name}” to the library.`);
      }
    }
  }
}
