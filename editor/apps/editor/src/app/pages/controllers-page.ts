import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { type ControllerInfo, type SceneMidiMessage, type Telemetry } from '@cathode/api-client';
import { CthKnob, CthStepBars, onAnimationFrame } from '@cathode/audio-ui';
import { provideIcons } from '@ng-icons/core';
import { matAddRound, matDeleteRound } from '@ng-icons/material-icons/round';
import { XuiButtonImports } from '@xui/button';
import { XuiCardImports } from '@xui/card';
import { XuiIconImports } from '@xui/icon';
import { XuiInputImports } from '@xui/input';
import { XuiSelectImports } from '@xui/select';
import { XuiTableImports } from '@xui/table';
import { XuiTextImports } from '@xui/text';
import { NumberSteppers } from '../controls/number-steppers';
import { RigControl } from '../controls/rig-knob';
import { Coalescer } from '../core/coalescer';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';
import { GlobalStore } from '../state/global.store';
import { KnobGesture } from '../state/knob-gesture';
import { LiveStore } from '../state/live.store';

/** External 1-8: the modifier sources a pedal or CC drives. */
const externalCount = 8;

/** Telemetry older than this no longer shows the controllers moving: the socket or the engine has stopped. */
const StaleMs = 500;

/** A live value has to move this much (of 0 to 1) to be redrawn: finer steps are invisible in a 64 px bar. */
const LiveStep = 0.01;

/** The controllers with a live indicator on this page. */
type LiveSource = 'lfo1' | 'lfo2' | 'adsr1' | 'adsr2';
const liveSources: readonly LiveSource[] = ['lfo1', 'lfo2', 'adsr1', 'adsr2'];

/** A card of controller settings; `live` names the controller whose value it shows moving. */
interface ControllerGroup {
  title: string;
  ids: string[];
  labels: string[];
  live?: LiveSource;
}

/**
 * The controllers to show live, from the latest telemetry `frame` received `ageMs` ago: null when they are not
 * running or the frame is stale or lacks them. Returns `previous` itself while nothing visible changed (the same stage
 * and every value within {@link LiveStep}), so a signal holding it is not marked for a change no one could see.
 */
export function liveControllers(
  previous: ControllerInfo | null,
  frame: Telemetry | null,
  ageMs: number
): ControllerInfo | null {
  const next = frame?.controllers?.running && ageMs < StaleMs ? frame.controllers : null;
  if (!next || !previous) {
    return next;
  }

  const same =
    next.sequencerStage === previous.sequencerStage &&
    liveSources.every(source => Math.abs(next[source] - previous[source]) < LiveStep);
  return same ? previous : next;
}

/** How the Controllers page names a modifier's scope: all channels, or one. */
export function scopeName(channel: string | null | undefined): string {
  return !channel || channel === 'all' ? 'All channels' : `Channel ${channel} only`;
}

/**
 * The controllers modifiers draw on (docs/02-architecture.md §5.4): two LFOs, two ADSRs, the envelope and pitch
 * followers and the sequencer (stored with the preset), the active scene's level and controllers, and Manual A-D
 * (global), and External 1-8, which a pedal or CC normally drives and which can be set here too (live only). Assign
 * them to a knob by right-clicking it on the block page.
 */
