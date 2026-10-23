import { ChangeDetectionStrategy, Component, effect, inject, model, signal } from '@angular/core';
import { CthRta, CthScope } from '@cathode/audio-ui';
import { XuiDialogImports } from '@xui/dialog';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';

type View = 'scope' | 'rta';

/**
 * The output analysers: the oscilloscope and the RTA. The Server streams only the frames of the view on show, and
 * nothing while the dialog is closed.
 */
@Component({
    selector: 'cth-scope-dialog',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [CthRta, CthScope, XuiDialogImports, XuiSegmentedControlImports],
    template: `
        <xui-dialog title="Analyser" icon="matShowChartRound" size="lg" [(open)]="open">
            <xui-dialog-body>
                <xui-segmented-control
                    class="mb-3"
                    size="sm"
                    aria-label="Analyser view"
                    [options]="views"
                    [value]="view()"
                    (valueChange)="$event && view.set($event)"
                />
                @if (view() === 'scope') {
                    <cth-scope class="bg-surface-sunken h-64 w-full rounded-lg" [source]="scope" />
                    <p class="text-foreground-muted mt-2 text-xs">Output, left channel. Grid: ±0.5 full scale, 5 ms.</p>
                } @else {
                    <cth-rta class="bg-surface-sunken h-64 w-full rounded-lg" [source]="rta" />
                    <p class="text-foreground-muted mt-2 text-xs">
                        Output, left channel, sixth-octave bands. Grid: 12 dB (0 dB = a full-scale sine); 100 Hz, 1 kHz,
                        10 kHz.
                    </p>
                }
            </xui-dialog-body>
        </xui-dialog>
    `
})
export class ScopeDialog {
    private readonly connection = inject(ConnectionService);
    private readonly events = inject(EventStream);
    private readonly notify = inject(Notify);

    readonly open = model(false);
    protected readonly view = signal<View>('scope');
    protected readonly views = [
        { label: 'Scope', value: 'scope' as View },
        { label: 'RTA', value: 'rta' as View }
    ];
    protected readonly scope = () => this.events.scope;
    protected readonly rta = () => this.events.rta;

    constructor() {
        effect(() => {
            const open = this.open();
            const view = this.view();
            const scope = open && view === 'scope';
            const rta = open && view === 'rta';
            if (!scope) {
                this.events.scope = null;
            }

            if (!rta) {
                this.events.rta = null;
            }

            void this.notify.run(async () => {
                await this.connection.client.PUT('/api/v1/scope', { body: { enabled: scope } });
                await this.connection.client.PUT('/api/v1/rta', { body: { enabled: rta } });
            });
        });
    }
}
