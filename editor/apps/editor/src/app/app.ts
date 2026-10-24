import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal, viewChild } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { provideIcons } from '@ng-icons/core';
import {
  matAddRound,
  matKeyboardRound,
  matRedoRound,
  matSaveRound,
  matSettingsRound,
  matShowChartRound,
  matTuneRound,
  matUndoRound
} from '@ng-icons/material-icons/round';
import { XuiButtonImports } from '@xui/button';
import { XuiDialogImports } from '@xui/dialog';
import { XuiFormFieldImports } from '@xui/form-field';
import { XuiIconImports } from '@xui/icon';
import { XuiInputImports } from '@xui/input';
import { XuiKbdImports } from '@xui/kbd';
import { XuiNavbarImports } from '@xui/navbar';
import { XuiNonIdealStateImports } from '@xui/non-ideal-state';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { XuiSpinnerImports } from '@xui/spinner';
import { XuiTooltipImports } from '@xui/tooltip';
import { Notify } from './core/notify';
import { Startup } from './core/startup';
import { BatchSetDialog } from './shell/batch-set-dialog';
import { EngineMeters } from './shell/engine-meters';
import { PreferencesDialog } from './shell/preferences-dialog';
import { PresetMenu } from './shell/preset-menu';
import { PresetPicker } from './shell/preset-picker';
import { SaveBlockDialog } from './shell/save-block-dialog';
import { ScopeDialog } from './shell/scope-dialog';
import { ShortcutHelp } from './shell/shortcut-help';
import { ShortcutService } from './shell/shortcuts';
import { Transport } from './shell/transport';
import { TunerDialog } from './shell/tuner-dialog';
import { CompareStore } from './state/compare.store';
import { LiveStore } from './state/live.store';
import { UiStore } from './state/ui.store';