@Component({
  selector: 'cth-controllers-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CthKnob,
    CthStepBars,
    NumberSteppers,
    RigControl,
    XuiButtonImports,
    XuiCardImports,
    XuiIconImports,
    XuiInputImports,
    XuiSelectImports,
    XuiTableImports,
    XuiTextImports
  ],
  providers: [provideIcons({ matAddRound, matDeleteRound })],
  host: { class: 'block h-full overflow-auto p-6' },
  template: `
    <div class="mx-auto flex max-w-6xl flex-col gap-6">
      <h1 xuiHeading class="text-lg">Controllers</h1>

      <section xuiCard class="flex flex-col gap-3 p-4" aria-labelledby="scene-heading">
        <h2 xuiHeading id="scene-heading" class="text-sm">Scene {{ live.activeScene() + 1 }}</h2>
        <div class="flex flex-wrap gap-6">
          <cth-knob
            label="Scene Level"
            unit="dB"
            [min]="-60"
            [max]="12"
            [defaultValue]="0"
            [value]="sceneLevel()"
            (valueChange)="setSceneLevel($event)"
            (committed)="gesture('level').end()"
          />
          @for (value of sceneControllers(); track $index) {
            <cth-knob
              [label]="'Controller ' + ($index + 1)"
              unit="%"
              [min]="0"
              [max]="100"
              [value]="value * 100"
              (valueChange)="setSceneController($index, $event / 100)"
              (committed)="gesture('controller' + $index).end()"
            />
          }
        </div>
      </section>

      <section xuiCard class="flex flex-col gap-3 p-4" aria-labelledby="scene-midi-heading">
        <h2 xuiHeading id="scene-midi-heading" class="text-sm">Scene {{ live.activeScene() + 1 }} MIDI</h2>
        <p xuiText class="text-foreground-muted text-xs">
          Sent through the MIDI output whenever this scene is selected, or the preset loads on it: program changes and
          CCs for other gear. Up to {{ maxSceneMidi }} per scene.
        </p>
        <xui-table>
          <xui-tr>
            <xui-th>Type</xui-th>
            <xui-th>Channel</xui-th>
            <xui-th>Number</xui-th>
            <xui-th>Value</xui-th>
            <xui-th><span class="sr-only">Remove</span></xui-th>
          </xui-tr>
          @for (message of sceneMidi(); track $index; let i = $index) {
            <xui-tr>
              <xui-td>
                <xui-select
                  class="w-24"
                  [attr.aria-label]="'Message ' + (i + 1) + ' type'"
                  [filterable]="false"
                  [items]="midiTypes"
                  [itemText]="midiTypeName"
                  [value]="message.type"
                  (valueChange)="setSceneMidi(i, { type: $event ?? 'pc' })"
                />
              </xui-td>
              <xui-td>
                <input
                  #channelField
                  xuiInput
                  type="number"
                  cthSteppers
                  class="w-20"
                  min="1"
                  max="16"
                  [attr.aria-label]="'Message ' + (i + 1) + ' channel'"
                  [value]="message.channel"
                  (change)="setSceneMidi(i, { channel: clamp(channelField.value, 1, 16) })"
                />
              </xui-td>
              <xui-td>
                <input
                  #numberField
                  xuiInput
                  type="number"
                  cthSteppers
                  class="w-20"
                  min="0"
                  max="127"
                  [attr.aria-label]="'Message ' + (i + 1) + (message.type === 'cc' ? ' controller' : ' program')"
                  [value]="message.number"
                  (change)="setSceneMidi(i, { number: clamp(numberField.value, 0, 127) })"
                />
              </xui-td>
              <xui-td>
                @if (message.type === 'cc') {
                  <input
                    #valueField
                    xuiInput
                    type="number"
                    cthSteppers
                    class="w-20"
                    min="0"
                    max="127"
                    [attr.aria-label]="'Message ' + (i + 1) + ' value'"
                    [value]="message.value"
                    (change)="setSceneMidi(i, { value: clamp(valueField.value, 0, 127) })"
                  />
                }
              </xui-td>
              <xui-td>
                <button
                  type="button"
                  xuiButton
                  variant="ghost"
                  size="sm"
                  color="error"
                  [attr.aria-label]="'Remove message ' + (i + 1)"
                  (click)="removeSceneMidi(i)"
                >
                  <ng-icon xui name="matDeleteRound" size="sm" />
                </button>
              </xui-td>
            </xui-tr>
          } @empty {
            <xui-tr><xui-td class="text-foreground-muted">This scene sends no MIDI.</xui-td></xui-tr>
          }
        </xui-table>
        <button
          xuiButton
          type="button"
          class="self-start"
          [disabled]="sceneMidi().length >= maxSceneMidi"
          (click)="addSceneMidi()"
        >
          <ng-icon xui name="matAddRound" size="sm" />
          Add message
        </button>
      </section>

      <section xuiCard class="flex flex-col gap-3 p-4" aria-labelledby="manual-heading">
        <h2 xuiHeading id="manual-heading" class="text-sm">Manual (global)</h2>
        <div class="flex flex-wrap gap-6">
          @for (name of manualNames; track name; let i = $index) {
            <cth-knob
              [label]="'Manual ' + name"
              unit="%"
              [min]="0"
              [max]="100"
              [value]="(manual()[i] ?? 0) * 100"
              (valueChange)="setManual(i, $event / 100)"
            />
          }
        </div>
      </section>

      <section xuiCard class="flex flex-col gap-3 p-4" aria-labelledby="external-heading">
        <h2 xuiHeading id="external-heading" class="text-sm">External (live)</h2>
        <p xuiText class="text-foreground-muted text-xs">
          What an expression pedal or a MIDI CC mapped to an External would set: move one here to try a wah, a whammy or
          a modifier without a pedal. Live values only, not saved; a pedal moved later takes over.
        </p>
        <div class="flex flex-wrap gap-6" data-testid="external-controls">
          @for (value of externals(); track $index) {
            <cth-knob
              [label]="'External ' + ($index + 1)"
              unit="%"
              [min]="0"
              [max]="100"
              [value]="value * 100"
              (valueChange)="setExternal($index, $event / 100)"
            />
          }
        </div>
      </section>

      <!-- Pairs side by side: LFO 1 and 2, ADSR 1 and 2, then the envelope and pitch followers. -->
      <div class="grid gap-6 lg:grid-cols-2">
        @for (group of groups; track group.title) {
          <section xuiCard class="flex flex-col gap-3 p-4" [attr.aria-label]="group.title">
            <div class="flex items-center justify-between gap-3">
              <h2 xuiHeading class="text-sm">{{ group.title }}</h2>
              @if (group.live; as source) {
                <!-- Where the controller is now: a recessed channel lit up to its value, empty while it stands still. -->
                <span
                  class="relative h-1 w-16 overflow-hidden rounded-full"
                  style="background: #0b0c0e; box-shadow: inset 0 1px 2px rgb(0 0 0 / 0.6)"
                  role="meter"
                  aria-valuemin="0"
                  aria-valuemax="100"
                  [attr.aria-label]="group.title + ' now'"
                  [attr.aria-valuenow]="percent(liveValue(source))"
                  [attr.data-live]="source"
                >
                  <span
                    class="bg-primary absolute inset-0 origin-left rounded-full"
                    [style.transform]="'scaleX(' + (liveValue(source) ?? 0) + ')'"
                  ></span>
                </span>
              }
            </div>
            <div class="flex flex-wrap items-start gap-6">
              @for (id of group.ids; track id) {
                <cth-rig-control
                  [id]="id"
                  [label]="group.labels[$index]"
                  [stored]="stored(id)"
                  (changed)="set(id, $event)"
                />
              }
            </div>
          </section>
        }
      </div>

      <section xuiCard class="flex flex-col gap-3 p-4" aria-labelledby="sequencer-heading">
        <h2 xuiHeading id="sequencer-heading" class="text-sm">Sequencer</h2>
        <div class="flex flex-wrap items-start gap-6">
          <cth-rig-control
            id="seqStages"
            label="Stages"
            [stored]="stored('seqStages')"
            (changed)="set('seqStages', $event)"
          />
          <cth-rig-control id="seqRate" label="Rate" [stored]="stored('seqRate')" (changed)="set('seqRate', $event)" />
          <cth-rig-control
            id="seqTempo"
            label="Tempo"
            [stored]="stored('seqTempo')"
            (changed)="set('seqTempo', $event)"
          />
        </div>
        <cth-step-bars
          label="Sequencer stages"
          [values]="stageValues()"
          [active]="playingStage()"
          (changed)="set('seq' + ($event.index + 1), $event.value)"
        />
      </section>

      <section xuiCard class="flex flex-col gap-3 p-4" aria-labelledby="modifiers-heading">
        <h2 xuiHeading id="modifiers-heading" class="text-sm">Modifiers</h2>
        <p xuiText class="text-foreground-muted text-xs">
          Every modified parameter of this preset. A modifier applies on all channels of its block, or on one: on the
          others the parameter keeps that channel's own value. Right-click a knob on a block's page to add one.
        </p>
        @if (modifiers().length === 0) {
          <p xuiText class="text-foreground-muted text-xs">No modifiers yet.</p>
        } @else {
          <xui-table data-testid="modifier-list">
            <xui-tr>
              <xui-th>Parameter</xui-th>
              <xui-th>Source</xui-th>
              <xui-th>Applies on</xui-th>
            </xui-tr>
            @for (m of modifiers(); track m.block + '/' + m.param; let i = $index) {
              <xui-tr>
                <xui-td>{{ m.block }} · {{ m.param }}</xui-td>
                <xui-td>{{ m.source }}</xui-td>
                <xui-td>
                  <xui-select
                    class="w-36"
                    [attr.aria-label]="m.block + ' ' + m.param + ' channels'"
                    [filterable]="false"
                    [items]="modifierScopes"
                    [itemText]="scopeName"
                    [value]="m.channel ?? 'all'"
                    (valueChange)="setScope(i, $event)"
                  />
                </xui-td>
              </xui-tr>
            }
          </xui-table>
        }
      </section>
    </div>
  `
})
export class ControllersPage {
  protected readonly live = inject(LiveStore);
  private readonly globals = inject(GlobalStore);
  private readonly connection = inject(ConnectionService);
  private readonly notify = inject(Notify);
  private readonly events = inject(EventStream);
  /**
   * The knobs here are sent through one coalescer: one request in flight per control, the latest value next, and the
   * values sent but not yet confirmed shown meanwhile, so a dragged knob neither floods the rig nor jumps back.
   */
  private readonly sending = new Coalescer<number | number[]>((key, value) => this.send(key, value));
  /** The scene knobs' gestures, by knob: one drag is one undo step, the next drag another. */
  private readonly gestures = new Map<string, KnobGesture>();
  /** The gesture of the value waiting in {@link sending} for a scene key, sent with it. */
  private readonly gestureOf = new Map<string, string>();

