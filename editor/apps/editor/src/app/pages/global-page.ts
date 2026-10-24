import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import {
  type Footswitch,
  type FootswitchLayout,
  type GlobalSettings,
  MidiActions,
  type MidiActivity,
  type SwitchAction
} from '@cathode/api-client';
import { CthKnob } from '@cathode/audio-ui';
import { provideIcons } from '@ng-icons/core';
import { matAddRound, matDeleteRound, matSettingsInputComponentRound } from '@ng-icons/material-icons/round';
import { XuiButtonImports } from '@xui/button';
import { XuiCalloutImports } from '@xui/callout';
import { XuiCardImports } from '@xui/card';
import { XuiIconImports } from '@xui/icon';
import { XuiInputImports } from '@xui/input';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { XuiSelectImports } from '@xui/select';
import { XuiSwitchImports } from '@xui/switch';
import { XuiTableImports } from '@xui/table';
import { XuiTextImports } from '@xui/text';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';
import { CatalogStore } from '../state/catalog.store';
import { GlobalStore } from '../state/global.store';
import { LiveStore } from '../state/live.store';
import { addView, overrideOf, removeView, viewNames, withOverride } from './footswitches';

const Messages = ['cc', 'note', 'pc'];

/** A readable MIDI message for the monitor: "CC 11 = 64 (ch 1)". */
export function describeMidi(m: Pick<MidiActivity, 'status' | 'data1' | 'data2'>): string {
  const channel = (m.status & 0x0f) + 1;
  switch (m.status & 0xf0) {
    case 0xb0:
      return `CC ${m.data1} = ${m.data2} (ch ${channel})`;
    case 0x90:
      return m.data2 > 0 ? `Note on ${m.data1} vel ${m.data2} (ch ${channel})` : `Note off ${m.data1} (ch ${channel})`;
    case 0x80:
      return `Note off ${m.data1} (ch ${channel})`;
    case 0xc0:
      return `Program ${m.data1} (ch ${channel})`;
    default:
      return `0x${m.status.toString(16)} ${m.data1} ${m.data2}`;
  }
}

/**
 * Global settings: where the tempo comes from, the metronome, the global output EQ, and MIDI: the channel, mappings
 * (with MIDI learn and a live monitor) and footswitch layouts for generic MIDI foot controllers.
 */
