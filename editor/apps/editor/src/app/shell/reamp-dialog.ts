import { ChangeDetectionStrategy, Component, inject, model, signal } from '@angular/core';
import { problemError } from '@cathode/api-client';
import { XuiButtonImports } from '@xui/button';
import { XuiDialogImports } from '@xui/dialog';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { ConnectionService } from '../core/connection';
import { Notify } from '../core/notify';

/** The file a reamp of `name` is saved as: “DI take 3 (reamped).wav”. */
export function reampedName(name: string): string {
  return `${name.replace(/\.wav$/i, '')} (reamped).wav`;
}

/**
 * Reamp: plays a recorded DI track (a WAV file) through the preset as it is now, unsaved edits and the active scene
 * included, and saves what comes out of Output 1. The Server renders it offline, faster than real time and without
 * the interface, so the rig keeps playing meanwhile. The tail is silence added after the recording for delays and
 * reverbs to ring out.
 */
@Component({
  selector: 'cth-reamp-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [XuiButtonImports, XuiDialogImports, XuiSegmentedControlImports],
  template: `
    <xui-dialog title="Reamp a recording" class="w-[min(30rem,calc(100vw-2rem))] max-w-none" [(open)]="open">
      <xui-dialog-body class="flex flex-col gap-4">
        <p class="text-foreground-muted text-sm">
          Choose a DI recording (a WAV file). It is played into Input 1 of this preset, on the scene you are on, and
          Output 1 is saved as a new WAV file that lines up with the recording. The rig keeps playing meanwhile.
        </p>
        <div class="flex min-w-0 items-center gap-3">
          <button xuiButton type="button" variant="outline" size="sm" (click)="recording.click()">Choose file…</button>
          <span class="min-w-0 flex-1 truncate text-sm" data-testid="reamp-file" [title]="file()?.name ?? ''">
            {{ file()?.name ?? 'No file chosen' }}
          </span>
          <input
            #recording
            type="file"
            class="hidden"
            accept=".wav,audio/wav"
            data-testid="reamp-input"
            (change)="choose(recording)"
          />
        </div>
        <div class="flex items-center gap-3">
          <span class="text-foreground-muted text-xs">Tail</span>
          <xui-segmented-control
            size="sm"
            aria-label="Tail after the recording"
            [options]="tails"
            [value]="tail()"
            (valueChange)="$event !== null && tail.set($event)"
          />
        </div>
      </xui-dialog-body>
      <xui-dialog-footer>
        <button xuiButton variant="ghost" type="button" (click)="open.set(false)">Close</button>
        <button
          xuiButton
          type="button"
          data-testid="reamp-render"
          [disabled]="!file() || rendering()"
          [loading]="rendering()"
          (click)="render()"
        >
          Render
        </button>
      </xui-dialog-footer>
    </xui-dialog>
  `
})
export class ReampDialog {
  readonly open = model(false);

  private readonly connection = inject(ConnectionService);
  private readonly notify = inject(Notify);

  protected readonly file = signal<File | null>(null);
  protected readonly rendering = signal(false);
  /** Seconds of silence after the recording. */
  protected readonly tail = signal(2);
  protected readonly tails = [
    { label: 'None', value: 0 },
    { label: '2 s', value: 2 },
    { label: '5 s', value: 5 },
    { label: '10 s', value: 10 }
  ];

  protected choose(input: HTMLInputElement): void {
    this.file.set(input.files?.[0] ?? null);
    input.value = '';
  }

  /** Sends the recording to the Server and saves the render it answers with. */
  protected async render(): Promise<void> {
    const file = this.file();
    if (!file || this.rendering()) {
      return;
    }

    this.rendering.set(true);
    try {
      await this.notify.run(async () => {
        const connection = this.connection.connection();
        if (!connection) {
          throw new Error('Not connected to a Server yet.');
        }

        // A plain fetch: the answer is a file, and the typed client reads JSON.
        const response = await fetch(`${connection.baseUrl}/api/v1/live/render?tail=${this.tail()}`, {
          method: 'POST',
          headers: {
            'Content-Type': 'audio/wav',
            ...(connection.token ? { Authorization: `Bearer ${connection.token}` } : {})
          },
          body: file
        });
        if (!response.ok) {
          throw await problemError(response);
        }

        const name = reampedName(file.name);
        const link = document.createElement('a');
        link.href = URL.createObjectURL(await response.blob());
        link.download = name;
        link.click();
        URL.revokeObjectURL(link.href);
        this.notify.success(`Rendered “${name}”.`);
      });
    } finally {
      this.rendering.set(false);
    }
  }
}