  protected readonly manualNames = ['A', 'B', 'C', 'D'];
  protected readonly modifiers = computed(() => this.live.preset()?.modifiers ?? []);
  protected readonly modifierScopes = ['all', 'A', 'B', 'C', 'D'];
  protected readonly scopeName = scopeName;
  protected readonly maxSceneMidi = 8;
  protected readonly midiTypes = ['pc', 'cc'];
  protected readonly midiTypeName = (type: string) => (type === 'cc' ? 'CC' : 'Program');
  protected readonly groups: readonly ControllerGroup[] = [
    lfo(1),
    lfo(2),
    adsr(1),
    adsr(2),
    {
      title: 'Envelope follower',
      ids: ['envAttack', 'envRelease', 'envGain'],
      labels: ['Attack', 'Release', 'Sensitivity']
    },
    { title: 'Pitch follower', ids: ['pitchLow', 'pitchHigh'], labels: ['Low', 'High'] }
  ];
  /**
   * The sequencer, LFOs and ADSRs as the engine last reported them, while they run: read from telemetry on animation
   * frames (it bypasses change detection), and set only when the stage changed or a value moved visibly.
   */
  protected readonly controllersNow = signal<ControllerInfo | null>(null);
  /** The sequencer stage playing, for the step bars' marker; null while the sequencer stands still. */
  protected readonly playingStage = computed(() => this.controllersNow()?.sequencerStage ?? null);
  protected readonly scene = computed(() => this.live.preset()?.scenes[this.live.activeScene()]);
  protected readonly sceneLevel = computed(
    () =>
      (this.sending.pending(`scene:${this.live.activeScene()}:level`) as number | undefined) ?? this.scene()?.level ?? 0
  );
  protected readonly sceneControllers = computed(
    () =>
      (this.sending.pending(`scene:${this.live.activeScene()}:controllers`) as number[] | undefined) ??
      this.scene()?.controllers ?? [0, 0, 0, 0]
  );
  protected readonly sceneMidi = computed<readonly SceneMidiMessage[]>(() => this.scene()?.midi ?? []);
  protected readonly manual = computed(() =>
    (this.globals.settings()?.manual ?? [0, 0, 0, 0]).map(
      (value, i) => (this.sending.pending(`manual:${i}`) as number | undefined) ?? value
    )
  );
  /** External 1-8 as last set from this page: the Server keeps them live only and does not report them back. */
  private readonly externalsSent = signal<readonly number[]>(Array.from({ length: externalCount }, () => 0));
  protected readonly externals = computed(() =>
    this.externalsSent().map((value, i) => (this.sending.pending(`external:${i}`) as number | undefined) ?? value)
  );
  /** The sequencer's stages in use, each at its value (odd stages default to 1, even to 0, as the engine's do). */
  protected readonly stageValues = computed(() =>
    Array.from(
      { length: Math.round(this.stored('seqStages') ?? 8) },
      (_, i) => this.stored(`seq${i + 1}`) ?? (i % 2 === 0 ? 1 : 0)
    )
  );

