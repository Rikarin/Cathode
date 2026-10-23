import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import type { PresetBlock } from '@cathode/api-client';
import { XuiInputImports } from '@xui/input';
import { CatalogStore } from '../../state/catalog.store';
import { LiveStore } from '../../state/live.store';
import { CustomShifterType, NoteNames, customParamId, noteAbove } from './custom-shifter';

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
                        xuiInput
                        type="number"
                        min="-24"
                        max="24"
                        step="1"
                        class="w-14 text-center"
                        [attr.data-testid]="'param-' + cell.id"
                        [attr.aria-label]="'Voice ' + row.voice + ', ' + cell.note"
                        [value]="cell.value"
                        (change)="set(cell.id, $any($event.target).valueAsNumber)"
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

  /** A parameter's value on the active channel, or its default. */
  private value(id: string): number {
    const block = this.block();
    const stored = this.live.valueOf(block, this.live.channelOf(block.id), id);
    return stored ?? this.catalog.info(block.type)?.params.find(p => p.id === id)?.default ?? 0;
  }
}
