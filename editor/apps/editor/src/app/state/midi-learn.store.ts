import { DestroyRef, Injectable, effect, inject, signal, untracked } from '@angular/core';
import { type MidiLearnRequest, unwrap } from '@cathode/api-client';
import { XuiToastService } from '@xui/toast';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';
import { actionText } from '../pages/footswitches';

/**
 * What an armed MIDI learn waits to map, for people: "bypass delay1", or "amp1 gain (External 2)" for a knob's learn
 * (the parameter the External will drive).
 */
export function learnText(request: MidiLearnRequest): string {
  const modifier = request.modifier;
  return modifier
    ? `${modifier.block} ${modifier.param} (External ${request.value ?? 1})`
    : actionText({ action: request.action, target: request.target ?? null, value: request.value ?? null });
}

/**
 * MIDI learn as the Server has it armed: read once the event stream connects, on every reconnect and whenever any editor arms or cancels it
 * (`midiLearnChanged`), so a reload or another editor still shows it. While armed, a toast that stays says what the next
 * control moved will map, with a Cancel button.
 */
@Injectable({ providedIn: 'root' })
export class MidiLearnStore {
  private readonly connection = inject(ConnectionService);
  private readonly notify = inject(Notify);
  private readonly toaster = inject(XuiToastService);

  /** The armed request, or null. */
  readonly armed = signal<MidiLearnRequest | null>(null);
  private toast: number | null = null;

  constructor() {
    const events = inject(EventStream);
    const subscriptions = [
      events.connected$.subscribe(() => void this.refresh()),
      events.midi$.subscribe(m => {
        if (m.type === 'midiLearned') {
          this.armed.set(null);
        } else if (m.type === 'midiLearnChanged') {
          void this.refresh();
        }
      })
    ];
    inject(DestroyRef).onDestroy(() => subscriptions.forEach(s => s.unsubscribe()));
    effect(() => {
      const armed = this.armed();
      untracked(() => this.showToast(armed));
    });
    // No read here: the store starts with the shell, before the connection is made. The stream's first `connected`
    // reads it, as every reconnect does.
  }

  async refresh(): Promise<void> {
    await this.notify.run(async () => {
      const armed = unwrap(await this.connection.client.GET('/api/v1/midi/learn'));
      this.armed.set(armed ?? null);
    });
  }

  /** Arms MIDI learn (replacing a request already armed); resolves to whether the Server took it. */
  async arm(request: MidiLearnRequest): Promise<boolean> {
    const done = await this.notify.run(async () => {
      await this.connection.client.PUT('/api/v1/midi/learn', { body: request });
      this.armed.set(request);
      return true;
    });
    return done ?? false;
  }

  /** Disarms MIDI learn: nothing is mapped, and a knob's learn leaves its knob as it was. */
  async cancel(): Promise<void> {
    await this.notify.run(async () => {
      await this.connection.client.DELETE('/api/v1/midi/learn');
      this.armed.set(null);
    });
  }

  private showToast(armed: MidiLearnRequest | null): void {
    if (this.toast !== null) {
      this.toaster.dismiss(this.toast);
      this.toast = null;
    }

    if (armed) {
      this.toast = this.toaster.show({
        message: `MIDI learn: move a pedal, knob or switch on your MIDI controller to map ${learnText(armed)}.`,
        color: 'primary',
        timeout: 0,
        actionText: 'Cancel',
        onAction: () => void this.cancel()
      });
    }
  }
}
