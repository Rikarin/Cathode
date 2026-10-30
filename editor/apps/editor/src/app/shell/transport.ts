import {
  ChangeDetectionStrategy,
  Component,
  type ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild
} from '@angular/core';
import { type LooperActions, type LooperInfo } from '@cathode/api-client';
import { onAnimationFrame } from '@cathode/audio-ui';
import { provideIcons } from '@ng-icons/core';
import {
  matDownloadRound,
  matFiberManualRecordRound,
  matLoopRound,
  matPlayArrowRound,
  matRepeatRound,
  matSlowMotionVideoRound,
  matStopRound,
  matSwapHorizRound,
  matTimerRound,
  matUndoRound
} from '@ng-icons/material-icons/round';
import { XuiButtonImports } from '@xui/button';
import { XuiDialogImports } from '@xui/dialog';
import { XuiIconImports } from '@xui/icon';
import { XuiInputImports } from '@xui/input';
import { XuiTagImports } from '@xui/tag';
import { XuiTooltipImports } from '@xui/tooltip';
import { NumberSteppers } from '../controls/number-steppers';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';
import { GlobalStore } from '../state/global.store';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';

const LooperColors: Record<string, 'error' | 'warning' | 'success' | 'none'> = {
  recording: 'error',
  armed: 'warning',
  overdubbing: 'warning',
  playing: 'success'
};

/**
 * Tempo, tap, metronome and the looper. Tempo and looper state come from telemetry (animation frames), so they show
 * what the engine is doing, including taps from a footswitch or MIDI clock.
 */
@Component({
  selector: 'cth-transport',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NumberSteppers,
    XuiButtonImports,
    XuiDialogImports,
    XuiIconImports,
    XuiInputImports,
    XuiTagImports,
    XuiTooltipImports
  ],
  providers: [
    provideIcons({
      matDownloadRound,
      matFiberManualRecordRound,
      matLoopRound,
      matPlayArrowRound,
      matRepeatRound,
      matSlowMotionVideoRound,
      matStopRound,
      matSwapHorizRound,
      matTimerRound,
      matUndoRound
    })
  ],
  host: { class: 'flex items-center gap-1' },
  template: `
    @if (editing()) {
      <input
        #tempoField
        xuiInput
        size="sm"
        type="number"
        cthSteppers
        class="w-20"
        aria-label="Tempo in BPM"
        [value]="tempo()"
        (keydown.enter)="setTempo(tempoField.value)"
        (keydown.escape)="editing.set(false)"
        (blur)="setTempo(tempoField.value)"
      />
    } @else {
      <button
        type="button"
        xuiButton
        variant="ghost"
        size="sm"
        class="tabular-nums"
        data-testid="tempo"
        [xuiTooltip]="'Tempo: click to type'"
        (click)="startEditing()"
      >
        {{ tempo() }} BPM
      </button>
    }
    <button
      type="button"
      xuiButton
      variant="outline"
      size="sm"
      data-testid="tap"
      [xuiTooltip]="'Tap tempo'"
      (click)="live.tap()"
    >
      TAP
    </button>
    <button
      type="button"
      xuiButton
      variant="ghost"
      size="sm"
      aria-label="Metronome"
      [active]="globals.settings()?.metronome ?? false"
      [xuiTooltip]="'Metronome'"
      (click)="globals.update(g => (g.metronome = !g.metronome))"
    >
      <ng-icon xui name="matTimerRound" size="sm" />
    </button>
    <button
      type="button"
      xuiButton
      variant="ghost"
      size="sm"
      aria-label="Looper"
      [xuiTooltip]="'Looper'"
      (click)="looperOpen.set(true)"
    >
      <ng-icon xui name="matLoopRound" size="sm" [color]="looper()?.state === 'recording' ? 'error' : 'inherit'" />
    </button>

    <xui-dialog title="Looper" icon="matLoopRound" [(open)]="looperOpen">
      <xui-dialog-body>
        <div class="flex flex-col gap-4">
          <div class="flex items-center gap-3">
            <xui-tag [color]="stateColor()">{{ looper()?.state ?? 'empty' }}</xui-tag>
            <span class="text-foreground-muted text-sm tabular-nums">
              {{ (looper()?.positionSeconds ?? 0).toFixed(1) }} / {{ (looper()?.lengthSeconds ?? 0).toFixed(1) }} s
            </span>
            @if (looper()?.reversed) {
              <xui-tag minimal>reverse</xui-tag>
            }
            @if (looper()?.halfSpeed) {
              <xui-tag minimal>half speed</xui-tag>
            }
          </div>
          <div class="flex flex-wrap gap-2" role="toolbar" aria-label="Looper transport">
            @for (action of transport; track action.id) {
              <button
                xuiButton
                variant="outline"
                size="sm"
                type="button"
                [attr.data-testid]="'looper-' + action.id"
                (click)="act(action.id)"
              >
                <ng-icon xui [name]="action.icon" size="sm" /> {{ action.label }}
              </button>
            }
          </div>
        </div>
      </xui-dialog-body>
      <xui-dialog-footer align="between">
        <button xuiButton variant="ghost" color="error" type="button" (click)="act('clear')">Clear</button>
        <button xuiButton variant="outline" type="button" [disabled]="!looper()?.lengthSeconds" (click)="export()">
          <ng-icon xui name="matDownloadRound" size="sm" /> Export WAV
        </button>
      </xui-dialog-footer>
    </xui-dialog>
  `
})
export class Transport {
  protected readonly live = inject(LiveStore);
  protected readonly globals = inject(GlobalStore);
  private readonly events = inject(EventStream);
  private readonly connection = inject(ConnectionService);
  private readonly library = inject(LibraryStore);
  private readonly notify = inject(Notify);

