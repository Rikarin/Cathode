import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import {
  type Footswitch,
  type FootswitchLayout,
  type FootswitchOverride,
  type GlobalSettings,
  type MidiActivity,
  type MidiOutputInfo,
  type OutputModes,
  type OutputSettings,
  type OversamplingModes,
  type SwitchAction,
  unwrap
} from '@cathode/api-client';
import { CthKnob } from '@cathode/audio-ui';
import { provideIcons } from '@ng-icons/core';
import { matAddRound, matDeleteRound, matSettingsInputComponentRound } from '@ng-icons/material-icons/round';
import { XuiAlertDialogImports } from '@xui/alert-dialog';
import { XuiButtonImports } from '@xui/button';
import { XuiButtonGroupImports } from '@xui/button-group';
import { XuiCalloutImports } from '@xui/callout';
import { XuiCardImports } from '@xui/card';
import { XuiIconImports } from '@xui/icon';
import { XuiInputImports } from '@xui/input';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { XuiSelectImports } from '@xui/select';
import { XuiSwitchImports } from '@xui/switch';
import { XuiTableImports } from '@xui/table';
import { XuiTextImports } from '@xui/text';
import { NumberSteppers } from '../controls/number-steppers';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';
import { CatalogStore } from '../state/catalog.store';
import { GlobalStore } from '../state/global.store';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';
import { MidiLearnStore, learnText } from '../state/midi-learn.store';
import { ActionPicker, injectActionContext, targetLabel } from './action-picker';
import {
  type OverrideChange,
  addView,
  overrideOf,
  removeLayout,
  removeView,
  shownValue,
  viewNames,
  withOverride
} from './footswitches';

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

/** The MIDI output choice that sends to every output (xui-select takes an empty value for none). */
export const AllMidiOutputs = 'all';

/**
 * The MIDI output choices: every output, then each output by uid, then the saved port when it is not connected now (so
 * the setting shows, and stays, until another is chosen).
 */
export function midiOutputChoices(outputs: readonly MidiOutputInfo[], saved: string | null | undefined): string[] {
  const uids = outputs.map(o => o.uid);
  return [AllMidiOutputs, ...uids, ...(saved && !uids.includes(saved) ? [saved] : [])];
}

/** A MIDI output choice's label: the port's name, or its uid when it is not connected. */
export function midiOutputName(outputs: readonly MidiOutputInfo[], choice: string): string {
  return choice === AllMidiOutputs
    ? 'All outputs'
    : (outputs.find(o => o.uid === choice)?.name ?? `${choice} (not connected)`);
}

/**
 * Global settings: where the tempo comes from, the metronome, the outputs (mode, global EQ and Boost/Pad each), Input 1
 * Gain and the oversampling filters, and MIDI: the channel, mappings
 * (with MIDI learn and a live monitor) and footswitch layouts for generic MIDI foot controllers.
 */
