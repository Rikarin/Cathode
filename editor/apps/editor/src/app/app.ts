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
  matUndoRound
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
import { XuiTooltipImports } from '@xui/tooltip';
import { Startup } from './core/startup';
import { BatchSetDialog } from './shell/batch-set-dialog';
import { EngineMeters } from './shell/engine-meters';
import { LevelingDialog } from './shell/leveling-dialog';
import { PresetMenu } from './shell/preset-menu';
import { PresetPicker } from './shell/preset-picker';
import { PresetSceneDisplay } from './shell/preset-scene-display';
import { SaveBlockDialog } from './shell/save-block-dialog';
import { ScopeDialog } from './shell/scope-dialog';
import { SettingsDialog } from './shell/settings-dialog';
import { ShortcutService } from './shell/shortcuts';
import { Transport } from './shell/transport';
import { TunerDialog } from './shell/tuner-dialog';
import { CompareStore } from './state/compare.store';
import { LiveStore } from './state/live.store';
import { PresetSwitcher } from './state/preset-switcher';
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
      matUndoRound
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
      @case ('token') {
        <xui-non-ideal-state
          class="h-full"
          title="Access token needed"
          description="This Cathode server asks for its access token. It is Cathode__Token in /etc/cathode/cathode.env on the rig, or ask whoever set it up."
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
        <nav xuiNavbar class="gap-3" [class.window-drag]="macWindow" [class.ps-21]="trafficLights()">
          <div xuiNavbarGroup class="no-scrollbar min-w-0 gap-1 overflow-x-auto">
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
            <a xuiButton variant="ghost" size="sm" routerLink="/scenes" routerLinkActive="bg-active-overlay">Scenes</a>
            <a xuiButton variant="ghost" size="sm" routerLink="/controllers" routerLinkActive="bg-active-overlay"
              >Controllers</a
            >
            <a xuiButton variant="ghost" size="sm" routerLink="/perform" routerLinkActive="bg-active-overlay"
              >Perform</a
            >
            <a xuiButton variant="ghost" size="sm" routerLink="/global" routerLinkActive="bg-active-overlay">Global</a>
          </div>
          <div xuiNavbarGroup align="end" class="shrink-0 gap-2">
            <div class="flex items-center">
              <button
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
            <button
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
            <cth-engine-meters />
            @if (settingsButton) {
              <button
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
        <div class="bg-surface border-border flex items-center gap-6 border-b px-4 py-1.5">
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
          <cth-preset-scene-display class="max-w-xl min-w-0 flex-1" (stepPreset)="presetPicker()?.step($event)" />
          <div class="ms-auto flex shrink-0 items-center gap-1" aria-label="Preset actions">
            <button
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
                  <button xuiMenuItem (click)="switcher.load(recent.id, recent.name)">{{ recent.name }}</button>
                } @empty {
                  <button xuiMenuItem disabled>No recent presets</button>
                }
              </xui-menu>
            </ng-template>
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
  protected readonly abOptions = [
    { label: 'A', value: 'A' as const },
    { label: 'B', value: 'B' as const }
  ];
  protected readonly scenes = computed(() =>
    (this.live.preset()?.scenes ?? []).map((scene, index) => ({ label: String(index + 1), value: index }))
  );

  constructor() {
    window.cathode?.onFullScreen?.(fullScreen => this.fullScreen.set(fullScreen));
    window.cathode?.onOpenSettings?.(() => this.ui.settings.set('preferences'));
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
      reload: () => void this.presetPicker()?.reload(),
      save: () => this.save(),
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
      global: () => ui.toggleGlobal(),
      saveBlock: () => block() && ui.saveBlockOpen.set(true),
      blockInfo: () => this.shortcuts.blockInfo.update(on => !on),
      undo: () => void this.live.undo(),
      redo: () => void this.live.redo(),
      redoAlias: () => void this.live.redo(),
      tuner: () => ui.tunerOpen.update(open => !open),
      settings: () => ui.settings.update(tab => (tab === null ? 'preferences' : null)),
      help: () => ui.settings.update(tab => (tab === 'shortcuts' ? null : 'shortcuts'))
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