@Component({
  selector: 'cth-global-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CthKnob,
    XuiButtonImports,
    XuiCalloutImports,
    XuiCardImports,
    XuiIconImports,
    XuiInputImports,
    XuiSegmentedControlImports,
    XuiSelectImports,
    XuiSwitchImports,
    XuiTableImports,
    XuiTextImports
  ],
  providers: [provideIcons({ matAddRound, matDeleteRound, matSettingsInputComponentRound })],
  host: { class: 'block h-full overflow-auto p-6' },
  template: `
    @if (settings(); as s) {
      <div class="flex max-w-6xl flex-col gap-6">
        <h1 xuiHeading class="text-lg">Global</h1>

        <section xuiCard class="flex flex-col gap-4 p-4" aria-labelledby="tempo-heading">
          <h2 xuiHeading id="tempo-heading" class="text-sm">Tempo and metronome</h2>
          <div class="flex flex-wrap items-end gap-6">
            <div class="flex flex-col gap-1">
              <span class="text-foreground-muted text-xs">Tempo from</span>
              <xui-segmented-control
                size="sm"
                aria-label="Tempo source"
                [options]="tempoModes"
                [value]="s.tempoMode"
                (valueChange)="save(g => (g.tempoMode = $event ?? 'preset'))"
              />
            </div>
            @if (s.tempoMode === 'global') {
              <cth-knob
                label="Global tempo"
                unit="BPM"
                [min]="30"
                [max]="300"
                [value]="s.tempo"
                (committed)="save(g => (g.tempo = $event))"
              />
            }
            <label class="flex flex-col items-center gap-2">
              <span class="text-foreground-muted text-xs">Metronome</span>
              <xui-switch
                aria-label="Metronome"
                [checked]="s.metronome"
                (checkedChange)="save(g => (g.metronome = $event))"
              />
            </label>
            <cth-knob
              label="Click level"
              unit="dB"
              [min]="-60"
              [max]="0"
              [value]="s.metronomeLevel"
              (committed)="save(g => (g.metronomeLevel = $event))"
            />
          </div>
        </section>

        <section xuiCard class="flex flex-col gap-4 p-4" aria-labelledby="offsets-heading">
          <h2 xuiHeading id="offsets-heading" class="text-sm">Mix and gate offsets</h2>
          <p xuiText class="text-foreground-muted text-xs">
            Effects Mix is added to the Mix of every block whose Global Mix switch is on. The noise gate offset is added
            to the threshold of every Input block's gate.
          </p>
          <div class="flex flex-wrap items-end gap-6">
            <cth-knob
              label="Effects Mix"
              unit="%"
              [min]="-50"
              [max]="50"
              [defaultValue]="0"
              [value]="s.effectsMix"
              (committed)="save(g => (g.effectsMix = $event))"
            />
            <cth-knob
              label="Gate offset"
              unit="dB"
              [min]="-20"
              [max]="20"
              [defaultValue]="0"
              [value]="s.noiseGateOffset"
              (committed)="save(g => (g.noiseGateOffset = $event))"
            />
          </div>
        </section>

        <section xuiCard class="flex flex-col gap-4 p-4" aria-labelledby="eq-heading">
          <h2 xuiHeading id="eq-heading" class="text-sm">Global output EQ</h2>
          <div class="flex flex-wrap gap-4">
            @for (param of eqParams(); track param.id) {
              <cth-knob
                size="sm"
                [label]="param.name"
                [unit]="param.unit"
                [min]="param.min"
                [max]="param.max"
                [defaultValue]="param.default"
                [value]="s.eq[param.id] ?? param.default"
                (committed)="save(g => (g.eq[param.id] = $event))"
              />
            }
          </div>
        </section>

        <section xuiCard class="flex flex-col gap-4 p-4" aria-labelledby="midi-heading">
          <h2 xuiHeading id="midi-heading" class="text-sm">MIDI</h2>
          <div class="flex flex-wrap items-end gap-6">
            <label class="flex flex-col gap-1">
              <span class="text-foreground-muted text-xs">Receive channel</span>
              <xui-select
                class="w-32"
                aria-label="MIDI channel"
                [filterable]="false"
                [items]="channelNames"
                [value]="channelNames[s.midiChannel]"
                (valueChange)="save(g => (g.midiChannel = Math.max(0, channelNames.indexOf($event ?? ''))))"
              />
            </label>
          </div>

          <xui-table>
            <xui-tr>
              <xui-th>Message</xui-th>
              <xui-th>Action</xui-th>
              <xui-th>Target</xui-th>
              <xui-th>Value</xui-th>
              <xui-th><span class="sr-only">Remove</span></xui-th>
            </xui-tr>
            @for (mapping of s.midiMappings; track mapping.id) {
              <xui-tr>
                <xui-td
                  >{{ mapping.message.toUpperCase() }} {{ mapping.number }} ·
                  {{ mapping.channel === 0 ? 'any channel' : 'ch ' + mapping.channel }}</xui-td
                >
                <xui-td>{{ mapping.action }}</xui-td>
                <xui-td>{{ mapping.target ?? '' }}</xui-td>
                <xui-td>{{ mapping.value ?? '' }}</xui-td>
                <xui-td>
                  <button
                    xuiButton
                    variant="ghost"
                    size="sm"
                    color="error"
                    [attr.aria-label]="'Remove mapping for ' + mapping.message + ' ' + mapping.number"
                    (click)="removeMapping(mapping.id)"
                  >
                    <ng-icon xui name="matDeleteRound" size="sm" />
                  </button>
                </xui-td>
              </xui-tr>
            } @empty {
              <xui-tr><xui-td class="text-foreground-muted">No mappings yet: use MIDI learn below.</xui-td></xui-tr>
            }
          </xui-table>

          <div class="flex flex-wrap items-end gap-3">
            <label class="flex flex-col gap-1">
              <span class="text-foreground-muted text-xs">Action</span>
              <xui-select
                class="w-44"
                aria-label="Action to learn"
                [items]="actions"
                [value]="learnAction()"
                (valueChange)="learnAction.set($event ?? 'scene')"
              />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-foreground-muted text-xs">Target (block, bank or looper action)</span>
              <input
                xuiInput
                class="w-48"
                [value]="learnTarget()"
                (input)="learnTarget.set($any($event.target).value)"
              />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-foreground-muted text-xs">Value (optional)</span>
              <input
                xuiInput
                type="number"
                class="w-24"
                [value]="learnValue()"
                (input)="learnValue.set($any($event.target).value)"
              />
            </label>
            <button xuiButton type="button" [loading]="learning()" (click)="learn()">
              <ng-icon xui name="matSettingsInputComponentRound" size="sm" />
              {{ learning() ? 'Move a control…' : 'MIDI learn' }}
            </button>
          </div>

          <div aria-live="polite">
            <span class="text-foreground-muted text-xs">Monitor</span>
            <ul class="bg-surface-sunken mt-1 h-28 overflow-auto rounded-lg p-2 font-mono text-xs">
              @for (line of monitor(); track $index) {
                <li>{{ line }}</li>
              } @empty {
                <li class="text-foreground-subtle">Nothing received yet.</li>
              }
            </ul>
          </div>
        </section>

        <section xuiCard class="flex flex-col gap-4 p-4" aria-labelledby="footswitch-heading">
          <h2 xuiHeading id="footswitch-heading" class="text-sm">Footswitches</h2>
          <div class="flex flex-wrap items-end gap-3">
            <label class="flex flex-col gap-1">
              <span class="text-foreground-muted text-xs">Active layout</span>
              <xui-select
                class="w-56"
                aria-label="Active footswitch layout"
                [items]="s.footswitches"
                [itemText]="layoutName"
                [value]="activeLayout()"
                (valueChange)="save(g => (g.activeLayout = $event?.id ?? null))"
              />
            </label>
            <button xuiButton variant="outline" type="button" (click)="addLayout()">
              <ng-icon xui name="matAddRound" size="sm" /> New layout
            </button>
          </div>
          @if (activeLayout(); as layout) {
            <div class="flex flex-wrap items-center gap-2" data-testid="footswitch-views">
              <span class="text-foreground-muted text-xs">Views</span>
              @for (view of views(); track $index; let i = $index) {
                <span class="flex items-center gap-0.5">
                  <button
                    xuiButton
                    size="sm"
                    [variant]="i === editingView() ? 'default' : 'outline'"
                    (click)="editingView.set(i)"
                  >
                    {{ view }}{{ i === s.activeView ? ' (active)' : '' }}
                  </button>
                  @if (views().length > 1 && i === editingView()) {
                    <button
                      xuiButton
                      size="sm"
                      variant="ghost"
                      color="error"
                      [attr.aria-label]="'Remove view ' + view"
                      (click)="dropView(layout, i)"
                    >
                      <ng-icon xui name="matDeleteRound" size="sm" />
                    </button>
                  }
                </span>
              }
              <button xuiButton size="sm" variant="ghost" (click)="saveLayout(addView(layout))">
                <ng-icon xui name="matAddRound" size="sm" /> View
              </button>
              @if (editingView() !== s.activeView) {
                <button xuiButton size="sm" variant="ghost" (click)="save(g => (g.activeView = editingView()))">
                  Make active
                </button>
              }
            </div>
            <xui-table>
              <xui-tr>
                <xui-th>Switch</xui-th>
                <xui-th>Sends</xui-th>
                <xui-th>Tap</xui-th>
                <xui-th>Hold</xui-th>
                <xui-th>LED</xui-th>
                <xui-th>Then layout</xui-th>
                <xui-th>In this preset</xui-th>
                <xui-th><span class="sr-only">Remove</span></xui-th>
              </xui-tr>
              @for (fs of switchesOnView(layout); track fs.index) {
                <xui-tr>
                  <xui-td>
                    <input
                      xuiInput
                      size="sm"
                      class="w-28"
                      [attr.aria-label]="'Switch ' + fs.index + ' label'"
                      [value]="fs.label"
                      (change)="editSwitch(layout, fs, { label: $any($event.target).value })"
                    />
                  </xui-td>
                  <xui-td class="flex items-center gap-1">
                    <xui-select
                      class="w-20"
                      [filterable]="false"
                      [items]="messages"
                      [value]="fs.message"
                      [attr.aria-label]="'Switch ' + fs.index + ' message'"
                      (valueChange)="editSwitch(layout, fs, { message: $event ?? 'cc' })"
                    />
                    <input
                      xuiInput
                      size="sm"
                      type="number"
                      class="w-16"
                      [attr.aria-label]="'Switch ' + fs.index + ' number'"
                      [value]="fs.number"
                      (change)="editSwitch(layout, fs, { number: +$any($event.target).value })"
                    />
                    <input
                      xuiInput
                      size="sm"
                      type="number"
                      class="w-14"
                      [attr.aria-label]="'Switch ' + fs.index + ' channel'"
                      [value]="fs.channel"
                      (change)="editSwitch(layout, fs, { channel: +$any($event.target).value })"
                    />
                  </xui-td>
                  <xui-td>
                    <input
                      xuiInput
                      size="sm"
                      class="w-40"
                      placeholder="scene 2"
                      [attr.aria-label]="'Switch ' + fs.index + ' tap action'"
                      [value]="actionText(fs.tap)"
                      (change)="
                        editSwitch(layout, fs, {
                          tap: parseAction($any($event.target).value) ?? fs.tap
                        })
                      "
                    />
                  </xui-td>
                  <xui-td>
                    <input
                      xuiInput
                      size="sm"
                      class="w-40"
                      placeholder="(none)"
                      [attr.aria-label]="'Switch ' + fs.index + ' hold action'"
                      [value]="fs.hold ? actionText(fs.hold) : ''"
                      (change)="editSwitch(layout, fs, { hold: parseAction($any($event.target).value) })"
                    />
                  </xui-td>
                  <xui-td>
                    <xui-switch
                      [attr.aria-label]="'Switch ' + fs.index + ' LED'"
                      [checked]="fs.led"
                      (checkedChange)="editSwitch(layout, fs, { led: $event })"
                    />
                  </xui-td>
                  <xui-td>
                    <xui-select
                      class="w-32"
                      placeholder="(stay)"
                      [filterable]="false"
                      [attr.aria-label]="'Switch ' + fs.index + ' layout link'"
                      [items]="s.footswitches"
                      [itemText]="layoutName"
                      [value]="linkOf(fs)"
                      (valueChange)="editSwitch(layout, fs, { link: $event?.id ?? null })"
                    />
                  </xui-td>
                  <xui-td>
                    <input
                      xuiInput
                      size="sm"
                      class="w-36"
                      placeholder="(layout's)"
                      [attr.aria-label]="'Switch ' + fs.index + ' action in this preset'"
                      [value]="presetOverride(layout.id, fs.index)"
                      (change)="setOverride(layout.id, fs.index, $any($event.target).value)"
                    />
                  </xui-td>
                  <xui-td>
                    <button
                      xuiButton
                      variant="ghost"
                      size="sm"
                      color="error"
                      [attr.aria-label]="'Remove switch ' + fs.index"
                      (click)="removeSwitch(layout, fs)"
                    >
                      <ng-icon xui name="matDeleteRound" size="sm" />
                    </button>
                  </xui-td>
                </xui-tr>
              }
            </xui-table>
            <div>
              <button xuiButton variant="outline" size="sm" type="button" (click)="addSwitch(layout)">
                <ng-icon xui name="matAddRound" size="sm" /> Add switch
              </button>
            </div>
            <xui-callout minimal compact color="primary">
              Actions are written as "action target value": scene 2, sceneNext, bypass delay1, channel amp1 1, looper
              record, tap, tuner, metronome, presetNext, viewNext, layout &lt;id&gt;. A switch with a hold action acts
              on release; hold it for half a second for the hold action. A switch can also jump to another layout after
              its action, and the loaded preset can give it an action of its own (In this preset), saved with the
              preset.
            </xui-callout>
          }
        </section>
      </div>
    }
  `
})
export class GlobalPage {
  private readonly globals = inject(GlobalStore);
  private readonly catalog = inject(CatalogStore);
  private readonly connection = inject(ConnectionService);
  private readonly notify = inject(Notify);
  private readonly live = inject(LiveStore);

