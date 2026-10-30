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
import type { PresetPosition } from '@cathode/api-client';
import { provideIcons } from '@ng-icons/core';
import { matExpandMoreRound, matKeyboardArrowDownRound, matKeyboardArrowUpRound } from '@ng-icons/material-icons/round';
import { XuiIconImports } from '@xui/icon';
import { XuiMenuImports } from '@xui/menu';
import { XuiTooltipImports } from '@xui/tooltip';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';
import { UiStore } from '../state/ui.store';

/** Where the loaded preset stands in the preset order, for the readout: its number, and what the number counts. */
export interface PresetPlace {
  number: string;
  where: string;
}

/**
 * The bank slot or setlist entry the preset was loaded from (`position`), or else its first slot in bank order; numbered
 * from 1, as a foot controller shows them. A preset in no bank has no number.
 */
export function presetPlace(
  position: PresetPosition | null | undefined,
  id: string | undefined,
  banks: readonly { id: string; name: string; slots: readonly (string | null)[] }[]
): PresetPlace {
  const number = (n: number) => String(n + 1).padStart(3, '0');
  if (position?.setlist != null && position.entry != null) {
    return { number: number(position.entry), where: `Setlist entry ${position.entry + 1}` };
  }

  const at =
    position?.bank != null && position.slot != null
      ? { bank: banks.find(b => b.id === position.bank), slot: position.slot }
      : banks.map(bank => ({ bank, slot: id ? bank.slots.indexOf(id) : -1 })).find(b => b.slot >= 0);
  return at
    ? { number: number(at.slot), where: `${at.bank?.name ?? 'Bank'}, slot ${at.slot + 1}` }
    : { number: '---', where: 'In no bank' };
}

/**
 * The bank the loaded preset stands in, by the same rule as {@link presetPlace}: the bank it was loaded from, or else
 * the first holding it in bank order. Null when it was loaded from a setlist or is in no bank.
 */
export function presetBank(
  position: PresetPosition | null | undefined,
  id: string | undefined,
  banks: readonly { id: string; slots: readonly (string | null)[] }[]
): string | null {
  if (position?.setlist != null && position.entry != null) {
    return null;
  }

  if (position?.bank != null && position.slot != null) {
    return banks.some(b => b.id === position.bank) ? position.bank : null;
  }

  return (id && banks.find(b => b.slots.includes(id))?.id) || null;
}

/**
 * The preset and scene readout, as Axe-Edit has it: two lines, each with up/down steppers, a number and a name. The
 * preset line steps through the presets in bank order, or through the setlist the preset was loaded from (its number
 * is the bank slot or setlist entry), and opens the preset picker on a click; the scene line steps through the eight scenes, wrapping, and renames the scene on a
 * click. Beside the preset, the bank it stands in: choosing another bank loads that bank's first preset, and next and
 * previous go on from there.
 */
@Component({
  selector: 'cth-preset-scene-display',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [XuiIconImports, XuiMenuImports, XuiTooltipImports],
  providers: [provideIcons({ matExpandMoreRound, matKeyboardArrowDownRound, matKeyboardArrowUpRound })],
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
        class="lcd-glass flex min-w-0 flex-1 items-center gap-3 px-2 text-start text-sm hover:brightness-110"
        data-testid="preset-picker-open"
        [xuiTooltip]="'Presets (P) · ' + place().where"
        (click)="ui.presetPicker.set(true)"
      >
        <span class="text-lcd-accent w-8 shrink-0 font-semibold tabular-nums" aria-hidden="true">{{
          place().number
        }}</span>
        <span class="truncate font-medium" data-testid="preset-name">
          {{ live.preset()?.name || '(untitled)' }}
          @if (live.state()?.dirty) {
            <span class="text-warning" aria-label="unsaved changes"> •</span>
          }
        </span>
      </button>
      @if (library.banks().length > 0) {
        <button
          type="button"
          class="lcd-glass flex max-w-32 shrink-0 items-center gap-1 ps-2 pe-1 text-xs hover:brightness-110 max-lg:max-w-24"
          data-testid="bank-menu"
          [attr.aria-label]="'Bank: ' + (bankName() ?? 'none')"
          [xuiTooltip]="'Bank: choose one to start from its first preset'"
          [xuiMenuTriggerFor]="bankMenu"
        >
          <span class="truncate" [class.text-lcd-muted]="!bankName()">{{ bankName() ?? 'No bank' }}</span>
          <ng-icon xui name="matExpandMoreRound" size="xs" class="text-lcd-muted shrink-0" />
        </button>
      }
    </div>
    <ng-template #bankMenu>
      <xui-menu>
        @for (bank of library.banks(); track bank.id) {
          <button
            type="button"
            xuiMenuItem
            [selected]="bank.id === bankId()"
            [disabled]="!hasPresets(bank)"
            [attr.data-testid]="'bank-choice-' + bank.id"
            (click)="chooseBank.emit(bank.id)"
          >
            {{ bank.name }}
          </button>
        }
      </xui-menu>
    </ng-template>
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
          class="lcd-glass border-focus min-w-0 flex-1 px-2 text-sm outline-none"
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
          class="lcd-glass flex min-w-0 flex-1 items-center gap-3 px-2 text-start text-sm hover:brightness-110"
          data-testid="scene-name"
          [xuiTooltip]="'Rename the scene'"
          (click)="renaming.set(true)"
        >
          <span class="text-lcd-accent w-8 shrink-0 font-semibold tabular-nums" aria-hidden="true">{{
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
  protected readonly library = inject(LibraryStore);

  /** Next (1) or previous (-1) preset; the shell steps in bank order as ⌘PgDn/⌘PgUp do. */
  readonly stepPreset = output<1 | -1>();
  /** A bank chosen from the bank menu, by id; the shell loads its first preset. */
  readonly chooseBank = output<string>();

  protected readonly renaming = signal(false);
  private readonly nameInput = viewChild<ElementRef<HTMLInputElement>>('name');
  /** The preset's bank slot or setlist entry, or dashes for a preset in no bank. */
  protected readonly place = computed(() =>
    presetPlace(this.live.state()?.position, this.live.preset()?.id, this.library.banks())
  );
  /** The bank the preset stands in (null: loaded from a setlist, or in no bank), and its name. */
  protected readonly bankId = computed(() =>
    presetBank(this.live.state()?.position, this.live.preset()?.id, this.library.banks())
  );
  protected readonly bankName = computed(() => this.library.banks().find(b => b.id === this.bankId())?.name ?? null);
  protected readonly hasPresets = (bank: { slots: readonly (string | null)[] }) => bank.slots.some(Boolean);
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
