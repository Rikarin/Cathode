import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal, viewChild } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { provideIcons } from '@ng-icons/core';
import {
  matAddRound,
  matGraphicEqRound,
  matHistoryRound,
  matRedoRound,
  matSaveRound,
  matSettingsRound,
  matShowChartRound,
  matTuneRound,
  matUndoRound,
  matVolumeOffRound,
  matVolumeUpRound
} from '@ng-icons/material-icons/round';
import { XuiButtonImports } from '@xui/button';
import { XuiDialogImports } from '@xui/dialog';
import { XuiFormFieldImports } from '@xui/form-field';
import { XuiIconImports } from '@xui/icon';
import { XuiInputImports } from '@xui/input';
import { XuiKbdImports } from '@xui/kbd';
import { XuiMenuImports } from '@xui/menu';
import { XuiNavbarImports } from '@xui/navbar';
import { XuiNonIdealStateImports } from '@xui/non-ideal-state';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { XuiSpinnerImports } from '@xui/spinner';
import { XuiTextImports } from '@xui/text';
import { XuiTooltipImports } from '@xui/tooltip';
import { Notify } from './core/notify';
import { Startup } from './core/startup';
import { BatchSetDialog } from './shell/batch-set-dialog';
import { keepDialogFocusOffClose } from './shell/dialog-focus';
import { EngineMeters } from './shell/engine-meters';
import { LevelingDialog } from './shell/leveling-dialog';
import { PresetMenu } from './shell/preset-menu';
import { PresetPicker } from './shell/preset-picker';
import { PresetSceneDisplay } from './shell/preset-scene-display';
import { SaveBlockDialog } from './shell/save-block-dialog';
import { ScopeDialog } from './shell/scope-dialog';
import { ServerTargetForm } from './shell/server-target';
import { SettingsDialog } from './shell/settings-dialog';
import { ShortcutService } from './shell/shortcuts';
import { keepToolbarClicksUnfocused } from './shell/toolbar-focus';
import { Transport } from './shell/transport';
import { TunerDialog } from './shell/tuner-dialog';
import { CompareStore } from './state/compare.store';
import { LibraryStore } from './state/library.store';
import { LiveStore } from './state/live.store';
import { MidiLearnStore } from './state/midi-learn.store';
import { PresetSwitcher } from './state/preset-switcher';
import { SetlistPlayer } from './state/setlist-player';
import { UiStore } from './state/ui.store';