  protected readonly settings = this.globals.settings;
  protected readonly addView = addView;
  /** The view of the active layout being edited. */
  protected readonly editingView = signal(0);
  protected readonly views = computed(() => {
    const layout = this.activeLayout();
    return layout ? viewNames(layout) : [];
  });
  protected readonly tempoModes = [
    { label: 'Preset', value: 'preset' },
    { label: 'Global', value: 'global' },
    { label: 'MIDI clock', value: 'midiClock' }
  ];
  // Labels as items: xui-select treats a falsy value (channel 0) as empty.
  protected readonly channelNames = Array.from({ length: 17 }, (_, i) => (i === 0 ? 'All' : `Channel ${i}`));
  protected readonly Math = Math;
  protected readonly actions = [...MidiActions];
  protected readonly messages = Messages;
  protected readonly layoutName = (l: FootswitchLayout) => l.name;
  /** The global EQ is the Graphic EQ's plain ten-band type: its knobs, without the type and switch choices. */
  protected readonly eqParams = computed(() =>
    (this.catalog.info('geq')?.params ?? []).filter(p => p.kind === 'float')
  );
  protected readonly activeLayout = computed(() => {
    const s = this.settings();
    return s?.footswitches.find(l => l.id === s.activeLayout) ?? null;
  });
  protected readonly learnAction = signal<string>('scene');
  protected readonly learnTarget = signal('');
  protected readonly learnValue = signal('');
  protected readonly learning = signal(false);
  protected readonly monitor = signal<string[]>([]);

