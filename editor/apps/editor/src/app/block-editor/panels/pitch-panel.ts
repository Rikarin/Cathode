import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import type { PresetBlock } from '@cathode/api-client';
import { XuiInputImports } from '@xui/input';
import { CatalogStore } from '../../state/catalog.store';
import { LiveStore } from '../../state/live.store';
import { blockDefault } from '../type-labels';
import {
  CustomScale,
  CustomShifterType,
  DiatonicType,
  NoteNames,
  customParamId,
  customScaleNotes,
  noteAbove
} from './custom-shifter';

const Voices = [1, 2, 3, 4] as const;

/**
 * The Pitch block's Custom Shifter tables (the Axe-Fx custom scales): for each voice, the shift in semitones for
 * each of the twelve notes of the key. Columns run from the key's root, named in the Key parameter's key; under each
 * value is the note that voice then sounds. The tables are relative to the key, so they also follow a MIDI or Auto
 * key source.
 */
@Component({
  selector: 'cth-pitch-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [XuiInputImports],
  template: `
    @if (customScale(); as notes) {
      <p class="text-foreground-muted text-xs" data-testid="custom-scale">
        Custom scale in {{ tonic() }}: <span class="text-foreground">{{ notes.join(' ') }}</span>
      </p>
    }
    @if (custom()) {
      <section class="flex flex-col gap-2" aria-label="Custom shifter">
        <span class="text-foreground-muted text-xs">Custom shifter: semitones for each note of the key</span>
        <div class="overflow-x-auto">
          <table class="border-separate border-spacing-1 text-xs">
            <thead>
              <tr>
                <th></th>
                @for (column of columns(); track column.degree) {
                  <th scope="col" class="font-medium">{{ column.name }}</th>
                }
              </tr>
            </thead>
            <tbody>
              @for (row of rows(); track row.voice) {
                <tr>
                  <th scope="row" class="text-foreground-muted pr-1 text-left font-normal">Voice {{ row.voice }}</th>
                  @for (cell of row.cells; track cell.id) {
                    <td>
                      <input
                        #semitonesField
                        xuiInput
                        type="number"
                        min="-24"
                        max="24"
                        step="1"
                        class="w-12 [appearance:textfield] px-1 text-center [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none"
                        [attr.data-testid]="'param-' + cell.id"
                        [attr.aria-label]="'Voice ' + row.voice + ', ' + cell.note"
                        [value]="cell.value"
                        (change)="set(cell.id, semitonesField.valueAsNumber)"
                      />
                      <div class="text-foreground-muted text-center">{{ cell.sounds }}</div>
                    </td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>
    }
  `
})
export class PitchPanel {
  private readonly live = inject(LiveStore);
  private readonly catalog = inject(CatalogStore);

  readonly block = input.required<PresetBlock>();

  protected readonly custom = computed(() => this.value('type') === CustomShifterType);
  private readonly key = computed(() => this.value('key'));
  protected readonly tonic = computed(() => NoteNames[Math.round(this.key())] ?? 'C');

  /** The Diatonic type's custom scale, named in its Key (the scale's root), when Scale is Custom. */
  protected readonly customScale = computed(() =>
    this.value('type') === DiatonicType && Math.round(this.value('scale')) === CustomScale
      ? customScaleNotes(
          Math.round(this.key()),
          4 + Math.round(this.value('customNotes')),
          Array.from({ length: 8 }, (_, n) => this.value(`customNote${n + 1}`))
        )
      : null
  );

  protected readonly columns = computed(() =>
    NoteNames.map((_, degree) => ({ degree, name: noteAbove(this.key(), degree) }))
  );

  protected readonly rows = computed(() =>
    Voices.map(voice => ({
      voice,
      cells: NoteNames.map((_, degree) => {
        const id = customParamId(voice, degree);
        const value = this.value(id);
        const note = noteAbove(this.key(), degree);
        return { id, note, value, sounds: noteAbove(this.key(), degree + value) };
      })
    }))
  );

  protected set(id: string, value: number): void {
    if (Number.isFinite(value)) {
      const block = this.block();
      const clamped = Math.max(-24, Math.min(24, Math.round(value)));
      this.live.setParam(block.id, this.live.channelOf(block.id), id, clamped);
    }
  }

  /** A parameter's value on the active channel, or its default on the block's type. */
  private value(id: string): number {
    const block = this.block();
    const valueOf = (param: string) => this.live.valueOf(block, this.live.channelOf(block.id), param);
    const params = this.catalog.info(block.type)?.params ?? [];
    const param = params.find(p => p.id === id);
    return valueOf(id) ?? (param ? blockDefault(params, param, valueOf) : 0);
  }
}
