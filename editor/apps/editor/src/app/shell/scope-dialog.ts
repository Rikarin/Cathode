import { ChangeDetectionStrategy, Component, effect, inject, model } from '@angular/core';
import { CthScope } from '@cathode/audio-ui';
import { XuiDialogImports } from '@xui/dialog';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';

/** The oscilloscope. The Server only streams scope frames while this is open. */
@Component({
    selector: 'cth-scope-dialog',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [CthScope, XuiDialogImports],
    template: `
        <xui-dialog title="Scope" icon="matShowChartRound" size="lg" [(open)]="open">
            <xui-dialog-body>
                <cth-scope class="bg-surface-sunken h-64 w-full rounded-lg" [source]="frame" />
                <p class="text-foreground-muted mt-2 text-xs">Output, left channel. Grid: ±0.5 full scale, 5 ms.</p>
            </xui-dialog-body>
        </xui-dialog>
    `
})
export class ScopeDialog {
    private readonly connection = inject(ConnectionService);
    private readonly events = inject(EventStream);
    private readonly notify = inject(Notify);

    readonly open = model(false);
    protected readonly frame = () => this.events.scope;

    constructor() {
        effect(() => {
            const enabled = this.open();
            if (!enabled) {
                this.events.scope = null;
            }

            void this.notify.run(async () => {
                await this.connection.client.PUT('/api/v1/scope', { body: { enabled } });
            });
        });
    }
}