  constructor() {
    const subscription = inject(EventStream).midi$.subscribe(message => {
      if (message.type === 'midiLearned') {
        this.learning.set(false);
        this.notify.success(`Learned ${describeMidi(message)}.`);
      } else {
        this.monitor.update(lines => [describeMidi(message), ...lines].slice(0, 30));
      }
    });
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }

  protected save(change: (g: GlobalSettings) => void): void {
    void this.globals.update(change);
  }

  protected removeMapping(id: string): void {
    this.save(g => (g.midiMappings = g.midiMappings.filter(m => m.id !== id)));
  }

  protected async learn(): Promise<void> {
    const value = this.learnValue().trim();
    await this.notify.run(async () => {
      await this.connection.client.PUT('/api/v1/midi/learn', {
        body: {
          action: this.learnAction(),
          target: this.learnTarget().trim() || null,
          value: value === '' ? null : Number(value)
        }
      });
      this.learning.set(true);
    });
  }

  protected addLayout(): void {
    const id = `layout-${Date.now().toString(36)}`;
    this.save(g => {
      g.footswitches.push({ id, name: `Layout ${g.footswitches.length + 1}`, switches: [] });
      g.activeLayout = id;
    });
  }

  protected addSwitch(layout: FootswitchLayout): void {
    const index = Math.max(0, ...layout.switches.map(s => s.index)) + 1;
    this.save(g => {
      g.footswitches
        .find(l => l.id === layout.id)!
        .switches.push({
          index,
          label: `Switch ${index}`,
          message: 'cc',
          channel: 1,
          number: 19 + index,
          tap: { action: 'scene', target: null, value: index - 1 },
          hold: null,
          led: true,
          view: this.editingView(),
          link: null
        });
    });
  }

