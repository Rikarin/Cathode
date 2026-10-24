import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, signal } from '@angular/core';
import { type PresetBlock, unwrap } from '@cathode/api-client';
import { XuiButtonImports } from '@xui/button';
import { XuiCalloutImports } from '@xui/callout';
import { ConnectionService } from '../../core/connection';
import { Notify } from '../../core/notify';
import { LiveStore } from '../../state/live.store';
import { resonancePercent, sweepFrequency, sweepPosition } from './cab-resonance';

/**
 * The cab resonance helper on the Circuit Amp (docs/08-blocks-parity.md §2): a sine plays in place of the amp's output,
 * through the cab and whatever follows, while you sweep it to where the cabinet booms loudest; Apply writes that
 * frequency into Load resonance, so the amp's speaker load resonates where the cab does. The tone lives in the engine
 * only: nothing is added to the preset, and closing the helper (or leaving the block) stops it.
 */
@Component({
  selector: 'cth-cab-resonance-helper',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [XuiButtonImports, XuiCalloutImports],
  template: `
    @if (!open()) {
      <div>
        <button xuiButton variant="outline" size="sm" type="button" data-testid="cab-resonance" (click)="start()">
          Find the cab resonance…
        </button>
      </div>
    } @else {
      <section
        class="border-border bg-surface-raised flex max-w-2xl flex-col gap-3 rounded-xl border p-4"
        aria-label="Cab resonance helper"
      >
        <p class="text-foreground-muted text-xs">
          A sine now plays instead of the amp, through the cab and the rest of the chain. Turn your monitors to a modest
          level and sweep slowly: where the low end booms loudest is the cab's resonance.
        </p>
        <label class="flex items-center gap-3 text-sm">
          <span class="text-foreground-muted w-20 text-xs">Frequency</span>
          <input
            class="flex-1"
            type="range"
            min="0"
            max="1000"
            aria-label="Test tone frequency"
            [value]="position()"
            (input)="sweep(+$any($event.target).value / 1000)"
          />
          <span class="w-16 text-right tabular-nums" data-testid="tone-frequency">{{ frequency().toFixed(0) }} Hz</span>
        </label>
        <label class="flex items-center gap-3 text-sm">
          <span class="text-foreground-muted w-20 text-xs">Level</span>
          <input
            class="flex-1"
            type="range"
            min="-48"
            max="-6"
            aria-label="Test tone level"
            [value]="level()"
            (input)="setLevel(+$any($event.target).value)"
          />
          <span class="w-16 text-right tabular-nums">{{ level() }} dB</span>
        </label>
        <p class="text-foreground-muted text-xs">
          Load resonance {{ percent().toFixed(0) }} % (the speaker load resonates at {{ baseHz().toFixed(0) }} Hz at
          100 %).
        </p>
        <div class="flex gap-2">
          <button xuiButton size="sm" type="button" data-testid="apply-resonance" (click)="apply()">
            Apply as Load resonance
          </button>
          <button xuiButton size="sm" variant="ghost" type="button" (click)="stop()">Cancel</button>
        </div>
      </section>
    }
  `
})
export class CabResonanceHelper {
  private readonly connection = inject(ConnectionService);
  private readonly live = inject(LiveStore);
  private readonly notify = inject(Notify);

  readonly block = input.required<PresetBlock>();

  protected readonly open = signal(false);
  protected readonly frequency = signal(100);
  protected readonly position = signal(sweepPosition(100) * 1000);
  protected readonly level = signal(-24);
  protected readonly baseHz = signal(100);
  protected readonly percent = signal(100);
  private playingFor: string | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => void this.silence());
  }

  protected async start(): Promise<void> {
    const id = this.block().id;
    const info = await this.notify.run(async () =>
      unwrap(
        await this.connection.client.GET('/api/v1/live/blocks/{id}/speaker-resonance', { params: { path: { id } } })
      )
    );
    if (!info) {
      return;
    }

    this.baseHz.set(info.baseHz);
    this.frequency.set(info.hz);
    this.position.set(sweepPosition(info.hz) * 1000);
    this.percent.set(info.percent);
    this.open.set(true);
    this.playingFor = id;
    await this.send(true);
  }

  protected sweep(position: number): void {
    const hz = sweepFrequency(position);
    this.position.set(position * 1000);
    this.frequency.set(hz);
    this.percent.set(resonancePercent(hz, this.baseHz()));
    void this.send(true);
  }

  protected setLevel(db: number): void {
    this.level.set(db);
    void this.send(true);
  }

  protected async apply(): Promise<void> {
    const block = this.block();
    this.live.setParam(
      block.id,
      this.live.channelOf(block.id),
      'spkres',
      resonancePercent(this.frequency(), this.baseHz())
    );
    await this.stop();
    this.notify.success(`Load resonance set for ${this.frequency().toFixed(0)} Hz.`);
  }

  protected async stop(): Promise<void> {
    this.open.set(false);
    await this.silence();
  }

  private async silence(): Promise<void> {
    if (this.playingFor) {
      const id = this.playingFor;
      this.playingFor = null;
      await this.connection.client.PUT('/api/v1/live/blocks/{id}/test-tone', {
        params: { path: { id } },
        body: { enabled: false, frequency: this.frequency(), level: this.level() }
      });
    }
  }

  private async send(enabled: boolean): Promise<void> {
    const id = this.playingFor;
    if (!id) {
      return;
    }

    await this.notify.run(async () =>
      unwrap(
        await this.connection.client.PUT('/api/v1/live/blocks/{id}/test-tone', {
          params: { path: { id } },
          body: { enabled, frequency: this.frequency(), level: this.level() }
        })
      )
    );
  }
}
