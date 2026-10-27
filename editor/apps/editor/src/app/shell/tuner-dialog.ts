import { ChangeDetectionStrategy, Component, effect, inject, model } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { type TunerOutputs } from '@cathode/api-client';
import { CthTuner } from '@cathode/audio-ui';
import { XuiDialogImports } from '@xui/dialog';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
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

/**
 * The tuner. The engine only runs pitch detection while a client has it open: the tuner is rig-wide, so this only
 * tells the Server when this dialog opens or closes (never at startup), as this client, and another editor's stays on.
 * Below the needle, the global Tuner Output: what the outputs play while tuning (silence, the dry input, the preset).
 */
@Component({
  selector: 'cth-tuner-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CthTuner, XuiDialogImports, XuiSegmentedControlImports],
  template: `
    <xui-dialog title="Tuner" icon="matTuneRound" [(open)]="open">
      <xui-dialog-body>
        <cth-tuner class="py-4" [source]="reading" />
        @if (globals.settings(); as s) {
          <div class="flex items-center justify-center gap-2">
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
  protected readonly reading = () => this.events.latest?.tuner;
  protected readonly tunerOutputs = TunerOutputOptions;

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

  private send(on: boolean): void {
    void this.notify.run(async () => {
      await this.connection.client.PUT('/api/v1/tuner', {
        params: { query: { client: this.events.clientId } },
        body: { bypassed: !on }
      });
    });
  }
}
