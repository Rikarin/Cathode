import {
  ChangeDetectionStrategy,
  Component,
  type ElementRef,
  computed,
  effect,
  inject,
  output,
  signal,
  viewChild
} from '@angular/core';
import { provideIcons } from '@ng-icons/core';
import { matKeyboardArrowDownRound, matKeyboardArrowUpRound } from '@ng-icons/material-icons/round';
import { XuiIconImports } from '@xui/icon';
import { XuiTooltipImports } from '@xui/tooltip';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';
import { UiStore } from '../state/ui.store';

/**
 * The preset and scene readout, as Axe-Edit has it: two lines, each with up/down steppers, a number and a name. The
 * preset line steps through the library in its next/previous order (its number is the place in that order) and opens
 * the preset picker on a click; the scene line steps through the eight scenes, wrapping, and renames the scene on a
 * click.
 */
@Component({
  selector: 'cth-preset-scene-display',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [XuiIconImports, XuiTooltipImports],
  providers: [provideIcons({ matKeyboardArrowDownRound, matKeyboardArrowUpRound })],
  host: { class: 'flex min-w-0 flex-col gap-1' },
  template: `
    <div class="flex h-6 min-w-0 items-stretch gap-1" aria-label="Preset">
      <div class="flex flex-col">
        <button
          type="button"
          class="cth-stepper rounded-t"
          aria-label="Next preset"
          data-testid="preset-next"
          [xuiTooltip]="'Next preset (⌘PgDn)'"
          (click)="stepPreset.emit(1)"
        >
          <ng-icon xui name="matKeyboardArrowUpRound" size="xs" />
        </button>
        <button
          type="button"
          class="cth-stepper rounded-b"
          aria-label="Previous preset"
          data-testid="preset-previous"
          [xuiTooltip]="'Previous preset (⌘PgUp)'"
          (click)="stepPreset.emit(-1)"
        >
          <ng-icon xui name="matKeyboardArrowDownRound" size="xs" />
        </button>
      </div>
      <button
        type="button"
        class="bg-surface-inset border-border hover:border-border-strong flex min-w-0 flex-1 items-center gap-3 rounded border px-2 text-start text-sm"
        data-testid="preset-picker-open"
        [xuiTooltip]="'Presets (P)'"
        (click)="ui.presetPicker.set(true)"
      >
        <span class="text-primary w-8 shrink-0 font-semibold tabular-nums" aria-hidden="true">{{
          presetNumber()
        }}</span>
        <span class="truncate font-medium" data-testid="preset-name">
          {{ live.preset()?.name || '(untitled)' }}
          @if (live.state()?.dirty) {
            <span class="text-warning" aria-label="unsaved changes"> •</span>
          }
        </span>
      </button>
    </div>
    <div class="flex h-6 min-w-0 items-stretch gap-1" aria-label="Scene name">
      <div class="flex flex-col">
        <button type="button" class="cth-stepper rounded-t" aria-label="Next scene" (click)="stepScene(1)">
          <ng-icon xui name="matKeyboardArrowUpRound" size="xs" />
        </button>
        <button type="button" class="cth-stepper rounded-b" aria-label="Previous scene" (click)="stepScene(-1)">
          <ng-icon xui name="matKeyboardArrowDownRound" size="xs" />
        </button>
      </div>
      @if (renaming()) {
        <input
          #sceneNameField
          class="bg-surface-inset border-focus min-w-0 flex-1 rounded border px-2 text-sm outline-none"
          aria-label="Scene name"
          data-testid="scene-name-input"
          [value]="sceneName()"
          (keydown.enter)="rename(sceneNameField.value)"
          (keydown.escape)="renaming.set(false)"
          (blur)="rename(sceneNameField.value)"
          #name
        />
      } @else {
        <button
          type="button"
          class="bg-surface-inset border-border hover:border-border-strong flex min-w-0 flex-1 items-center gap-3 rounded border px-2 text-start text-sm"
          data-testid="scene-name"
          [xuiTooltip]="'Rename the scene'"
          (click)="renaming.set(true)"
        >
          <span class="text-primary w-8 shrink-0 font-semibold tabular-nums" aria-hidden="true">{{
            sceneNumber()
          }}</span>
          <span class="truncate">{{ sceneName() }}</span>
        </button>
      }
    </div>
  `
})
export class PresetSceneDisplay {
  protected readonly live = inject(LiveStore);
  protected readonly ui = inject(UiStore);
  private readonly library = inject(LibraryStore);

  /** Next (1) or previous (-1) preset; the shell steps through the library as ⌘PgDn/⌘PgUp do. */
  readonly stepPreset = output<1 | -1>();

  protected readonly renaming = signal(false);
  private readonly nameInput = viewChild<ElementRef<HTMLInputElement>>('name');
  /** The preset's place in the library's next/previous order, or dashes for a preset not saved yet. */
  protected readonly presetNumber = computed(() => {
    const index = this.library.ordered().findIndex(p => p.id === this.live.preset()?.id);
    return index < 0 ? '---' : String(index + 1).padStart(3, '0');
  });
  protected readonly sceneNumber = computed(() => `S${String(this.live.activeScene() + 1).padStart(2, '0')}`);
  protected readonly sceneName = computed(() => this.live.preset()?.scenes[this.live.activeScene()]?.name ?? '');

  constructor() {
    // The rename field takes the keyboard as it opens, with the old name selected.
    effect(() => {
      const input = this.nameInput()?.nativeElement;
      input?.focus();
      input?.select();
    });
  }

  protected stepScene(direction: 1 | -1): void {
    const count = this.live.preset()?.scenes.length ?? 8;
    void this.live.selectScene((this.live.activeScene() + direction + count) % count);
  }

  protected rename(name: string): void {
    if (!this.renaming()) {
      return;
    }

    this.renaming.set(false);
    const trimmed = name.trim();
    if (trimmed && trimmed !== this.sceneName()) {
      void this.live.renameScene(this.live.activeScene(), trimmed);
    }
  }
}
