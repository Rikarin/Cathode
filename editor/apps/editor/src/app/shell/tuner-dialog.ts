import { ChangeDetectionStrategy, Component, effect, inject, model } from '@angular/core';
import { CthTuner } from '@cathode/audio-ui';
import { XuiDialogImports } from '@xui/dialog';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';

/** The tuner. The engine only runs pitch detection while this is open. */
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

    constructor() {
        effect(() => {
            const on = this.open();
            void this.notify.run(async () => {
                await this.connection.client.PUT('/api/v1/tuner', { body: { bypassed: !on } });
            });
        });
    }
}
