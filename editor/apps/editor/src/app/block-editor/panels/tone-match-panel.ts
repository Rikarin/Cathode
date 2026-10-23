import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import type { AssetInfo, PresetBlock } from '@cathode/api-client';
import { XuiButtonImports } from '@xui/button';
import { XuiCalloutImports } from '@xui/callout';
import { XuiSelectImports } from '@xui/select';
import { LibraryStore } from '../../state/library.store';
import { LiveStore } from '../../state/live.store';
import { AssetPicker } from './asset-picker';

/**
 * Learning a Tone Match: pick a recording of the tone to match, press Learn and play for a while. The Server listens
 * to the rig's output, compares the two long-term spectra and stores the correction as the block's match.
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
        <button xuiButton data-testid="learn-tone-match" [disabled]="!reference() || listening()" (click)="learn()">
          {{ listening() ? 'Listening… play now' : 'Learn' }}
        </button>
        <span class="text-foreground-muted text-xs" data-testid="match-state">{{ matchName() }}</span>
      </div>
      <xui-callout class="max-w-xl" minimal compact color="primary">
        Play the same kind of part as the reference, at playing level, until Tone Match stops listening. Learning again
        replaces the match rather than adding to it.
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
          body: { reference: reference.ref, seconds: this.duration(), amount: 100, smoothing: 1 / 3 }
        })
      );
      await this.library.refreshAssets();
    } finally {
      this.listening.set(false);
    }
  }
}
