import { ChangeDetectionStrategy, Component, effect, inject, model, signal } from '@angular/core';
import { CthRta, CthScope } from '@cathode/audio-ui';
import { XuiDialogImports } from '@xui/dialog';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { XuiSelectImports } from '@xui/select';
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
  imports: [CthRta, CthScope, XuiDialogImports, XuiSegmentedControlImports, XuiSelectImports],
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
          <div class="mb-3 flex flex-wrap items-end gap-4">
            <label class="flex flex-col gap-1">
              <span class="text-foreground-muted text-xs">Bands</span>
              <xui-select
                class="w-36"
                aria-label="RTA bands"
                [filterable]="false"
                [items]="resolutions"
                [itemText]="resolutionName"
                [value]="bandsPerOctave()"
                (valueChange)="$event && bandsPerOctave.set($event)"
              />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-foreground-muted text-xs">Decay</span>
              <xui-select
                class="w-28"
                aria-label="RTA decay"
                [filterable]="false"
                [items]="decays"
                [itemText]="decayName"
                [value]="decayMs()"
                (valueChange)="$event !== null && $event !== undefined && decayMs.set($event)"
              />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-foreground-muted text-xs">Window</span>
              <xui-select
                class="w-40"
                aria-label="RTA window"
                [filterable]="false"
                [items]="windows"
                [itemText]="windowName"
                [value]="window()"
                (valueChange)="$event && window.set($event)"
              />
            </label>
          </div>
          <cth-rta class="bg-surface-sunken h-64 w-full rounded-lg" [source]="rta" />
          <p class="text-foreground-muted mt-2 text-xs">
            {{ rtaSource() }}. Grid: 12 dB (0 dB = a full-scale sine); 100 Hz, 1 kHz, 10 kHz. Place an RTA block on the
            grid to look at any point of the chain.
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
  protected readonly resolutions = [3, 6, 12];
  protected readonly resolutionName = (n: number) =>
    ({ 3: 'Third-octave', 6: 'Sixth-octave', 12: '12th-octave' })[n] ?? `${n}/oct`;
  protected readonly decays = [0, 250, 500, 1000, 2000, 5000];
  protected readonly decayName = (ms: number) => (ms === 0 ? 'None' : ms < 1000 ? `${ms} ms` : `${ms / 1000} s`);
  protected readonly windows = ['hann', 'blackmanHarris', 'flatTop', 'rectangular'];
  protected readonly windowName = (w: string) =>
    ({ hann: 'Hann', blackmanHarris: 'Blackman-Harris', flatTop: 'Flat top', rectangular: 'Rectangular' })[w] ?? w;
  protected readonly bandsPerOctave = signal(6);
  protected readonly decayMs = signal(0);
  protected readonly window = signal('hann');
  /** Where the spectrum comes from: the output, or the preset's RTA block. */
  protected readonly rtaSource = () =>
    this.events.rta?.source === 'block' ? 'RTA block, wherever it sits on the grid' : 'Output, left channel';

  constructor() {
    effect(() => {
      const open = this.open();
      const view = this.view();
      const scope = open && view === 'scope';
      const rta = open && view === 'rta';
      const settings = { bandsPerOctave: this.bandsPerOctave(), decayMs: this.decayMs(), window: this.window() };
      if (!scope) {
        this.events.scope = null;
      }

      if (!rta) {
        this.events.rta = null;
      }

      void this.notify.run(async () => {
        await this.connection.client.PUT('/api/v1/scope', { body: { enabled: scope } });
        await this.connection.client.PUT('/api/v1/rta', { body: { enabled: rta, ...settings } });
      });
    });
  }
}
