import { ChangeDetectionStrategy, Component, effect, inject, model } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CthTuner } from '@cathode/audio-ui';
import { XuiDialogImports } from '@xui/dialog';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';

/**
 * The tuner. The engine only runs pitch detection while a client has it open: the tuner is rig-wide, so this only
 * tells the Server when this dialog opens or closes (never at startup), as this client, and another editor's stays on.
 */
@Component({
  selector: 'cth-tuner-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CthTuner, XuiDialogImports],
  template: `
    <xui-dialog title="Tuner" icon="matTuneRound" [(open)]="open">
      <xui-dialog-body>
        <cth-tuner class="py-4" [source]="reading" />
      </xui-dialog-body>
    </xui-dialog>
  `
})
export class TunerDialog {
  private readonly connection = inject(ConnectionService);
  private readonly events = inject(EventStream);
  private readonly notify = inject(Notify);

  readonly open = model(false);
  protected readonly reading = () => this.events.latest?.tuner;

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

  private send(on: boolean): void {
    void this.notify.run(async () => {
      await this.connection.client.PUT('/api/v1/tuner', {
        params: { query: { client: this.events.clientId } },
        body: { bypassed: !on }
      });
    });
  }
}
