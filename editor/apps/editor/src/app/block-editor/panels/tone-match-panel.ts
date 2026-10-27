import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import type { AssetInfo, PresetBlock } from '@cathode/api-client';
import { XuiButtonImports } from '@xui/button';
import { XuiCalloutImports } from '@xui/callout';
import { XuiSelectImports } from '@xui/select';
import { LibraryStore } from '../../state/library.store';
import { LiveStore } from '../../state/live.store';
import { AssetPicker } from './asset-picker';

/** The learn-time amounts the panel offers, in %. */
export const toneMatchAmounts = [25, 50, 75, 100];
/** The smoothing widths the panel offers, in octaves. */
export const toneMatchSmoothings = [1 / 6, 1 / 4, 1 / 3, 1 / 2, 1];

/** Names a smoothing width as a fraction of an octave: "1/3 octave", "1 octave". */
export function smoothingName(octaves: number): string {
  return octaves >= 1 ? '1 octave' : `1/${Math.round(1 / octaves)} octave`;
}

/** The body of a learn request, clamped to the ranges the Server accepts. */
export function toneMatchRequest(reference: string, seconds: number, amount: number, smoothing: number) {
  const clamp = (value: number, min: number, max: number) => Math.min(max, Math.max(min, value));
  return {
    reference,
    seconds: clamp(seconds, 3, 60),
    amount: clamp(amount, 0, 100),
    smoothing: clamp(smoothing, 0.1, 1)
  };
}

/**
 * Learning a Tone Match: pick a recording of the tone to match, press Learn and play for a while. The Server listens
 * to the rig's output, compares the two long-term spectra and stores the correction as the block's match. Amount and
 * Smoothing shape that correction as it is learnt.
 */
@Component({
  selector: 'cth-tone-match-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AssetPicker, XuiButtonImports, XuiCalloutImports, XuiSelectImports],
  template: `
    <div class="flex flex-col gap-3">
      <cth-asset-picker
        label="Reference recording"
        accept=".wav"
        [assets]="library.impulseResponses()"
        [reference]="reference()?.ref"
        (picked)="reference.set($event)"
      />
      <div class="flex flex-wrap items-end gap-3">
        <label class="flex flex-col gap-1">
          <span class="text-foreground-muted text-xs">Listen for</span>
          <xui-select
            class="w-32"
            aria-label="Listen for"
            [filterable]="false"
            [items]="durations"
            [itemText]="seconds"
            [value]="duration()"
            (valueChange)="$event && duration.set($event)"
          />
        </label>
        <label class="flex flex-col gap-1">
          <span class="text-foreground-muted text-xs">Amount</span>
          <xui-select
            class="w-28"
            aria-label="Amount"
            data-testid="tone-match-amount"
            [filterable]="false"
            [items]="amounts"
            [itemText]="percent"
            [value]="amount()"
            (valueChange)="$event && amount.set($event)"
          />
        </label>
        <label class="flex flex-col gap-1">
          <span class="text-foreground-muted text-xs">Smoothing</span>
          <xui-select
            class="w-32"
            aria-label="Smoothing"
            data-testid="tone-match-smoothing"
            [filterable]="false"
            [items]="smoothings"
            [itemText]="octaves"
            [value]="smoothing()"
            (valueChange)="$event && smoothing.set($event)"
          />
        </label>
        <button
          type="button"
          xuiButton
          data-testid="learn-tone-match"
          [disabled]="!reference() || listening()"
          (click)="learn()"
        >
          {{ listening() ? 'Listening… play now' : 'Learn' }}
        </button>
        <span class="text-foreground-muted text-xs" data-testid="match-state">{{ matchName() }}</span>
      </div>
      <xui-callout class="max-w-xl" minimal compact color="primary">
        Play the same kind of part as the reference, at playing level, until Tone Match stops listening. Learning again
        replaces the match rather than adding to it. Finer smoothing follows the reference more closely; wider smoothing
        keeps only its broad shape.
      </xui-callout>
    </div>
  `
})
export class ToneMatchPanel {
  protected readonly live = inject(LiveStore);
  protected readonly library = inject(LibraryStore);

  readonly block = input.required<PresetBlock>();

  protected readonly durations = [5, 10, 20, 30];
  protected readonly seconds = (s: number) => `${s} seconds`;
  protected readonly reference = signal<AssetInfo | null>(null);
  protected readonly duration = signal(10);
  /** How much of the learnt correction goes into the match (the block's Amount knob scales it again afterwards). */
  protected readonly amounts = toneMatchAmounts;
  protected readonly percent = (a: number) => `${a} %`;
  protected readonly amount = signal(100);
  /** The smoothing width, in octaves, within the 0.1 to 1 octave the Server accepts. */
  protected readonly smoothings = toneMatchSmoothings;
  protected readonly octaves = smoothingName;
  protected readonly smoothing = signal(1 / 3);
  protected readonly listening = signal(false);
  protected readonly matchName = computed(() => {
    const match = this.block().assets['match'];
    return match
      ? (this.library.assets().find(a => a.ref === match)?.name ?? 'Matched')
      : 'Not matched yet: the block passes the signal through.';
  });

  protected async learn(): Promise<void> {
    const reference = this.reference();
    if (!reference) {
      return;
    }

    this.listening.set(true);
    try {
      const id = this.block().id;
      await this.live.mutate(c =>
        c.POST('/api/v1/live/blocks/{id}/tonematch', {
          params: { path: { id } },
          body: toneMatchRequest(reference.ref, this.duration(), this.amount(), this.smoothing())
        })
      );
      await this.library.refreshAssets();
    } finally {
      this.listening.set(false);
    }
  }
}