  constructor() {
    onAnimationFrame(() => {
      const next = liveControllers(this.controllersNow(), this.events.latest, performance.now() - this.events.latestAt);
      if (next !== this.controllersNow()) {
        this.controllersNow.set(next);
      }
    });
  }

  /** LFO or ADSR `source`'s value now, 0 to 1, or null while it stands still. */
  protected liveValue(source: LiveSource): number | null {
    return this.controllersNow()?.[source] ?? null;
  }

  protected percent(value: number | null): number | null {
    return value === null ? null : Math.round(value * 100);
  }

  protected stored(id: string): number | undefined {
    return (this.sending.pending(`controller:${id}`) as number | undefined) ?? this.live.preset()?.controllers[id];
  }

  protected set(id: string, value: number): Promise<void> {
    return this.sending.set(`controller:${id}`, value);
  }

  protected setSceneLevel(level: number): Promise<void> {
    const key = `scene:${this.live.activeScene()}:level`;
    this.gestureOf.set(key, this.gesture('level').current());
    return this.sending.set(key, level);
  }

  protected setSceneController(index: number, value: number): Promise<void> {
    const controllers = [...this.sceneControllers()];
    controllers[index] = value;
    const key = `scene:${this.live.activeScene()}:controllers`;
    this.gestureOf.set(key, this.gesture(`controller${index}`).current());
    return this.sending.set(key, controllers);
  }

