import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { XuiButtonImports } from '@xui/button';
import { XuiInputImports } from '@xui/input';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { ConnectionService } from '../core/connection';
import type { ServerTarget } from '../core/desktop';
import { Notify } from '../core/notify';

/**
 * Which Server the desktop app uses: its own engine on this computer, or a remote one (a headless rig) by URL and
 * access token. Choosing reloads the window on the new Server. It needs only the desktop bridge, not a Server, so the
 * startup screen offers it when the Server in use cannot be reached.
 */
@Component({
  selector: 'cth-server-target',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [XuiButtonImports, XuiInputImports, XuiSegmentedControlImports],
  host: { class: 'flex flex-col items-start gap-4' },
  template: `
    <xui-segmented-control
      size="sm"
      aria-label="Server mode"
      [options]="modeOptions"
      [value]="mode()"
      (valueChange)="mode.set($event ?? 'local')"
    />
    @if (mode() === 'remote') {
      <div class="flex flex-wrap items-end gap-3">
        <label class="flex flex-col gap-1">
          <span class="text-foreground-muted text-xs">URL</span>
          <input
            xuiInput
            class="w-72"
            placeholder="http://192.168.1.20:5180"
            aria-label="Server URL"
            [value]="remoteUrl()"
            (input)="remoteUrl.set($any($event.target).value)"
          />
        </label>
        <label class="flex flex-col gap-1">
          <span class="text-foreground-muted text-xs">Access token</span>
          <input
            xuiInput
            type="password"
            class="w-72"
            aria-label="Access token"
            [value]="remoteToken()"
            (input)="remoteToken.set($any($event.target).value)"
          />
        </label>
      </div>
    }
    <div>
      <button xuiButton type="button" variant="outline" (click)="connect()">
        {{ mode() === 'remote' ? 'Connect' : 'Use this computer' }}
      </button>
    </div>
  `
})
export class ServerTargetForm {
  private readonly desktop = inject(ConnectionService).desktop;
  private readonly notify = inject(Notify);

  protected readonly mode = signal<'local' | 'remote'>('local');
  protected readonly remoteUrl = signal('');
  protected readonly remoteToken = signal('');
  protected readonly modeOptions = [
    { label: 'This computer', value: 'local' as const },
    { label: 'Remote', value: 'remote' as const }
  ];

  constructor() {
    void this.load();
  }

  protected async connect(): Promise<void> {
    const target: ServerTarget =
      this.mode() === 'remote'
        ? { mode: 'remote', baseUrl: this.remoteUrl().trim().replace(/\/+$/, ''), token: this.remoteToken() }
        : { mode: 'local' };
    await this.notify.run(() => this.desktop!.setTarget(target));
  }

  private async load(): Promise<void> {
    const target = await this.notify.run(() => this.desktop!.target());
    if (target) {
      this.mode.set(target.mode);
      if (target.mode === 'remote') {
        this.remoteUrl.set(target.baseUrl);
        this.remoteToken.set(target.token);
      }
    }
  }
}
