import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { type AssetInfo, unwrap } from '@cathode/api-client';
import { XuiButtonImports } from '@xui/button';
import { XuiCalloutImports } from '@xui/callout';
import { XuiInputImports } from '@xui/input';
import { XuiSelectImports } from '@xui/select';
import { XuiSwitchImports } from '@xui/switch';
import { XuiTableImports } from '@xui/table';
import { XuiTextImports } from '@xui/text';
import { ConnectionService } from '../core/connection';
import { Notify } from '../core/notify';
import { LibraryStore } from '../state/library.store';

interface MixRow {
  asset: AssetInfo | null;
  levelDb: number;
  delayMs: number;
  invert: boolean;
}

/**
 * Impulse responses: the IRs in the asset store, a Cab-Lab-style mixer that blends them into a new IR, and (in the
 * desktop app, on a real interface) measuring one with a sine sweep.
 */
@Component({
  selector: 'cth-irs-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    XuiButtonImports,
    XuiCalloutImports,
    XuiInputImports,
    XuiSelectImports,
    XuiSwitchImports,
    XuiTableImports,
    XuiTextImports
  ],
  host: { class: 'flex h-full flex-col gap-8 overflow-auto p-6' },
  template: `
    <section class="flex flex-col gap-3" aria-labelledby="mix-heading">
      <h1 xuiHeading id="mix-heading" class="text-lg">IR mixer</h1>
      <p class="text-foreground-muted max-w-2xl text-sm">
        Blend impulse responses (two mics on one cab, two cabs) into a new IR. Alignment moves each IR's peak to the
        same sample before its delay applies, so the blend starts phase-coherent.
      </p>
      <xui-table class="max-w-3xl">
        <xui-tr>
          <xui-th>IR</xui-th>
          <xui-th>Level (dB)</xui-th>
          <xui-th>Delay (ms)</xui-th>
          <xui-th>Invert</xui-th>
          <xui-th />
        </xui-tr>
        @for (row of rows(); track $index; let i = $index) {
          <xui-tr>
            <xui-td>
              <xui-select
                class="w-64"
                placeholder="Pick an IR"
                [attr.aria-label]="'IR ' + (i + 1)"
                [items]="library.impulseResponses()"
                [itemText]="name"
                [value]="row.asset"
                (valueChange)="edit(i, { asset: $event })"
              />
            </xui-td>
            <xui-td>
              <input
                xuiInput
                size="sm"
                type="number"
                step="0.5"
                class="w-24"
                [attr.aria-label]="'IR ' + (i + 1) + ' level'"
                [value]="row.levelDb"
                (change)="edit(i, { levelDb: +$any($event.target).value || 0 })"
              />
            </xui-td>
            <xui-td>
              <input
                xuiInput
                size="sm"
                type="number"
                step="0.01"
                min="0"
                class="w-24"
                [attr.aria-label]="'IR ' + (i + 1) + ' delay'"
                [value]="row.delayMs"
                (change)="edit(i, { delayMs: Math.max(0, +$any($event.target).value || 0) })"
              />
            </xui-td>
            <xui-td>
              <xui-switch
                [attr.aria-label]="'IR ' + (i + 1) + ' polarity invert'"
                [checked]="row.invert"
                (checkedChange)="edit(i, { invert: $event })"
              />
            </xui-td>
            <xui-td>
              <button xuiButton variant="ghost" size="sm" [disabled]="rows().length <= 1" (click)="remove(i)">
                Remove
              </button>
            </xui-td>
          </xui-tr>
        }
      </xui-table>
      <div class="flex flex-wrap items-end gap-4">
        <button xuiButton variant="outline" size="sm" [disabled]="rows().length >= 8" (click)="add()">Add IR</button>
        <label class="flex flex-col gap-1">
          <span class="text-foreground-muted text-xs">Name</span>
          <input
            xuiInput
            class="w-56"
            aria-label="Mix name"
            [value]="mixName()"
            (input)="mixName.set($any($event.target).value)"
          />
        </label>
        <label class="flex flex-col gap-1">
          <span class="text-foreground-muted text-xs">Length (ms)</span>
          <input
            xuiInput
            type="number"
            min="5"
            max="2000"
            class="w-24"
            aria-label="Mix length"
            [value]="lengthMs()"
            (change)="lengthMs.set(+$any($event.target).value || 170)"
          />
        </label>
        <label class="flex items-center gap-2 text-sm">
          <xui-switch aria-label="Align peaks" [checked]="align()" (checkedChange)="align.set($event)" />
          Align peaks
        </label>
        <label class="flex items-center gap-2 text-sm">
          <xui-switch aria-label="Normalise" [checked]="normalize()" (checkedChange)="normalize.set($event)" />
          Normalise
        </label>
        <button xuiButton data-testid="mix-irs" [disabled]="!canMix()" [loading]="mixing()" (click)="mix()">Mix</button>
      </div>
    </section>

    @if (desktop) {
      <section class="flex flex-col gap-3" aria-labelledby="capture-heading">
        <h2 xuiHeading id="capture-heading" class="text-base">Capture an IR</h2>
        <p class="text-foreground-muted max-w-2xl text-sm">
          Plays a sine sweep on outputs 1 and 2 and records input 1: cable the outputs to the power amp or speaker, and
          the mic to input 1. The engine stops while it measures. Start quiet: the sweep is loud.
        </p>
        <div class="flex flex-wrap items-end gap-4">
          <label class="flex flex-col gap-1">
            <span class="text-foreground-muted text-xs">Sweep (s)</span>
            <input
              xuiInput
              type="number"
              min="1"
              max="30"
              class="w-20"
              aria-label="Sweep length"
              [value]="sweepSeconds()"
              (change)="sweepSeconds.set(+$any($event.target).value || 5)"
            />
          </label>
          <label class="flex flex-col gap-1">
            <span class="text-foreground-muted text-xs">Level (dBFS)</span>
            <input
              xuiInput
              type="number"
              min="-60"
              max="0"
              class="w-20"
              aria-label="Sweep level"
              [value]="sweepLevel()"
              (change)="sweepLevel.set(+$any($event.target).value || -18)"
            />
          </label>
          <label class="flex flex-col gap-1">
            <span class="text-foreground-muted text-xs">IR length (ms)</span>
            <input
              xuiInput
              type="number"
              min="20"
              max="2000"
              class="w-24"
              aria-label="Captured IR length"
              [value]="captureLength()"
              (change)="captureLength.set(+$any($event.target).value || 500)"
            />
          </label>
          <button xuiButton [loading]="capturing()" (click)="capture()">Capture</button>
        </div>
        @if (captureOutput(); as output) {
          <xui-callout class="max-w-2xl" [color]="captureOk() ? 'success' : 'warning'">
            <pre class="text-xs whitespace-pre-wrap">{{ output }}</pre>
          </xui-callout>
        }
      </section>
    }

    <section class="flex flex-col gap-3" aria-labelledby="irs-heading">
      <h2 xuiHeading id="irs-heading" class="text-base">Impulse responses ({{ library.impulseResponses().length }})</h2>
      <ul class="flex max-w-3xl flex-col gap-1 text-sm">
        @for (ir of library.impulseResponses(); track ir.ref) {
          <li class="flex gap-4">
            <span class="flex-1 truncate">{{ ir.name }}</span>
            <span class="text-foreground-muted tabular-nums">{{ (ir.size / 1024).toFixed(0) }} KB</span>
            <span class="text-foreground-muted">{{ added(ir) }}</span>
          </li>
        } @empty {
          <li class="text-foreground-muted">No IRs yet: upload one in a Cab or IR Player block.</li>
        }
      </ul>
    </section>
  `
})
export class IrsPage {
  private readonly connection = inject(ConnectionService);
  private readonly notify = inject(Notify);
  protected readonly library = inject(LibraryStore);
  protected readonly desktop = this.connection.desktop;
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