@Component({
  selector: 'cth-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    BatchSetDialog,
    EngineMeters,
    PresetMenu,
    PresetSceneDisplay,
    PresetPicker,
    LevelingDialog,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    SaveBlockDialog,
    ScopeDialog,
    ServerTargetForm,
    SettingsDialog,
    Transport,
    TunerDialog,
    XuiButtonImports,
    XuiDialogImports,
    XuiFormFieldImports,
    XuiIconImports,
    XuiInputImports,
    XuiKbdImports,
    XuiMenuImports,
    XuiNavbarImports,
    XuiNonIdealStateImports,
    XuiSegmentedControlImports,
    XuiSpinnerImports,
    XuiTextImports,
    XuiTooltipImports
  ],
  providers: [
    provideIcons({
      matAddRound,
      matGraphicEqRound,
      matHistoryRound,
      matRedoRound,
      matSaveRound,
      matSettingsRound,
      matShowChartRound,
      matTuneRound,
      matUndoRound,
      matVolumeOffRound,
      matVolumeUpRound
    })
  ],
  host: {
    class: 'flex h-full flex-col',
    '(document:keydown)': 'onKeydown($event)',
    '(document:keyup)': 'shortcuts.keyUp($event)',
    '(window:blur)': 'shortcuts.altHeld.set(false)'
  },
  template: `
    @switch (startup.state()) {
      @case ('starting') {
        <div class="grid h-full place-items-center">
          <xui-spinner [attr.aria-label]="remoteUrl() ? 'Connecting to ' + remoteUrl() : 'Starting the engine'" />
        </div>
      }
      @case ('failed') {
        <xui-non-ideal-state
          class="h-full"
          title="Cannot reach the Cathode server"
          [description]="startup.error() ?? ''"
        >
          <div action class="flex flex-col items-center gap-6">
            <div class="flex gap-2">
              <button xuiButton type="button" (click)="startup.start()">Try again</button>
              @if (remoteUrl()) {
                <button xuiButton type="button" variant="outline" (click)="useThisComputer()">Use this computer</button>
              }
            </div>
            @if (startup.target()) {
              <section
                class="border-border flex flex-col items-start gap-3 border-t pt-6 text-start"
                aria-labelledby="startup-server-heading"
              >
                <h2 xuiHeading id="startup-server-heading" class="text-sm">Server</h2>
                @if (remoteUrl(); as url) {
                  <p xuiText class="text-foreground-muted max-w-md text-sm">
                    This app uses the remote server at {{ url }}. Use this computer to run the engine here again, or
                    change the address or token.
                  </p>
                }
                <cth-server-target />
              </section>
            }
          </div>
        </xui-non-ideal-state>
      }
      @case ('token') {
        <xui-non-ideal-state
          class="h-full"
          title="Access token needed"
          description="This Cathode server asks for its access token. The Cathode desktop app shows it in Settings ▸ Audio setup ▸ Other devices; a headless rig keeps it as Cathode__Token in /etc/cathode/cathode.env. Or ask whoever set it up."
        >
          <form
            action
            class="flex items-center gap-2"
            (submit)="$event.preventDefault(); startup.useToken(tokenInput.value)"
          >
            <input
              #tokenInput
              xuiInput
              type="password"
              class="w-72"
              aria-label="Access token"
              data-testid="access-token"
              autocomplete="current-password"
            />
            <button xuiButton type="submit">Connect</button>
          </form>
        </xui-non-ideal-state>
      }
      @default {
        <!-- Narrower than a laptop (an iPad in portrait), the pages and the tools take a row each. -->
        <nav
          xuiNavbar
          class="chrome-bar gap-3 border-none max-lg:h-auto max-lg:flex-wrap max-lg:py-1"
          [class.window-drag]="macWindow"
          [class.ps-21]="trafficLights()"
        >
          <!-- Room inside the scroller for the links' focus rings, which it would clip at its ends (-mx keeps it in place). -->
          <div xuiNavbarGroup class="no-scrollbar -mx-1 min-w-0 gap-1 overflow-x-auto px-1 max-lg:h-10 max-lg:w-full">
            @if (!macWindow) {
              <span xuiNavbarHeading class="max-xl:hidden">Cathode</span>
              <div xuiNavbarDivider class="max-xl:hidden"></div>
            }
            <a
              xuiButton
              variant="ghost"
              size="sm"
              routerLink="/"
              routerLinkActive="bg-active-overlay"
              [routerLinkActiveOptions]="{ exact: true }"
              >Editor</a
            >
            <a xuiButton variant="ghost" size="sm" routerLink="/library" routerLinkActive="bg-active-overlay"
              >Library</a
            >
            <a xuiButton variant="ghost" size="sm" routerLink="/scenes" routerLinkActive="bg-active-overlay">Scenes</a>
            <a xuiButton variant="ghost" size="sm" routerLink="/controllers" routerLinkActive="bg-active-overlay"
              >Controllers</a
            >
            <a xuiButton variant="ghost" size="sm" routerLink="/perform" routerLinkActive="bg-active-overlay"
              >Perform</a
            >
            <a xuiButton variant="ghost" size="sm" routerLink="/setlists" routerLinkActive="bg-active-overlay"
              >Setlists</a
            >
            <a xuiButton variant="ghost" size="sm" routerLink="/global" routerLinkActive="bg-active-overlay">Global</a>
          </div>
          <div xuiNavbarGroup align="end" class="shrink-0 gap-2 max-lg:ms-auto max-lg:h-10">
            <div class="flex items-center">
              <button
                type="button"
                xuiButton
                variant="ghost"
                size="sm"
                aria-label="Undo"
                [attr.disabled]="live.state()?.canUndo ? null : ''"
                [xuiTooltip]="'Undo (⌘Z)'"
                (click)="live.undo()"
              >
                <ng-icon xui name="matUndoRound" size="sm" />
              </button>
              <button
                type="button"
                xuiButton
                variant="ghost"
                size="sm"
                aria-label="Redo"
                [attr.disabled]="live.state()?.canRedo ? null : ''"
                [xuiTooltip]="'Redo (⇧⌘Z, ⌘Y)'"
                (click)="live.redo()"
              >
                <ng-icon xui name="matRedoRound" size="sm" />
              </button>
            </div>
            <cth-transport />
            <button
              type="button"
              xuiButton
              variant="ghost"
              size="sm"
              aria-label="Tuner"
              [xuiTooltip]="'Tuner (⌘T)'"
              (click)="ui.tunerOpen.set(true)"
            >
              <ng-icon xui name="matTuneRound" size="sm" />
            </button>
            <button
              type="button"
              xuiButton
              variant="ghost"
              size="sm"
              aria-label="Analyser"
              [xuiTooltip]="'Scope and RTA'"
              (click)="scopeOpen.set(true)"
            >
              <ng-icon xui name="matShowChartRound" size="sm" />
            </button>
            <button
              type="button"
              xuiButton
              variant="ghost"
              size="sm"
              aria-label="Preset Leveling"
              data-testid="leveling"
              [xuiTooltip]="'Preset Leveling (loudness)'"
              (click)="levelingOpen.set(true)"
            >
              <ng-icon xui name="matGraphicEqRound" size="sm" />
            </button>
            <button
              type="button"
              xuiButton
              variant="ghost"
              size="sm"
              data-testid="mute"
              [attr.aria-label]="live.state()?.muted ? 'Unmute' : 'Mute'"
              [attr.aria-pressed]="live.state()?.muted ?? false"
              [class.text-error]="live.state()?.muted"
              [class.bg-error/15]="live.state()?.muted"
              [xuiTooltip]="live.state()?.muted ? 'Muted: every output is silent (M)' : 'Mute every output (M)'"
              (click)="toggleMute()"
            >
              <ng-icon xui [name]="live.state()?.muted ? 'matVolumeOffRound' : 'matVolumeUpRound'" size="sm" />
            </button>
            <cth-engine-meters />
            @if (settingsButton) {
              <button
                type="button"
                xuiButton
                variant="ghost"
                size="sm"
                aria-label="Settings"
                data-testid="preferences-button"
                [xuiTooltip]="'Settings (⌘,)'"
                (click)="ui.settings.set('preferences')"
              >
                <ng-icon xui name="matSettingsRound" size="sm" />
              </button>
            }
          </div>
        </nav>
        <div class="chrome-bar flex items-center gap-6 px-4 py-1.5">
          <div class="flex items-center gap-2">
            <span class="text-foreground-muted text-xs font-medium" aria-hidden="true">Compare</span>
            <xui-segmented-control
              class="cth-lit"
              size="sm"
              aria-label="A/B compare"
              [options]="abOptions"
              [value]="compare.active()"
              (valueChange)="$event && compare.switchTo($event)"
            />
          </div>
          <div class="flex items-center gap-2">
            <span class="text-foreground-muted text-xs font-medium" aria-hidden="true">Scene</span>
            <xui-segmented-control
              class="cth-lit grid grid-cols-4"
              size="sm"
              aria-label="Scene"
              data-testid="scenes"
              [options]="scenes()"
              [value]="live.activeScene()"
              (valueChange)="$event !== null && live.selectScene($event)"
            />
          </div>
          <cth-preset-scene-display
            class="max-w-xl min-w-0 flex-1"
            (stepPreset)="presetPicker()?.step($event)"
            (chooseBank)="presetPicker()?.loadBank($event)"
          />
          <div class="ms-auto flex shrink-0 items-center gap-1" aria-label="Preset actions">
            <button
              type="button"
              xuiButton
              variant="ghost"
              size="sm"
              aria-label="Recent presets"
              data-testid="recent-presets"
              [xuiTooltip]="'Recent presets'"
              [xuiMenuTriggerFor]="recentMenu"
            >
              <ng-icon xui name="matHistoryRound" size="sm" />
            </button>
            <ng-template #recentMenu>
              <xui-menu>
                @for (recent of switcher.recent(); track recent.id) {
                  <button type="button" xuiMenuItem (click)="switcher.load(recent.id, recent.name)">
                    {{ recent.name }}
                  </button>
                } @empty {
                  <button type="button" xuiMenuItem disabled>No recent presets</button>
                }
              </xui-menu>
            </ng-template>
            <button
              type="button"
              xuiButton
              variant="ghost"
              size="sm"
              aria-label="New preset"
              [xuiTooltip]="'New preset'"
              (click)="newPreset()"
            >
              <ng-icon xui name="matAddRound" size="sm" />
            </button>
            <button
              type="button"
              xuiButton
              variant="ghost"
              size="sm"
              aria-label="Save"
              data-testid="save"
              [xuiTooltip]="live.preset()?.factory ? 'Save a copy (factory presets are read-only, ⌘S)' : 'Save (⌘S)'"
              (click)="save()"
            >
              <ng-icon xui name="matSaveRound" size="sm" />
            </button>
            <cth-preset-menu (saveAs)="openSaveAs()" />
          </div>
        </div>
        <main class="relative min-h-0 flex-1 overflow-hidden">
          <router-outlet />
          @if (switcher.loading(); as loading) {
            <div
              class="bg-background/70 absolute inset-0 z-20 grid place-items-center backdrop-blur-[1px]"
              role="status"
              data-testid="preset-loading"
            >
              <div class="flex flex-col items-center gap-3">
                <xui-spinner aria-hidden="true" />
                <span class="text-foreground-muted text-sm">Loading “{{ loading }}”…</span>
              </div>
            </div>
          }
        </main>
        <cth-tuner-dialog [open]="ui.tunerOpen()" (openChange)="ui.tunerOpen.set($event)" />
        <cth-settings-dialog />
        <cth-preset-picker />
        <cth-save-block-dialog />
        <cth-batch-set-dialog />
        <cth-scope-dialog [(open)]="scopeOpen" />
        <cth-leveling-dialog [(open)]="levelingOpen" />
        <xui-dialog
          [title]="saveAsFirst() ? 'Save preset' : 'Save preset as'"
          icon="matSaveRound"
          [(open)]="saveAsOpen"
        >
          <xui-dialog-body>
            @if (live.preset()?.factory) {
              <p class="text-foreground-muted mb-3 text-sm">
                Factory presets are read-only: this saves your own copy, which you can change and rename.
              </p>
            } @else if (saveAsFirst()) {
              <p class="text-foreground-muted mb-3 text-sm">This preset has not been saved yet: give it a name.</p>
            }
            <xui-form-field label="Name">
              <input
                #saveAsNameField
                xuiInput
                data-testid="save-as-name"
                [value]="saveAsName()"
                (input)="saveAsName.set(saveAsNameField.value)"
                (keydown.enter)="saveAs()"
              />
            </xui-form-field>
          </xui-dialog-body>
          <xui-dialog-footer>
            <button xuiButton variant="ghost" type="button" (click)="saveAsOpen.set(false)">Cancel</button>
            <button
              xuiButton
              type="button"
              data-testid="save-as-confirm"
              [disabled]="!saveAsName().trim()"
              (click)="saveAs()"
            >
              Save
            </button>
          </xui-dialog-footer>
        </xui-dialog>
      }
    }
  `
})
export class App {
  protected readonly startup = inject(Startup);
  protected readonly live = inject(LiveStore);
  protected readonly compare = inject(CompareStore);
  protected readonly ui = inject(UiStore);
  protected readonly shortcuts = inject(ShortcutService);
  protected readonly presetPicker = viewChild(PresetPicker);
  protected readonly switcher = inject(PresetSwitcher);

