import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { type SceneMidiMessage } from '@cathode/api-client';
import { CthKnob } from '@cathode/audio-ui';
import { provideIcons } from '@ng-icons/core';
import { matAddRound, matDeleteRound } from '@ng-icons/material-icons/round';
import { XuiButtonImports } from '@xui/button';
import { XuiCardImports } from '@xui/card';
import { XuiIconImports } from '@xui/icon';
import { XuiInputImports } from '@xui/input';
import { XuiSelectImports } from '@xui/select';
import { XuiSliderImports } from '@xui/slider';
import { XuiTableImports } from '@xui/table';
import { XuiTextImports } from '@xui/text';
import { RigControl } from '../controls/rig-knob';
import { Coalescer } from '../core/coalescer';
import { ConnectionService } from '../core/connection';
import { Notify } from '../core/notify';
import { GlobalStore } from '../state/global.store';
import { LiveStore } from '../state/live.store';

/** How the Controllers page names a modifier's scope: all channels, or one. */
export function scopeName(channel: string | null | undefined): string {
  return !channel || channel === 'all' ? 'All channels' : `Channel ${channel} only`;
}

/**
 * The controllers modifiers draw on (docs/02-architecture.md §5.4): two LFOs, two ADSRs, the envelope and pitch
 * followers and the sequencer (stored with the preset), the active scene's level and controllers, and Manual A-D
 * (global). Assign them to a knob by right-clicking it on the block page.
 */
@Component({
  selector: 'cth-controllers-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CthKnob,
    RigControl,
    XuiButtonImports,
    XuiCardImports,
    XuiIconImports,
    XuiInputImports,
    XuiSelectImports,
    XuiSliderImports,
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
          />
          @for (value of sceneControllers(); track $index) {
            <cth-knob
              [label]="'Controller ' + ($index + 1)"
              unit="%"
              [min]="0"
              [max]="100"
              [value]="value * 100"
              (valueChange)="setSceneController($index, $event / 100)"
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

      <!-- Pairs side by side: LFO 1 and 2, ADSR 1 and 2, then the envelope and pitch followers. -->
      <div class="grid gap-6 lg:grid-cols-2">
        @for (group of groups; track group.title) {
          <section xuiCard class="flex flex-col gap-3 p-4" [attr.aria-label]="group.title">
            <h2 xuiHeading class="text-sm">{{ group.title }}</h2>
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
        <div class="flex h-36 items-end gap-2" role="group" aria-label="Sequencer stages">
          @for (stage of stages(); track stage) {
            <xui-slider
              orientation="vertical"
              class="h-32"
              [min]="0"
              [max]="1"
              [stepSize]="0.01"
              [labelStepSize]="1"
              [attr.aria-label]="'Stage ' + stage"
              [value]="stored('seq' + stage) ?? (stage % 2 === 0 ? 0 : 1)"
              (valueChange)="set('seq' + stage, $event)"
            />
          }
        </div>
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
  /**
   * The knobs here are sent through one coalescer: one request in flight per control, the latest value next, and the
   * values sent but not yet confirmed shown meanwhile, so a dragged knob neither floods the rig nor jumps back.
   */
  private readonly sending = new Coalescer<number | number[]>((key, value) => this.send(key, value));

  protected readonly manualNames = ['A', 'B', 'C', 'D'];
  protected readonly modifiers = computed(() => this.live.preset()?.modifiers ?? []);
  protected readonly modifierScopes = ['all', 'A', 'B', 'C', 'D'];
  protected readonly scopeName = scopeName;
  protected readonly maxSceneMidi = 8;
  protected readonly midiTypes = ['pc', 'cc'];
  protected readonly midiTypeName = (type: string) => (type === 'cc' ? 'CC' : 'Program');
  protected readonly groups = [
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
  protected readonly stages = computed(() =>
    Array.from({ length: Math.round(this.stored('seqStages') ?? 8) }, (_, i) => i + 1)
  );

  protected stored(id: string): number | undefined {
    return (this.sending.pending(`controller:${id}`) as number | undefined) ?? this.live.preset()?.controllers[id];
  }

  protected set(id: string, value: number): Promise<void> {
    return this.sending.set(`controller:${id}`, value);
  }

  protected setSceneLevel(level: number): Promise<void> {
    return this.sending.set(`scene:${this.live.activeScene()}:level`, level);
  }

  protected setSceneController(index: number, value: number): Promise<void> {
    const controllers = [...this.sceneControllers()];
    controllers[index] = value;
    return this.sending.set(`scene:${this.live.activeScene()}:controllers`, controllers);
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

  /** Sends one coalesced value: `controller:<id>`, `scene:<n>:level`, `scene:<n>:controllers` or `manual:<n>`. */
  private async send(key: string, value: number | number[]): Promise<void> {
    const [kind, id = '', field] = key.split(':');
    if (kind === 'controller') {
      await this.live.setControllers({ [id]: value as number });
    } else if (kind === 'scene') {
      await this.live.updateScene(
        Number(id),
        field === 'level' ? { level: value as number } : { controllers: value as number[] }
      );
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

function lfo(n: number) {
  return {
    title: `LFO ${n}`,
    ids: [`lfo${n}Rate`, `lfo${n}Shape`, `lfo${n}Tempo`],
    labels: ['Rate', 'Shape', 'Tempo']
  };
}

function adsr(n: number) {
  return {
    title: `ADSR ${n}`,
    ids: [`adsr${n}Attack`, `adsr${n}Decay`, `adsr${n}Sustain`, `adsr${n}Release`, `adsr${n}Threshold`],
    labels: ['Attack', 'Decay', 'Sustain', 'Release', 'Threshold']
  };
}