@Component({
  selector: 'cth-global-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ActionPicker,
    CthKnob,
    NumberSteppers,
    XuiAlertDialogImports,
    XuiButtonGroupImports,
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
      <div class="mx-auto flex max-w-6xl flex-col gap-6">
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

        <section xuiCard class="flex flex-col gap-4 p-4" aria-labelledby="presets-heading">
          <h2 xuiHeading id="presets-heading" class="text-sm">Presets and scenes</h2>
          <div class="flex flex-wrap items-end gap-6">
            <div class="flex flex-col gap-1">
              <span class="text-foreground-muted text-xs" id="default-scene-label">Scene on preset load</span>
              <xui-select
                class="w-40"
                data-testid="default-scene"
                aria-labelledby="default-scene-label"
                [filterable]="false"
                [items]="defaultScenes"
                [itemText]="defaultSceneName"
                [value]="String(s.defaultScene)"
                (valueChange)="$event !== null && save(g => (g.defaultScene = +$event))"
              />
            </div>
            <div class="flex flex-col gap-1">
              <span class="text-foreground-muted text-xs" id="startup-preset-label">Startup preset</span>
              <xui-select
                class="w-56"
                data-testid="startup-preset"
                aria-labelledby="startup-preset-label"
                [items]="startupChoices()"
                [itemText]="startupName"
                [value]="s.startupPreset ?? 'none'"
                (valueChange)="save(g => (g.startupPreset = !$event || $event === 'none' ? null : $event))"
              />
            </div>
            @for (option of presetSwitches; track option.key) {
              <label class="flex flex-col items-center gap-2" [attr.title]="option.help">
                <span class="text-foreground-muted text-xs">{{ option.label }}</span>
                <xui-switch
                  [attr.data-testid]="'global-' + option.key"
                  [attr.aria-label]="option.label"
                  [checked]="s[option.key]"
                  (checkedChange)="save(g => (g[option.key] = $event))"
                />
              </label>
            }
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

        <section xuiCard class="flex flex-col gap-4 p-4" aria-labelledby="cabs-heading">
          <h2 xuiHeading id="cabs-heading" class="text-sm">Cab modeling</h2>
          <p xuiText class="text-foreground-muted text-xs">
            For playing through a real speaker cabinet: every Cab and IR Player block passes its input straight through,
            without changing any preset.
          </p>
          <label class="flex items-center gap-2 text-sm">
            <xui-switch
              aria-label="Cab modeling off"
              data-testid="cab-modeling-off"
              [checked]="s.cabModelingOff"
              (checkedChange)="save(g => (g.cabModelingOff = $event))"
            />
            Cab modeling off
          </label>
        </section>

        <section xuiCard class="flex flex-col gap-4 p-4" aria-labelledby="outputs-heading">
          <h2 xuiHeading id="outputs-heading" class="text-sm">Outputs</h2>
          <p xuiText class="text-foreground-muted text-xs">
            Each output has its own mode, global EQ and Boost/Pad, applied to every preset. Outputs 2-4 play what the
            grid's Output 2-4 blocks get, on the device channels chosen in Setup.
          </p>
          <div class="flex flex-wrap items-end gap-6">
            <div class="flex flex-col gap-1">
              <span class="text-foreground-muted text-xs">Output</span>
              <xui-segmented-control
                size="sm"
                aria-label="Output to edit"
                [options]="outputOptions"
                [value]="output()"
                (valueChange)="output.set($event ?? 0)"
              />
            </div>
            @if (s.outputs[output()]; as out) {
              <div class="flex flex-col gap-1">
                <span class="text-foreground-muted text-xs">Mode</span>
                <xui-segmented-control
                  size="sm"
                  aria-label="Output mode"
                  [options]="modeOptions"
                  [value]="out.mode"
                  (valueChange)="saveOutput(o => (o.mode = $event ?? 'stereo'))"
                />
              </div>
              <cth-knob
                label="Boost/Pad"
                unit="dB"
                [min]="-18"
                [max]="18"
                [defaultValue]="0"
                [value]="out.boostPad"
                (committed)="saveOutput(o => (o.boostPad = $event))"
              />
            }
            <label class="flex flex-col items-center gap-2">
              <span class="text-foreground-muted text-xs">Copy Output 1 to 2</span>
              <xui-switch
                aria-label="Copy Output 1 to Output 2"
                [checked]="s.copyOutput1To2"
                (checkedChange)="save(g => (g.copyOutput1To2 = $event))"
              />
            </label>
          </div>
          <span class="text-foreground-muted text-xs">Output {{ output() + 1 }} global EQ</span>
          <div class="flex flex-wrap gap-4">
            @if (s.outputs[output()]; as out) {
              @for (param of eqParams(); track param.id) {
                <cth-knob
                  size="sm"
                  [label]="param.name"
                  [unit]="param.unit"
                  [min]="param.min"
                  [max]="param.max"
                  [defaultValue]="param.default"
                  [value]="out.eq[param.id] ?? param.default"
                  (committed)="saveOutput(o => (o.eq[param.id] = $event))"
                />
              }
            }
          </div>
          @if (s.copyOutput1To2) {
            <xui-callout minimal compact color="primary">
              Output 2 plays Output 1 (before Output 1's EQ and Boost/Pad) in presets without an Output 2 block.
            </xui-callout>
          }
        </section>

        <section xuiCard class="flex flex-col gap-4 p-4" aria-labelledby="input-heading">
          <h2 xuiHeading id="input-heading" class="text-sm">Input and oversampling</h2>
          <div class="flex flex-wrap items-end gap-6">
            @if (s.inputs[0]; as input1) {
              <cth-knob
                label="Input 1 Gain"
                unit="dB"
                [min]="-20"
                [max]="20"
                [defaultValue]="0"
                [value]="input1.gain"
                (committed)="saveInput1(i => (i.gain = $event))"
              />
            }
            <div class="flex flex-col gap-1">
              <span class="text-foreground-muted text-xs">Oversampling (Amp, Drive, Dynamic Distortion)</span>
              <xui-segmented-control
                size="sm"
                aria-label="Oversampling filters"
                [options]="oversamplingOptions"
                [value]="s.oversampling"
                (valueChange)="save(g => (g.oversampling = $event ?? 'minimum'))"
              />
            </div>
            @if (s.inputs[0]; as input1) {
              <span class="text-foreground-muted text-xs">
                Input 1 full scale: {{ input1.fullScaleVolts.toFixed(2) }} V (calibrate in Setup)
              </span>
            }
          </div>
          <p xuiText class="text-foreground-muted text-xs">
            Input 1 Gain trims what every preset gets, after the DI out. Minimum phase adds no latency; linear phase
            delays every frequency alike, at about 2 ms more latency.
          </p>
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
            <label class="flex flex-col gap-1">
              <span class="text-foreground-muted text-xs">Send to</span>
              <xui-select
                class="w-56"
                aria-label="MIDI output"
                data-testid="midi-output"
                [filterable]="false"
                [items]="midiOutputChoices()"
                [itemText]="midiOutputName"
                [value]="s.midiOutPort ?? allMidiOutputs"
                (valueChange)="save(g => (g.midiOutPort = !$event || $event === allMidiOutputs ? null : $event))"
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
                <xui-td>{{ targetLabel(mapping.action, mapping.target, actionContext()) }}</xui-td>
                <xui-td>{{ shownValue(mapping.action, mapping.value) ?? '' }}</xui-td>
                <xui-td>
                  <button
                    type="button"
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
            <div class="flex w-full max-w-xl flex-col gap-1">
              <span class="text-foreground-muted text-xs">Action to learn (and its target and value)</span>
              <cth-action-picker
                label="Learn"
                [fromMessage]="true"
                [action]="learnAction()"
                (actionChange)="$event && learnAction.set($event)"
              />
            </div>
            <button xuiButton type="button" [loading]="learning()" (click)="learn()">
              <ng-icon xui name="matSettingsInputComponentRound" size="sm" />
              {{ learning() ? 'Move a control…' : 'MIDI learn' }}
            </button>
            @if (midiLearn.armed(); as armed) {
              <button xuiButton type="button" variant="outline" (click)="midiLearn.cancel()">Cancel learn</button>
              <span class="text-foreground-muted self-center text-xs" data-testid="midi-learn-armed"
                >Learning: {{ learnText(armed) }}</span
              >
            }
          </div>

          <!--
            Not a live region: a stream of CCs would be read out without end. Drawn as the preset display is, on LCD
            glass, as a hardware MIDI monitor reads.
          -->
          <div>
            <span id="midi-monitor-label" class="section-label">Monitor</span>
            <ul
              class="lcd-glass text-lcd-foreground mt-1.5 h-28 overflow-auto px-3 py-2 font-mono text-xs leading-5"
              tabindex="0"
              aria-labelledby="midi-monitor-label"
            >
              @for (line of monitor(); track $index) {
                <li>{{ line }}</li>
              } @empty {
                <li class="text-lcd-muted">Nothing received yet.</li>
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
            @if (activeLayout()) {
              <button
                xuiButton
                variant="outline"
                color="error"
                type="button"
                data-testid="delete-layout"
                (click)="confirmDeleteLayout.set(true)"
              >
                <ng-icon xui name="matDeleteRound" size="sm" /> Delete layout…
              </button>
            }
          </div>
          @if (activeLayout(); as layout) {
            <div class="flex flex-wrap items-center gap-2" data-testid="footswitch-views">
              <span class="text-foreground-muted text-xs">Views</span>
              @for (view of views(); track $index; let i = $index) {
                <!-- The view, and while it is the one edited (and not the only one) its red remove key joined to it. -->
                <span xuiButtonGroup>
                  <button
                    type="button"
                    xuiButton
                    size="sm"
                    [variant]="i === editingView() ? 'default' : 'outline'"
                    (click)="editingView.set(i)"
                  >
                    {{ view }}{{ i === s.activeView ? ' (active)' : '' }}
                  </button>
                  @if (views().length > 1 && i === editingView()) {
                    <button
                      type="button"
                      xuiButton
                      size="sm"
                      color="error"
                      [attr.aria-label]="'Remove view ' + view"
                      (click)="dropView(layout, i)"
                    >
                      <ng-icon xui name="matDeleteRound" size="sm" />
                    </button>
                  }
                </span>
              }
              <button type="button" xuiButton size="sm" variant="ghost" (click)="saveLayout(addView(layout))">
                <ng-icon xui name="matAddRound" size="sm" /> View
              </button>
              @if (editingView() !== s.activeView) {
                <button
                  type="button"
                  xuiButton
                  size="sm"
                  variant="ghost"
                  (click)="save(g => (g.activeView = editingView()))"
                >
                  Make active
                </button>
              }
            </div>
            <div role="table" aria-label="Footswitches on this view" class="flex flex-col gap-1">
              <div
                role="row"
                class="text-foreground-muted grid grid-cols-[7rem_13.5rem_minmax(11rem,1.5fr)_minmax(11rem,1.5fr)_2.5rem_minmax(7rem,9rem)_minmax(11rem,1.5fr)_2rem] items-center gap-2 px-1 text-xs font-medium"
              >
                <span role="columnheader">Switch</span>
                <span role="columnheader">Sends</span>
                <span role="columnheader">Tap</span>
                <span role="columnheader">Hold</span>
                <span role="columnheader">LED</span>
                <span role="columnheader">Then layout</span>
                <span role="columnheader">In this preset</span>
                <span role="columnheader"><span class="sr-only">Remove</span></span>
              </div>
              @for (fs of switchesOnView(layout); track fs.index) {
                <div
                  role="row"
                  class="bg-surface-raised grid grid-cols-[7rem_13.5rem_minmax(11rem,1.5fr)_minmax(11rem,1.5fr)_2.5rem_minmax(7rem,9rem)_minmax(11rem,1.5fr)_2rem] items-center gap-2 rounded-md px-1 py-1.5"
                >
                  <div role="cell" class="min-w-0">
                    <input
                      #labelField
                      xuiInput
                      size="sm"
                      class="w-full"
                      [attr.aria-label]="'Switch ' + fs.index + ' label'"
                      [value]="fs.label"
                      (change)="editSwitch(layout, fs, { label: labelField.value })"
                    />
                  </div>
                  <div role="cell" class="flex items-center gap-1">
                    <xui-select
                      class="w-20"
                      [filterable]="false"
                      [items]="messages"
                      [value]="fs.message"
                      [attr.aria-label]="'Switch ' + fs.index + ' message'"
                      (valueChange)="editSwitch(layout, fs, { message: $event ?? 'cc' })"
                    />
                    <input
                      #numberField
                      xuiInput
                      size="sm"
                      type="number"
                      cthSteppers
                      class="w-16"
                      [attr.aria-label]="'Switch ' + fs.index + ' number'"
                      [value]="fs.number"
                      (change)="editSwitch(layout, fs, { number: +numberField.value })"
                    />
                    <input
                      #channelField
                      xuiInput
                      size="sm"
                      type="number"
                      cthSteppers
                      class="w-14"
                      [attr.aria-label]="'Switch ' + fs.index + ' channel'"
                      [value]="fs.channel"
                      (change)="editSwitch(layout, fs, { channel: +channelField.value })"
                    />
                  </div>
                  <div role="cell" class="min-w-0">
                    <cth-action-picker
                      [label]="'Switch ' + fs.index + ' tap'"
                      [layout]="layout.id"
                      [action]="fs.tap"
                      (actionChange)="$event && editSwitch(layout, fs, { tap: $event })"
                    />
                  </div>
                  <div role="cell" class="min-w-0">
                    <cth-action-picker
                      [label]="'Switch ' + fs.index + ' hold'"
                      [optional]="true"
                      [layout]="layout.id"
                      [action]="fs.hold"
                      (actionChange)="editSwitch(layout, fs, { hold: $event })"
                    />
                  </div>
                  <div role="cell" class="min-w-0">
                    <xui-switch
                      [attr.aria-label]="'Switch ' + fs.index + ' LED'"
                      [checked]="fs.led"
                      (checkedChange)="editSwitch(layout, fs, { led: $event })"
                    />
                  </div>
                  <div role="cell" class="min-w-0">
                    <xui-select
                      class="w-full"
                      placeholder="(stay)"
                      [filterable]="false"
                      [attr.aria-label]="'Switch ' + fs.index + ' layout link'"
                      [items]="s.footswitches"
                      [itemText]="layoutName"
                      [value]="linkOf(fs)"
                      (valueChange)="editSwitch(layout, fs, { link: $event?.id ?? null })"
                    />
                  </div>
                  <div role="cell" class="flex min-w-0 flex-col gap-1">
                    <cth-action-picker
                      [label]="'Switch ' + fs.index + ' in this preset'"
                      [optional]="true"
                      [layout]="layout.id"
                      [action]="overrideOf(layout.id, fs.index)?.tap ?? null"
                      (actionChange)="setOverride(layout.id, fs.index, { tap: $event })"
                    />
                    @if (overrideOf(layout.id, fs.index); as own) {
                      <cth-action-picker
                        [label]="'Switch ' + fs.index + ' hold in this preset'"
                        [optional]="true"
                        [layout]="layout.id"
                        [action]="own.hold"
                        (actionChange)="setOverride(layout.id, fs.index, { hold: $event })"
                      />
                      <input
                        #overrideLabelField
                        xuiInput
                        size="sm"
                        class="w-full"
                        [placeholder]="'label: ' + (fs.label || 'none')"
                        [attr.aria-label]="'In this preset, label of switch ' + fs.index"
                        [value]="own.label ?? ''"
                        (change)="setOverride(layout.id, fs.index, { label: overrideLabelField.value })"
                      />
                    }
                  </div>
                  <div role="cell" class="min-w-0">
                    <button
                      type="button"
                      xuiButton
                      variant="ghost"
                      size="sm"
                      color="error"
                      [attr.aria-label]="'Remove switch ' + fs.index"
                      (click)="removeSwitch(layout, fs)"
                    >
                      <ng-icon xui name="matDeleteRound" size="sm" />
                    </button>
                  </div>
                </div>
              }
            </div>
            <div>
              <button xuiButton variant="outline" size="sm" type="button" (click)="addSwitch(layout)">
                <ng-icon xui name="matAddRound" size="sm" /> Add switch
              </button>
            </div>
            <xui-callout minimal compact color="primary">
              Pick each action from the list, then what it acts on: a block of the loaded preset, a bank and its slot, a
              looper command, a layout, a scene, channel, Manual, External or view. A switch with a hold action acts on
              release; hold it for half a second for the hold action. A switch can also jump to another layout after its
              action, and the loaded preset can give it an action of its own (In this preset), saved with the preset;
              once it has one, the preset can also give it a hold action ((none) for none) and a label of its own.
            </xui-callout>
          }
        </section>
      </div>
    }

    <xui-alert-dialog
      destructive
      [title]="'Delete layout “' + (activeLayout()?.name ?? '') + '”?'"
      description="Its switches and views go with it, and the first layout left becomes the active one. Switches that go on to this layout no longer do; a MIDI mapping or a switch whose action changes to it does nothing until you point it at another layout."
      confirmText="Delete"
      [(open)]="confirmDeleteLayout"
      (confirmed)="deleteLayout()"
    />
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
  protected readonly shownValue = shownValue;
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
  protected readonly defaultScenes = ['-1', '0', '1', '2', '3', '4', '5', '6', '7'];
  protected readonly String = String;
  protected readonly defaultSceneName = (scene: string) => (+scene < 0 ? 'As saved' : `Scene ${+scene + 1}`);
  protected readonly presetSwitches: {
    key: 'sceneRevert' | 'ignoreRedundantPc' | 'presetSpillover' | 'confirmPresetChange';
    label: string;
    help: string;
  }[] = [
    {
      key: 'sceneRevert',
      label: 'Scene Revert',
      help: 'On: a scene always comes back as saved. Off: changes made while playing stay with their scene.'
    },
    {
      key: 'ignoreRedundantPc',
      label: 'Ignore redundant PC',
      help: 'A program change for the preset already loaded does nothing.'
    },
    {
      key: 'presetSpillover',
      label: 'Preset spillover',
      help: 'On: delay and reverb tails ring out across a preset change. Off: a clean cut.'
    },
    {
      key: 'confirmPresetChange',
      label: 'Confirm discarding edits',
      help: 'Ask before changing presets while the current one has unsaved edits.'
    }
  ];
  private readonly library = inject(LibraryStore);
  protected readonly startupChoices = computed(() => ['none', ...this.library.presets().map(p => p.id)]);
  protected readonly startupName = (id: string) =>
    id === 'none' ? 'None (an empty preset)' : (this.library.presets().find(p => p.id === id)?.name ?? id);
  protected readonly messages = Messages;
  protected readonly layoutName = (l: FootswitchLayout) => l.name;
  /** The global EQ is the Graphic EQ's plain ten-band type: its knobs, without the type and switch choices. */
  protected readonly eqParams = computed(() =>
    (this.catalog.info('geq')?.params ?? []).filter(p => p.kind === 'float')
  );
  protected readonly output = signal(0);
  protected readonly outputOptions = [0, 1, 2, 3].map(k => ({ label: String(k + 1), value: k }));
  protected readonly modeOptions: { label: string; value: (typeof OutputModes)[number] }[] = [
    { label: 'Stereo', value: 'stereo' },
    { label: 'Sum L+R', value: 'sum' },
    { label: 'Copy L→R', value: 'copyLR' }
  ];
  protected readonly oversamplingOptions: { label: string; value: (typeof OversamplingModes)[number] }[] = [
    { label: 'Minimum phase', value: 'minimum' },
    { label: 'Linear phase', value: 'linear' }
  ];
  protected readonly activeLayout = computed(() => {
    const s = this.settings();
    return s?.footswitches.find(l => l.id === s.activeLayout) ?? null;
  });
  /** What MIDI learn will map: a scene, its number from the message (a CC's value picks the scene). */
  protected readonly learnAction = signal<SwitchAction>({ action: 'scene', target: null, value: null });
  /** Names targets in the mappings table: blocks, banks and layouts by name. */
  protected readonly actionContext = injectActionContext();
  protected readonly targetLabel = targetLabel;
  protected readonly midiLearn = inject(MidiLearnStore);
  protected readonly learning = computed(() => this.midiLearn.armed() !== null);
  protected readonly learnText = learnText;
  protected readonly monitor = signal<string[]>([]);
  /** The engine host's MIDI outputs, for where scene MIDI and footswitch LEDs go. */
  protected readonly midiOutputs = signal<MidiOutputInfo[]>([]);
  protected readonly allMidiOutputs = AllMidiOutputs;
  protected readonly midiOutputChoices = computed(() =>
    midiOutputChoices(this.midiOutputs(), this.settings()?.midiOutPort)
  );
  protected readonly midiOutputName = (choice: string) => midiOutputName(this.midiOutputs(), choice);

  constructor() {
    void this.library.refreshPresets();
    void this.refreshMidiOutputs();
    const subscription = inject(EventStream).midi$.subscribe(message => {
      if (message.type === 'midiLearned') {
        this.notify.success(`Learned ${describeMidi(message)}.`);
      } else if (message.type === 'midi') {
        this.monitor.update(lines => [describeMidi(message), ...lines].slice(0, 30));
      }
    });
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }

  private async refreshMidiOutputs(): Promise<void> {
    await this.notify.run(async () =>
      this.midiOutputs.set(unwrap(await this.connection.client.GET('/api/v1/midi/outputs')))
    );
  }

  protected save(change: (g: GlobalSettings) => void): void {
    void this.globals.update(change);
  }

  /** Changes the output being edited. */
  protected saveOutput(change: (o: OutputSettings) => void): void {
    const k = this.output();
    this.save(g => {
      const output = g.outputs[k];
      if (output) {
        change(output);
      }
    });
  }

  /** Changes Input 1's settings. */
  protected saveInput1(change: (input: GlobalSettings['inputs'][number]) => void): void {
    this.save(g => {
      const input = g.inputs[0];
      if (input) {
        change(input);
      }
    });
  }

  protected removeMapping(id: string): void {
    this.save(g => (g.midiMappings = g.midiMappings.filter(m => m.id !== id)));
  }

  protected async learn(): Promise<void> {
    const { action, target, value } = this.learnAction();
    await this.midiLearn.arm({ action, target: target ?? null, value: value ?? null, modifier: null });
  }

  protected readonly confirmDeleteLayout = signal(false);

  /** Deletes the active footswitch layout (removeLayout says what becomes of the rest). */
  protected deleteLayout(): void {
    const id = this.activeLayout()?.id;
    if (id) {
      this.editingView.set(0);
      this.save(g => Object.assign(g, removeLayout(g, id)));
    }
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
        .find(l => l.id === layout.id)
        ?.switches.push({
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
      const switches = g.footswitches.find(l => l.id === layout.id)?.switches ?? [];
      const i = switches.findIndex(s => s.index === fs.index);
      const current = switches[i];
      if (current) {
        switches[i] = { ...current, ...change };
      }
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

  /** The loaded preset's override for a switch: its hold action and label are edited once it has a tap. */
  protected overrideOf(layout: string, index: number): FootswitchOverride | undefined {
    return overrideOf(this.live.preset()?.footswitchOverrides ?? [], layout, index);
  }

  protected setOverride(layout: string, index: number, change: OverrideChange): void {
    const overrides = this.live.preset()?.footswitchOverrides ?? [];
    void this.live.setFootswitchOverrides(withOverride(overrides, layout, index, change));
  }

  protected removeSwitch(layout: FootswitchLayout, fs: Footswitch): void {
    this.save(g => {
      const l = g.footswitches.find(x => x.id === layout.id);
      if (l) {
        l.switches = l.switches.filter(s => s.index !== fs.index);
      }
    });
  }
}