  /**
   * In the macOS desktop app the navbar is the title bar: it drags the window and leaves room for the traffic lights
   * (apps/desktop/src/main.ts), which stand in for the app's name. Full screen hides them, and the room goes with them.
   */
  protected readonly macWindow = window.cathode?.platform === 'darwin';
  /** The desktop app opens Settings from its app menu (⌘,); a plain browser has no menu, so it gets a button. */
  protected readonly settingsButton = !window.cathode;
  private readonly fullScreen = signal(false);
  protected readonly trafficLights = computed(() => this.macWindow && !this.fullScreen());
  protected readonly scopeOpen = signal(false);
  protected readonly levelingOpen = signal(false);
  protected readonly saveAsOpen = signal(false);
  protected readonly saveAsName = signal('');
  protected readonly saveAsFirst = signal(false);
  private readonly library = inject(LibraryStore);
  private readonly setlists = inject(SetlistPlayer);
  private readonly notify = inject(Notify);
  protected readonly abOptions = [
    { label: 'A', value: 'A' as const },
    { label: 'B', value: 'B' as const }
  ];
  protected readonly scenes = computed(() =>
    (this.live.preset()?.scenes ?? []).map((scene, index) => ({ label: String(index + 1), value: index }))
  );

  constructor() {
    window.cathode?.onFullScreen(fullScreen => this.fullScreen.set(fullScreen));
    window.cathode?.onOpenSettings?.(() => this.ui.settings.set('preferences'));
    // Quitting the desktop app takes its Server, and the unsaved edits with it: ask first, as loading a preset does.
    window.cathode?.onQuit?.(() => this.switcher.mayDiscard('quit'));
    void this.startup.start();
    // Started with the shell, so an armed MIDI learn shows (with Cancel) on any page, after a reload too.
    inject(MidiLearnStore);
    inject(DestroyRef).onDestroy(this.registerShortcuts());
    // A dialog opened from the keyboard starts on itself, not with a focus ring on its Close button.
    inject(DestroyRef).onDestroy(keepDialogFocusOffClose(document));
    // The top bars' buttons act on a click without taking focus, as a macOS toolbar's do.
    inject(DestroyRef).onDestroy(keepToolbarClicksUnfocused(document));
  }

