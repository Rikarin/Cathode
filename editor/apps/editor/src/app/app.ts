import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { provideIcons } from '@ng-icons/core';
import {
  matAddRound,
  matRedoRound,
  matSaveRound,
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
import { Startup } from './core/startup';
import { EngineMeters } from './shell/engine-meters';
import { ScopeDialog } from './shell/scope-dialog';
import { Transport } from './shell/transport';
import { TunerDialog } from './shell/tuner-dialog';
import { CompareStore } from './state/compare.store';
import { LiveStore } from './state/live.store';

@Component({
  selector: 'cth-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    EngineMeters,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    ScopeDialog,
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
  providers: [provideIcons({ matAddRound, matRedoRound, matSaveRound, matShowChartRound, matTuneRound, matUndoRound })],
  host: {
    class: 'flex h-full flex-col',
    '(document:keydown)': 'shortcut($event)'
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
        <nav xuiNavbar class="gap-3">
          <div xuiNavbarGroup class="gap-1">
            <span xuiNavbarHeading>Cathode</span>
            <div xuiNavbarDivider></div>
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
            <a xuiButton variant="ghost" size="sm" routerLink="/irs" routerLinkActive="bg-active-overlay">IRs</a>
            <a xuiButton variant="ghost" size="sm" routerLink="/global" routerLinkActive="bg-active-overlay">Global</a>
            <a xuiButton variant="ghost" size="sm" routerLink="/setup" routerLinkActive="bg-active-overlay">Setup</a>
            <div xuiNavbarDivider></div>
            <span class="max-w-56 truncate text-sm font-medium" data-testid="preset-name">
              {{ live.preset()?.name }}
              @if (live.state()?.dirty) {
                <span class="text-warning" aria-label="unsaved changes"> •</span>
              }
            </span>
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
              [xuiTooltip]="'Redo (⇧⌘Z)'"
              (click)="live.redo()"
            >
              <ng-icon xui name="matRedoRound" size="sm" />
            </button>
          </div>
          <div xuiNavbarGroup align="end" class="gap-3">
            <cth-transport />
            <xui-segmented-control
              size="sm"
              aria-label="A/B compare"
              [options]="abOptions"
              [value]="compare.active()"
              (valueChange)="$event && compare.switchTo($event)"
            />
            <xui-segmented-control
              size="sm"
              aria-label="Scene"
              data-testid="scenes"
              [options]="scenes()"
              [value]="live.activeScene()"
              (valueChange)="$event !== null && live.selectScene($event)"
            />
            <button
              xuiButton
              variant="ghost"
              size="sm"
              aria-label="Tuner"
              [xuiTooltip]="'Tuner'"
              (click)="tunerOpen.set(true)"
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
          </div>
        </nav>
        <main class="min-h-0 flex-1">
          <router-outlet />
        </main>
        <cth-tuner-dialog [(open)]="tunerOpen" />
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

  protected readonly tunerOpen = signal(false);
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
    void this.startup.start();
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

  protected shortcut(event: KeyboardEvent): void {
    const target = event.target as HTMLElement | null;
    if (!(event.metaKey || event.ctrlKey) || target?.closest('input, textarea, [contenteditable]')) {
      return;
    }

    const key = event.key.toLowerCase();
    if (key === 's') {
      event.preventDefault();
      if (event.shiftKey) {
        this.openSaveAs();
      } else {
        this.save();
      }
    } else if (key === 'z') {
      event.preventDefault();
      void (event.shiftKey ? this.live.redo() : this.live.undo());
    } else if (key === 'a') {
      // An editor, not a document: select-all would only paint every label blue.
      event.preventDefault();
    }
  }
}
