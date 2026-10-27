import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { XuiButtonImports } from '@xui/button';
import { XuiCalloutImports } from '@xui/callout';
import { XuiInputImports } from '@xui/input';
import { XuiSwitchImports } from '@xui/switch';
import { ConnectionService } from '../core/connection';
import type { NetworkStatus } from '../core/desktop';
import { Notify } from '../core/notify';

/**
 * Control from other devices: the desktop app's own Server also listens on the network, serves the web editor there
 * and wants an access token, so an iPad on the same Wi-Fi edits the same live rig in Safari. Off by default. Shows the
 * addresses to open, the token and a QR code that opens the first address already signed in. Changing it restarts the
 * Server (the audio keeps playing) and reloads the window.
 */
@Component({
  selector: 'cth-network-access',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [XuiButtonImports, XuiCalloutImports, XuiInputImports, XuiSwitchImports],
  host: { class: 'flex flex-col items-start gap-4' },
  template: `
    @if (status(); as s) {
      <div class="flex items-center gap-3">
        <xui-switch
          aria-labelledby="network-access-label"
          data-testid="network-access"
          [checked]="s.enabled"
          [disabled]="applying()"
          (checkedChange)="apply($event, port())"
        />
        <span id="network-access-label" class="text-sm">Allow control from other devices on this network</span>
      </div>
      <p class="text-foreground-muted max-w-2xl text-xs">
        Opens the editor to a tablet, phone or another computer on the same Wi-Fi: they edit the same live rig as this
        window, at the same time. Each needs the access token below once; the QR code brings it along. Turn this on only
        on a network you trust, such as your home Wi-Fi: anyone there who has the token can change the rig, and the
        connection is not encrypted. The first time, macOS may ask whether cathode-server may accept incoming network
        connections: choose Allow.
      </p>
      <div class="flex flex-wrap items-end gap-3">
        <label class="flex flex-col gap-1">
          <span class="text-foreground-muted text-xs">Port</span>
          <input
            #portField
            xuiInput
            type="number"
            min="1024"
            max="65535"
            class="w-28"
            aria-label="Network port"
            [value]="port()"
            (input)="port.set(+portField.value)"
          />
        </label>
        @if (s.enabled && port() !== s.port) {
          <button xuiButton type="button" variant="outline" [loading]="applying()" (click)="apply(true, port())">
            Use this port
          </button>
        }
      </div>
      @if (s.error) {
        <xui-callout color="error" title="Not on the network">{{ s.error }}</xui-callout>
      }
      @if (s.enabled && !s.error) {
        <div class="flex flex-wrap items-start gap-6" data-testid="network-access-details">
          @if (s.qr; as qr) {
            <svg
              class="size-40 shrink-0 rounded-sm bg-white p-2"
              role="img"
              [attr.viewBox]="'0 0 ' + qr.size + ' ' + qr.size"
              [attr.aria-label]="'QR code for ' + qr.url"
              shape-rendering="crispEdges"
            >
              <path fill="#000" [attr.d]="qr.path" />
            </svg>
          }
          <div class="flex min-w-0 flex-col gap-3 text-sm">
            <div class="flex flex-col gap-1">
              <span class="text-foreground-muted text-xs">Open in the other device's browser</span>
              @for (url of s.urls; track url) {
                <span class="font-mono select-all" data-testid="network-url">{{ url }}</span>
              } @empty {
                <span>This computer is on no network yet. Join the Wi-Fi, then reopen this page.</span>
              }
            </div>
            <div class="flex flex-col gap-1">
              <span class="text-foreground-muted text-xs">Access token</span>
              <div class="flex flex-wrap items-center gap-2">
                <span class="font-mono break-all select-all" data-testid="network-token">{{ s.token }}</span>
                <button xuiButton type="button" size="sm" variant="ghost" (click)="copy(s.token)">Copy</button>
              </div>
            </div>
            <p class="text-foreground-muted max-w-md text-xs">
              Scan the code with the iPad's camera, or open an address and paste the token when the editor asks. In
              Safari, Share ▸ Add to Home Screen gives it a full-screen icon. A new token signs every other device out.
            </p>
            <div>
              <button
                xuiButton
                type="button"
                size="sm"
                variant="outline"
                [loading]="applying()"
                (click)="apply(true, s.port, true)"
              >
                New token
              </button>
            </div>
          </div>
        </div>
      }
    }
  `
})
export class NetworkAccess {
  private readonly network = inject(ConnectionService).desktop?.network ?? null;
  private readonly notify = inject(Notify);

  protected readonly status = signal<NetworkStatus | null>(null);
  protected readonly port = signal(5180);
  protected readonly applying = signal(false);

  constructor() {
    void this.load();
  }

  protected async apply(enabled: boolean, port: number, renewToken = false): Promise<void> {
    const network = this.network;
    if (!network) {
      return;
    }

    this.applying.set(true);
    try {
      // The window reloads once the Server is back; until then the switch shows the new state.
      const next = await this.notify.run(() => network.apply({ enabled, port, renewToken }));
      if (next) {
        this.status.set(next);
        this.port.set(next.port);
      } else {
        await this.load(); // refused: show what is really in use
      }
    } finally {
      this.applying.set(false);
    }
  }

  protected async copy(text: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(text);
      this.notify.success('Copied the access token.');
    } catch (error) {
      this.notify.error(error);
    }
  }

  private async load(): Promise<void> {
    const network = this.network;
    if (!network) {
      return;
    }

    const status = await this.notify.run(() => network.status());
    if (status) {
      this.status.set(status);
      this.port.set(status.port);
    }
  }
}