  /** The remote Server's URL when the desktop app uses one. */
  protected readonly remoteUrl = computed(() => {
    const target = this.startup.target();
    return target?.mode === 'remote' ? target.baseUrl : null;
  });

  /** Back to this computer's own engine (the window reloads on it). */
  protected async useThisComputer(): Promise<void> {
    const desktop = window.cathode;
    if (desktop) {
      await this.notify.run(() => desktop.setTarget({ mode: 'local' }));
    }
  }

  protected newPreset(): void {
    void this.switcher.newPreset();
  }

  /** Mutes or unmutes every output (the navbar button, M, or a footswitch on the rig). */
  protected toggleMute(): void {
    void this.live.setMuted(!this.live.state()?.muted);
  }

  /**
   * Save asks for a name, as Save as… does, when the preset has none to keep: a factory preset (read-only, so the user
   * names their own copy), and a preset never saved before or without a name (a new one).
   */
  protected async save(): Promise<void> {
    const preset = this.live.preset();
    if (!preset) {
      return;
    }

    if (preset.factory) {
      this.openSaveAs();
      return;
    }

    // Whether it was ever saved needs the Server: when it cannot answer, say so rather than doing nothing.
    const stored = preset.name.trim() ? await this.notify.run(() => this.library.isStored(preset.id)) : false;
    if (stored === undefined) {
      return;
    }

    if (stored) {
      void this.live.save();
    } else {
      this.openSaveAs(true);
    }
  }