  /** A scene knob's gesture. */
  protected gesture(knob: string): KnobGesture {
    let gesture = this.gestures.get(knob);
    if (!gesture) {
      gesture = new KnobGesture();
      this.gestures.set(knob, gesture);
    }

    return gesture;
  }

  protected clamp(raw: string, min: number, max: number): number {
    const value = Math.round(Number(raw));
    return Number.isFinite(value) ? Math.min(max, Math.max(min, value)) : min;
  }

  protected addSceneMidi(): void {
    const midi = [...this.sceneMidi(), { type: 'pc', channel: 1, number: 0, value: 0 }];
    void this.live.updateScene(this.live.activeScene(), { midi });
  }

  protected setSceneMidi(index: number, change: Partial<SceneMidiMessage>): void {
    const midi = this.sceneMidi().map((m, i) => (i === index ? { ...m, ...change } : m));
    void this.live.updateScene(this.live.activeScene(), { midi });
  }

  protected removeSceneMidi(index: number): void {
    const midi = this.sceneMidi().filter((_, i) => i !== index);
    void this.live.updateScene(this.live.activeScene(), { midi });
  }

  protected setManual(index: number, value: number): Promise<void> {
    return this.sending.set(`manual:${index}`, value);
  }

  protected setExternal(index: number, value: number): Promise<void> {
    return this.sending.set(`external:${index}`, Math.min(1, Math.max(0, value)));
  }

  /**
   * Sends one coalesced value: `controller:<id>`, `scene:<n>:level`, `scene:<n>:controllers`, `manual:<n>` or
   * `external:<n>`.
   */
  private async send(key: string, value: number | number[]): Promise<void> {
    const [kind, id = '', field] = key.split(':');
    if (kind === 'controller') {
      await this.live.setControllers({ [id]: value as number });
    } else if (kind === 'scene') {
      await this.live.updateScene(
        Number(id),
        field === 'level' ? { level: value as number } : { controllers: value as number[] },
        this.gestureOf.get(key)
      );
    } else if (kind === 'external') {
      const index = Number(id);
      await this.notify.run(async () => {
        await this.connection.client.PUT('/api/v1/live/external/{index}', {
          params: { path: { index } },
          body: { value: value as number }
        });
        this.externalsSent.update(values => values.map((v, i) => (i === index ? (value as number) : v)));
      });
    } else {
      const index = Number(id);
      await this.notify.run(async () => {
        await this.connection.client.PUT('/api/v1/live/manual/{index}', {
          params: { path: { index } },
          body: { value: value as number }
        });
        this.globals.settings.update(s =>
          s ? { ...s, manual: s.manual.map((v, i) => (i === index ? (value as number) : v)) } : s
        );
      });
    }
  }

  /** Scopes modifier <code>index</code> to one channel, or (<code>all</code>) to every channel of its block. */
  protected setScope(index: number, scope: string | null): void {
    const modifiers = this.modifiers().map((m, i) =>
      i === index ? { ...m, channel: !scope || scope === 'all' ? null : scope } : m
    );
    void this.live.setModifiers(modifiers);
  }
}

function lfo(n: 1 | 2): ControllerGroup {
  return {
    title: `LFO ${n}`,
    live: `lfo${n}`,
    ids: [`lfo${n}Rate`, `lfo${n}Shape`, `lfo${n}Tempo`],
    labels: ['Rate', 'Shape', 'Tempo']
  };
}

function adsr(n: 1 | 2): ControllerGroup {
  return {
    title: `ADSR ${n}`,
    live: `adsr${n}`,
    ids: [`adsr${n}Attack`, `adsr${n}Decay`, `adsr${n}Sustain`, `adsr${n}Release`, `adsr${n}Threshold`],
    labels: ['Attack', 'Decay', 'Sustain', 'Release', 'Threshold']
  };
}