  protected readonly sweepSeconds = signal(5);
  protected readonly sweepLevel = signal(-18);
  protected readonly captureLength = signal(500);
  protected readonly capturing = signal(false);
  protected readonly captureOutput = signal<string | null>(null);
  protected readonly captureOk = signal(false);

  protected readonly name = (asset: AssetInfo) => asset.name;
  protected readonly added = (asset: AssetInfo) => new Date(asset.added).toLocaleDateString();

  constructor() {
    void this.library.refreshAssets();
  }

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
            sources: this.rows().map(r => ({
              asset: r.asset!.ref,
              levelDb: r.levelDb,
              delayMs: r.delayMs,
              invert: r.invert
            })),
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
      this.notify.success(`Saved “${result.name}”: pick it in a Cab or IR Player block.`);
    }
  }

  protected async capture(): Promise<void> {
    this.capturing.set(true);
    const result = await this.notify.run(() =>
      this.desktop!.audio.irCapture({
        seconds: this.sweepSeconds(),
        levelDb: this.sweepLevel(),
        lengthMs: this.captureLength()
      })
    );
    this.capturing.set(false);
    this.captureOutput.set(result?.output ?? null);
    this.captureOk.set(!!result?.ok);
    if (result?.ok && result.wav) {
      const name = `Capture ${new Date().toISOString().slice(0, 19).replace('T', ' ').replaceAll(':', '.')}.wav`;
      const asset = await this.library.upload(new File([result.wav as BlobPart], name, { type: 'audio/wav' }));
      if (asset) {
        this.notify.success(`Saved “${asset.name}”.`);
      }
    }
  }

  private emptyRow(): MixRow {
    return { asset: null, levelDb: 0, delayMs: 0, invert: false };
  }
}