  /** Opens Save as… with the preset's name; `first` when it has never been saved, which the dialog says. */
  protected openSaveAs(first = false): void {
    this.saveAsFirst.set(first);
    this.saveAsName.set(this.live.preset()?.name ?? '');
    this.saveAsOpen.set(true);
  }

  protected async saveAs(): Promise<void> {
    const name = this.saveAsName().trim();
    if (name && (await this.live.save(name, true))) {
      this.saveAsOpen.set(false);
    }
  }

  /**
   * Hands key presses to the shortcuts. It returns nothing on purpose: an Angular listener that returns false
   * cancels the event, which would stop ⌘A and typing everywhere a shortcut does not apply.
   */
  protected onKeydown(event: KeyboardEvent): void {
    this.shortcuts.dispatch(event);
  }

  /** The shortcuts that work on every page (docs/02-architecture.md §7.5); the grid adds the cell commands. */
  private registerShortcuts(): () => void {
    const ui = this.ui;
    const block = () => ui.block();
    const handlers: Record<string, () => void> = {
      presetPicker: () => ui.presetPicker.set(true),
      presetPrevious: () => void this.presetPicker()?.step(-1),
      presetNext: () => void this.presetPicker()?.step(1),
      songPrevious: () => void this.setlists.step(-1),
      songNext: () => void this.setlists.step(1),
      reload: () => void this.presetPicker()?.reload(),
      save: () => void this.save(), // save() reports its own failures
      saveAs: () => this.openSaveAs(),
      level: () => this.levelingOpen.set(true),
      channelReset: () => {
        const selected = block();
        if (selected && ui.channels(selected) > 0) {
          void this.live.resetChannel(selected.id, this.live.channelOf(selected.id));
        }
      },
      channelNext: () => ui.nextChannel(),
      bypass: () => ui.toggleBypass(),
      engageAll: () => ui.allScenes({ bypassed: false }),
      bypassAll: () => ui.allScenes({ bypassed: true }),
      global: () => ui.makeGlobal(),
      saveBlock: () => block() && ui.saveBlockOpen.set(true),
      blockInfo: () => this.shortcuts.blockInfo.update(on => !on),
      undo: () => void this.live.undo(),
      redo: () => void this.live.redo(),
      redoAlias: () => void this.live.redo(),
      mute: () => this.toggleMute(),
      tuner: () => ui.tunerOpen.update(open => !open),
      settings: () => ui.settings.update(tab => (tab === null ? 'preferences' : null)),
      help: () => ui.settings.update(tab => (tab === 'shortcuts' ? null : 'shortcuts'))
    };
    for (const channel of ['A', 'B', 'C', 'D'] as const) {
      handlers[`channel${channel}`] = () => ui.setChannel(channel);
    }

    for (let scene = 0; scene < 8; scene++) {
      handlers[`scene${scene + 1}`] = () => {
        if (scene < (this.live.preset()?.scenes.length ?? 0)) {
          void this.live.selectScene(scene);
        }
      };
    }

    return this.shortcuts.register(handlers);
  }
}
