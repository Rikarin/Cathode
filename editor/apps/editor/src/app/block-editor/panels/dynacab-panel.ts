import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import type { ParameterInfo, PresetBlock } from '@cathode/api-client';
import { type ConeMove, CthSpeakerCone, MicColors } from '@cathode/audio-ui';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { CatalogStore } from '../../state/catalog.store';
import { LibraryStore } from '../../state/library.store';
import { LiveStore } from '../../state/live.store';
import { ParamControl } from '../param-control';
import { AssetPicker } from './asset-picker';
import { DynaCabSlots, type SlotKey, coneMics, gridSlot, slotParamId } from './dynacab';

const Choices: readonly SlotKey[] = ['cab', 'speaker', 'mic'];
const Placement: readonly SlotKey[] = ['position', 'distance', 'angle'];
const Mixer: readonly SlotKey[] = ['level', 'pan', 'delay', 'mute', 'solo', 'invert'];

/**
 * DynaCab's mics: the speaker cone with each mic's placement, dragged across the cone (cap to edge) and away from the
 * grille, and the selected mic's cab, speaker, mic, placement and mixer controls. A mic slot can instead play the
 * user's own multi-position IR pack, interpolated at the mic's placement. The block's global controls (cuts,
 * proximity, room, auto delay, level) stay on the rack below.
 */
@Component({
  selector: 'cth-dynacab-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AssetPicker, CthSpeakerCone, ParamControl, XuiSegmentedControlImports],
  template: `
    <div class="flex flex-wrap items-start gap-6">
      <div
        class="bg-surface-sunken border-border w-[480px] max-w-full shrink-0 rounded-xl border p-3 shadow-[inset_0_1px_3px_rgb(0_0_0/0.35)]"
      >
        <cth-speaker-cone
          label="Mic placement"
          [mics]="mics()"
          [selected]="selected()"
          (moved)="move($event)"
          (selectedChange)="selected.set($event)"
        />
      </div>

      <div class="flex min-w-80 flex-1 flex-col gap-4">
        <div class="flex items-center gap-3">
          <xui-segmented-control
            size="sm"
            aria-label="Mic slot"
            [options]="slotOptions()"
            [value]="selected()"
            (valueChange)="$event !== null && selected.set($event)"
          />
          <span class="size-3 rounded-full" [style.background]="color()" aria-hidden="true"></span>
        </div>

        @if (pack(); as name) {
          <p class="text-foreground-muted text-xs" data-testid="dynacab-pack">
            Playing the IR pack “{{ name }}”: cab, speaker and mic come from the captures.
          </p>
        } @else {
          <div class="flex flex-wrap gap-x-3 gap-y-5">
            @for (param of slotParams(choices); track param.id) {
              <cth-param-control [block]="block()" [param]="param" [accent]="color()" />
            }
          </div>
        }

        <div class="flex flex-wrap gap-x-3 gap-y-5">
          @for (param of slotParams(placement); track param.id) {
            <cth-param-control [block]="block()" [param]="param" [accent]="color()" />
          }
          @for (param of slotParams(mixer); track param.id) {
            <cth-param-control [block]="block()" [param]="param" [accent]="color()" />
          }
        </div>

        <cth-asset-picker
          [label]="'Mic ' + (selected() + 1) + ' IR pack (optional)'"
          accept=".cabgrid,.wav"
          [assets]="library.cabGrids()"
          [reference]="block().assets[grid()]"
          (picked)="live.setAsset(block().id, grid(), $event.ref)"
        />
      </div>
    </div>
  `
})
export class DynaCabPanel {
  protected readonly live = inject(LiveStore);
  protected readonly library = inject(LibraryStore);
  private readonly catalog = inject(CatalogStore);

  readonly block = input.required<PresetBlock>();

  protected readonly choices = Choices;
  protected readonly placement = Placement;
  protected readonly mixer = Mixer;
  protected readonly selected = signal(0);
  protected readonly grid = computed(() => gridSlot(this.selected()));
  protected readonly color = computed(() => MicColors[this.selected() % MicColors.length]);
  protected readonly slotOptions = computed(() =>
    Array.from({ length: DynaCabSlots }, (_, slot) => ({ label: `Mic ${slot + 1}`, value: slot }))
  );

  private readonly params = computed(
    () => new Map((this.catalog.info(this.block().type)?.params ?? []).map(p => [p.id, p] as const))
  );

  /** The current value of a parameter on the block's channel, or its default. */
  private readonly value = computed(() => {
    const block = this.block();
    const channel = this.live.channelOf(block.id);
    const params = this.params();
    return (id: string) => this.live.valueOf(block, channel, id) ?? params.get(id)?.default ?? 0;
  });

  protected readonly pack = computed(() => this.packName(this.selected()));

  protected readonly mics = computed(() =>
    coneMics(this.value(), this.params().get(slotParamId(0, 'mic'))?.options ?? [], slot => this.packName(slot))
  );

  protected slotParams(keys: readonly SlotKey[]): ParameterInfo[] {
    const params = this.params();
    return keys.flatMap(key => params.get(slotParamId(this.selected(), key)) ?? []);
  }

  protected move(move: ConeMove): void {
    const block = this.block();
    const channel = this.live.channelOf(block.id);
    this.selected.set(move.slot);
    this.live.setParam(block.id, channel, slotParamId(move.slot, 'position'), move.position);
    this.live.setParam(block.id, channel, slotParamId(move.slot, 'distance'), move.distance);
  }

  private packName(slot: number): string | undefined {
    const reference = this.block().assets[gridSlot(slot)];
    if (!reference) {
      return undefined;
    }

    const name = this.library.assets().find(a => a.ref === reference)?.name ?? 'IR pack';
    return name.replace(/\.(cabgrid|wav)$/i, '');
  }
}