@Component({
  selector: 'cth-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    BatchSetDialog,
    EngineMeters,
    PreferencesDialog,
    PresetMenu,
    PresetPicker,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    SaveBlockDialog,
    ScopeDialog,
    ShortcutHelp,
    Transport,
    TunerDialog,
    XuiButtonImports,
    XuiDialogImports,
    XuiFormFieldImports,
    XuiIconImports,
    XuiInputImports,
    XuiKbdImports,
    XuiNavbarImports,
    XuiNonIdealStateImports,
    XuiSegmentedControlImports,
    XuiSpinnerImports,
    XuiTooltipImports
  ],
  providers: [
    provideIcons({
      matAddRound,
      matKeyboardRound,
      matRedoRound,
      matSaveRound,
      matSettingsRound,
      matShowChartRound,
      matTuneRound,
      matUndoRound
    })
  ],
  host: {
    class: 'flex h-full flex-col',
    '(document:keydown)': 'shortcuts.dispatch($event)',
    '(document:keyup)': 'shortcuts.keyUp($event)',
    '(window:blur)': 'shortcuts.altHeld.set(false)'
  },
  template: `
    @switch (startup.state()) {
      @case ('starting') {
        <div class="grid h-full place-items-center"><xui-spinner aria-label="Starting the engine" /></div>
      }
      @case ('failed') {
        <xui-non-ideal-state
          class="h-full"
          title="Cannot reach the Cathode server"
          [description]="startup.error() ?? ''"
        >
          <button action xuiButton type="button" (click)="startup.start()">Try again</button>
        </xui-non-ideal-state>
      }
      @default {
        <nav xuiNavbar class="gap-3" [class.window-drag]="macWindow" [class.ps-21]="trafficLights()">
          <div xuiNavbarGroup class="gap-1">
            @if (!macWindow) {
              <span xuiNavbarHeading>Cathode</span>
              <div xuiNavbarDivider></div>
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
            <a xuiButton variant="ghost" size="sm" routerLink="/controllers" routerLinkActive="bg-active-overlay"
              >Controllers</a
            >
            <a xuiButton variant="ghost" size="sm" routerLink="/perform" routerLinkActive="bg-active-overlay"
              >Perform</a
            >
            <a xuiButton variant="ghost" size="sm" routerLink="/irs" routerLinkActive="bg-active-overlay">IRs</a>
            <a xuiButton variant="ghost" size="sm" routerLink="/global" routerLinkActive="bg-active-overlay">Global</a>
            <a xuiButton variant="ghost" size="sm" routerLink="/setup" routerLinkActive="bg-active-overlay">Setup</a>
            <div xuiNavbarDivider></div>
            <button
              type="button"
              class="hover:bg-active-overlay max-w-56 truncate rounded px-1 text-sm font-medium"
              data-testid="preset-name"
              [xuiTooltip]="'Presets (P)'"
              (click)="ui.presetPicker.set('browse')"
            >
              {{ live.preset()?.name || '(untitled)' }}
              @if (live.state()?.dirty) {
                <span class="text-warning" aria-label="unsaved changes"> •</span>
              }
            </button>
            <button
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
              xuiButton
              variant="ghost"
              size="sm"
              aria-label="Save"
              data-testid="save"
              [xuiTooltip]="'Save (⌘S)'"
              (click)="save()"
            >
              <ng-icon xui name="matSaveRound" size="sm" />
            </button>
            <button xuiButton variant="ghost" size="sm" (click)="openSaveAs()">Save as…</button>
            <cth-preset-menu />
            <button
              xuiButton
              variant="ghost"
              size="sm"
              aria-label="Undo"
              [disabled]="!live.state()?.canUndo"
              [xuiTooltip]="'Undo (⌘Z)'"
              (click)="live.undo()"
            >
              <ng-icon xui name="matUndoRound" size="sm" />
            </button>
            <button
              xuiButton
              variant="ghost"
              size="sm"
              aria-label="Redo"
              [disabled]="!live.state()?.canRedo"
              [xuiTooltip]="'Redo (⇧⌘Z, ⌘Y)'"
              (click)="live.redo()"
            >
              <ng-icon xui name="matRedoRound" size="sm" />
            </button>
          </div>
          <div xuiNavbarGroup align="end" class="gap-3">
            <cth-transport />
            <button
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
              xuiButton
              variant="ghost"
              size="sm"
              aria-label="Analyser"
              [xuiTooltip]="'Scope and RTA'"
              (click)="scopeOpen.set(true)"
            >
              <ng-icon xui name="matShowChartRound" size="sm" />
            </button>
            <cth-engine-meters />
            <button
              xuiButton
              variant="ghost"
              size="sm"
              aria-label="Keyboard shortcuts"
              [xuiTooltip]="'Keyboard shortcuts (?)'"
              (click)="shortcuts.helpOpen.set(true)"
            >
              <ng-icon xui name="matKeyboardRound" size="sm" />
            </button>
            <button
              xuiButton
              variant="ghost"
              size="sm"
              aria-label="Preferences"
              data-testid="preferences-button"
              [xuiTooltip]="'Preferences'"
              (click)="ui.preferencesOpen.set(true)"
            >
              <ng-icon xui name="matSettingsRound" size="sm" />
            </button>
          </div>
        </nav>
        <div class="bg-surface border-border flex items-center gap-6 border-b px-4 py-1.5">
          <div class="flex items-center gap-2">
            <span class="text-foreground-muted text-xs font-medium" aria-hidden="true">Compare</span>
            <xui-segmented-control
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
              size="sm"
              aria-label="Scene"
              data-testid="scenes"
              class="grid grid-cols-4"
              [options]="scenes()"
              [value]="live.activeScene()"
              (valueChange)="$event !== null && live.selectScene($event)"
            />
          </div>
        </div>
        <main class="min-h-0 flex-1">
          <router-outlet />
        </main>
        <cth-tuner-dialog [open]="ui.tunerOpen()" (openChange)="ui.tunerOpen.set($event)" />
        <cth-shortcut-help [open]="shortcuts.helpOpen()" (openChange)="shortcuts.helpOpen.set($event)" />
        <cth-preferences-dialog [open]="ui.preferencesOpen()" (openChange)="ui.preferencesOpen.set($event)" />
        <cth-preset-picker />
        <cth-save-block-dialog />
        <cth-batch-set-dialog />
        <cth-scope-dialog [(open)]="scopeOpen" />
        <xui-dialog title="Save preset as" icon="matSaveRound" [(open)]="saveAsOpen">
          <xui-dialog-body>
            <xui-form-field label="Name">
              <input
                xuiInput
                data-testid="save-as-name"
                [value]="saveAsName()"
                (input)="saveAsName.set($any($event.target).value)"
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
  private readonly notify = inject(Notify);
  private readonly presetPicker = viewChild(PresetPicker);

  /**
   * In the macOS desktop app the navbar is the title bar: it drags the window and leaves room for the traffic lights
   * (apps/desktop/src/main.ts), which stand in for the app's name. Full screen hides them, and the room goes with them.
   */
  protected readonly macWindow = window.cathode?.platform === 'darwin';
  private readonly fullScreen = signal(false);
  protected readonly trafficLights = computed(() => this.macWindow && !this.fullScreen());
  protected readonly scopeOpen = signal(false);
  protected readonly saveAsOpen = signal(false);
  protected readonly saveAsName = signal('');
  protected readonly abOptions = [
    { label: 'A', value: 'A' as const },
    { label: 'B', value: 'B' as const }
  ];
  protected readonly scenes = computed(() =>
    (this.live.preset()?.scenes ?? []).map((scene, index) => ({ label: String(index + 1), value: index }))
  );

  constructor() {
    window.cathode?.onFullScreen?.(fullScreen => this.fullScreen.set(fullScreen));
    void this.startup.start();
    inject(DestroyRef).onDestroy(this.registerShortcuts());
  }

  protected async newPreset(): Promise<void> {
    if (await this.live.newPreset()) {
      this.compare.reset();
    }
  }

  protected save(): void {
    void this.live.save();
  }

  protected openSaveAs(): void {
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
   * The shortcuts that work on every page (docs/02-architecture.md §7.5); the grid adds the cell commands. Preset
   * Leveling (⌘L) and Reset channel (⌘I) are waiting for the PS stream's endpoints.
   */
  private registerShortcuts(): () => void {
    const ui = this.ui;
    const block = () => ui.block();
    const handlers: Record<string, () => void> = {
      presetPicker: () => ui.presetPicker.set('browse'),
      goTo: () => ui.presetPicker.set('goto'),
      presetPrevious: () => void this.presetPicker()?.step(-1),
      presetNext: () => void this.presetPicker()?.step(1),
      reload: () => void this.presetPicker()?.reload(),
      save: () => this.save(),
      saveAs: () => this.openSaveAs(),
      // TODO(PS): Preset Leveling: call the PS stream's leveling endpoint once it is on master.
      level: () => this.notify.error('Preset Leveling is not available yet.'),
      // TODO(PS): Reset channel: call the PS stream's channel reset endpoint once it is on master.
      channelReset: () => this.notify.error('Reset channel is not available yet.'),
      channelNext: () => ui.nextChannel(),
      bypass: () => ui.toggleBypass(),
      engageAll: () => ui.allScenes({ bypassed: false }),
      bypassAll: () => ui.allScenes({ bypassed: true }),
      global: () => ui.toggleGlobal(),
      saveBlock: () => block() && ui.saveBlockOpen.set(true),
      blockInfo: () => this.shortcuts.blockInfo.update(on => !on),
      undo: () => void this.live.undo(),
      redo: () => void this.live.redo(),
      redoAlias: () => void this.live.redo(),
      tuner: () => ui.tunerOpen.update(open => !open),
      help: () => this.shortcuts.helpOpen.update(open => !open)
    };
    for (const channel of ['A', 'B', 'C', 'D'] as const) {
      handlers[`channel${channel}`] = () => ui.setChannel(channel);
    }

    handlers['bypassAlias'] = () => ui.toggleBypass();

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