  protected editSwitch(layout: FootswitchLayout, fs: Footswitch, change: Partial<Footswitch>): void {
    this.save(g => {
      const switches = g.footswitches.find(l => l.id === layout.id)!.switches;
      const i = switches.findIndex(s => s.index === fs.index);
      switches[i] = { ...switches[i], ...change };
    });
  }

  protected switchesOnView(layout: FootswitchLayout): Footswitch[] {
    return layout.switches.filter(s => s.view === Math.min(this.editingView(), viewNames(layout).length - 1));
  }

  protected linkOf(fs: Footswitch): FootswitchLayout | null {
    return this.settings()?.footswitches.find(l => l.id === fs.link) ?? null;
  }

  protected saveLayout(layout: FootswitchLayout): void {
    this.save(g => {
      const i = g.footswitches.findIndex(l => l.id === layout.id);
      g.footswitches[i] = layout;
    });
  }

  protected dropView(layout: FootswitchLayout, view: number): void {
    this.editingView.set(0);
    this.save(g => {
      const i = g.footswitches.findIndex(l => l.id === layout.id);
      g.footswitches[i] = removeView(layout, view);
      g.activeView = 0;
    });
  }

  /** The loaded preset's own action for a switch, as text. */
  protected presetOverride(layout: string, index: number): string {
    const own = overrideOf(this.live.preset()?.footswitchOverrides ?? [], layout, index);
    return own ? this.actionText(own.tap) : '';
  }

  protected setOverride(layout: string, index: number, text: string): void {
    const overrides = this.live.preset()?.footswitchOverrides ?? [];
    void this.live.setFootswitchOverrides(withOverride(overrides, layout, index, this.parseAction(text)));
  }

  protected removeSwitch(layout: FootswitchLayout, fs: Footswitch): void {
    this.save(g => {
      const l = g.footswitches.find(x => x.id === layout.id)!;
      l.switches = l.switches.filter(s => s.index !== fs.index);
    });
  }

  protected actionText(action: SwitchAction): string {
    return [action.action, action.target, action.value].filter(p => p !== null && p !== undefined).join(' ');
  }

  /** "bypass delay1" → { action: bypass, target: delay1 }; a trailing number is the value; empty → null. */
  protected parseAction(text: string): SwitchAction | null {
    const parts = text.trim().split(/\s+/).filter(Boolean);
    if (parts.length === 0 || !(MidiActions as readonly string[]).includes(parts[0])) {
      return null;
    }

    const last = parts.length > 1 && /^\d+$/.test(parts[parts.length - 1]) ? Number(parts.pop()) : null;
    return { action: parts[0], target: parts[1] ?? null, value: last };
  }
}