  private readonly injector = inject(Injector);
  private readonly tempoField = viewChild<ElementRef<HTMLInputElement>>('tempoField');

  protected readonly editing = signal(false);
  protected readonly looperOpen = signal(false);
  protected readonly tempo = signal(120);
  protected readonly looper = signal<LooperInfo | null>(null);
  protected readonly transport = [
    { id: 'record', label: 'Record', icon: 'matFiberManualRecordRound' },
    { id: 'play', label: 'Play', icon: 'matPlayArrowRound' },
    { id: 'overdub', label: 'Overdub', icon: 'matRepeatRound' },
    { id: 'stop', label: 'Stop', icon: 'matStopRound' },
    { id: 'undo', label: 'Undo', icon: 'matUndoRound' },
    { id: 'reverse', label: 'Reverse', icon: 'matSwapHorizRound' },
    { id: 'halfSpeed', label: 'Half speed', icon: 'matSlowMotionVideoRound' }
  ] as const;
  protected readonly stateColor = computed(() => LooperColors[this.looper()?.state ?? 'empty'] ?? 'none');

  constructor() {
    onAnimationFrame(() => {
      const t = this.events.latest;
      if (!t) {
        return;
      }

      const tempo = Math.round(t.tempo * 10) / 10;
      if (tempo !== this.tempo()) {
        this.tempo.set(tempo);
      }

      const looper = t.looper ?? null;
      const current = this.looper();
      if (
        looper?.state !== current?.state ||
        Math.abs((looper?.positionSeconds ?? 0) - (current?.positionSeconds ?? 0)) >= 0.1 ||
        looper?.lengthSeconds !== current?.lengthSeconds ||
        looper?.reversed !== current?.reversed ||
        looper?.halfSpeed !== current?.halfSpeed
      ) {
        this.looper.set(looper ? { ...looper } : null);
      }
    });
  }

  /** Opens the tempo field focused with its value selected, so typing replaces it. */
  protected startEditing(): void {
    this.editing.set(true);
    // After the field renders: it is not there until change detection has run.
    afterNextRender(
      () => {
        const field = this.tempoField()?.nativeElement;
        field?.focus();
        field?.select();
      },
      { injector: this.injector }
    );
  }

  protected setTempo(text: string): void {
    this.editing.set(false);
    const bpm = Number(text);
    if (Number.isFinite(bpm) && bpm >= 30 && bpm <= 300) {
      void this.live.setTempo(bpm);
    }
  }

  protected async act(action: (typeof LooperActions)[number]): Promise<void> {
    await this.notify.run(async () => {
      await this.connection.client.POST('/api/v1/looper/{action}', { params: { path: { action } } });
    });
  }

  protected export(): void {
    void this.library.download('/api/v1/looper/export', 'loop.wav');
  }
}
