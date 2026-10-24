import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { BuiltInTypes, type ChannelName, ChannelNames, type Preset, type SceneBlockState } from '@cathode/api-client';
import { XuiButtonImports } from '@xui/button';
import { XuiNonIdealStateImports } from '@xui/non-ideal-state';
import { XuiSelectImports } from '@xui/select';
import { XuiTextImports } from '@xui/text';
import { CatalogStore } from '../state/catalog.store';
import { LiveStore } from '../state/live.store';

/** One row of the Scene Manager: a block and its state in each of the eight scenes. */
export interface SceneRow {
  id: string;
  name: string;
  channels: ChannelName[];
  /** The Input and Output have channels but no bypass. */
  bypassable: boolean;
  ignores: ChannelName[];
  states: SceneBlockState[];
}

/** The blocks × scenes table: every block but shunts, in grid order (row, then column). */
export function sceneRows(
  preset: Preset,
  info: (type: string) => { name: string; channels: number } | undefined
): SceneRow[] {
  return preset.blocks
    .filter(b => b.type !== BuiltInTypes.shunt && info(b.type))
    .sort((a, b) => a.row - b.row || a.column - b.column)
    .map(b => ({
      id: b.id,
      name: `${info(b.type)!.name} (${b.id})`,
      channels: ChannelNames.slice(0, Math.max(1, info(b.type)!.channels)) as ChannelName[],
      bypassable: b.type !== BuiltInTypes.input && b.type !== BuiltInTypes.output,
      ignores: (b.sceneIgnore ?? []) as ChannelName[],
      states: preset.scenes.map(s => s.blocks[b.id] ?? { bypassed: false, channel: 'A' })
    }));
}

/**
 * The Scene Manager: what every block does in every scene, editable in place, plus copying a scene onto another and
 * swapping two. The active scene's column is highlighted; its edits take effect at once.
 */
@Component({
  selector: 'cth-scene-manager',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [XuiButtonImports, XuiNonIdealStateImports, XuiSelectImports, XuiTextImports],
  host: { class: 'flex h-full flex-col gap-4 overflow-auto p-6' },
  template: `
    <header class="flex flex-wrap items-center gap-3">
      <h1 xuiHeading class="mr-auto text-lg">Scenes</h1>
      <span class="text-foreground-muted text-xs">Scene</span>
      <xui-select
        class="shrink-0"
        aria-label="First scene"
        [filterable]="false"
        [items]="sceneIndexes"
        [itemText]="sceneName"
        [value]="String(from())"
        (valueChange)="$event !== null && from.set(+$event)"
      />
      <span class="text-foreground-muted text-xs">and</span>
      <xui-select
        class="shrink-0"
        aria-label="Second scene"
        [filterable]="false"
        [items]="sceneIndexes"
        [itemText]="sceneName"
        [value]="String(to())"
        (valueChange)="$event !== null && to.set(+$event)"
      />
      <button
        xuiButton
        variant="outline"
        size="sm"
        data-testid="copy-scene"
        [disabled]="from() === to()"
        (click)="live.copyScene(from(), to())"
      >
        Copy first onto second
      </button>
      <button
        xuiButton
        variant="outline"
        size="sm"
        data-testid="swap-scenes"
        [disabled]="from() === to()"
        (click)="live.swapScenes(from(), to())"
      >
        Swap
      </button>
    </header>

    @if (rows().length === 0) {
      <xui-non-ideal-state class="flex-1" title="No blocks" description="Add blocks to the grid first." />
    } @else {
      <div class="overflow-x-auto">
        <table class="w-full border-separate border-spacing-0 text-sm" data-testid="scene-matrix">
          <thead>
            <tr>
              <th class="text-foreground-muted px-2 py-1 text-left text-xs font-semibold">Block</th>
              @for (scene of scenes(); track $index) {
                <th
                  class="px-2 py-1 text-left text-xs font-semibold"
                  [class.bg-active-overlay]="$index === live.activeScene()"
                >
                  {{ $index + 1 }}. {{ scene.name }}
                </th>
              }
            </tr>
          </thead>
          <tbody>
            @for (row of rows(); track row.id) {
              <tr>
                <th class="border-border border-t px-2 py-1 text-left font-medium whitespace-nowrap">
                  {{ row.name }}
                </th>
                @for (state of row.states; track $index; let scene = $index) {
                  <td class="border-border border-t px-2 py-1" [class.bg-active-overlay]="scene === live.activeScene()">
                    <div class="flex items-center gap-1">
                      @if (row.bypassable) {
                        <button
                          type="button"
                          class="size-3 shrink-0 rounded-full"
                          [class]="state.bypassed ? 'bg-surface-sunken ring-border ring-1' : 'bg-success'"
                          [attr.aria-label]="
                            row.name + ' in scene ' + (scene + 1) + ': ' + (state.bypassed ? 'bypassed' : 'engaged')
                          "
                          [attr.aria-pressed]="!state.bypassed"
                          (click)="
                            live.setSceneBlock(scene, row.id, { bypassed: !state.bypassed, channel: state.channel })
                          "
                        ></button>
                      } @else {
                        <!-- Input and Output can't be bypassed: keep the dot's place so the channels line up. -->
                        <span class="size-3 shrink-0" aria-hidden="true"></span>
                      }
                      @if (row.channels.length > 1) {
                        @for (channel of row.channels; track channel) {
                          <button
                            type="button"
                            class="rounded px-1 text-[11px] font-semibold"
                            [class]="
                              state.channel === channel ? 'bg-primary text-primary-foreground' : 'text-foreground-muted'
                            "
                            [attr.aria-label]="row.name + ' in scene ' + (scene + 1) + ': channel ' + channel"
                            [attr.aria-pressed]="state.channel === channel"
                            [attr.title]="row.ignores.includes(channel) ? 'Scene Ignore is on for this channel' : null"
                            (click)="live.setSceneBlock(scene, row.id, { bypassed: state.bypassed, channel })"
                          >
                            {{ channel }}{{ row.ignores.includes(channel) ? '*' : '' }}
                          </button>
                        }
                      }
                    </div>
                  </td>
                }
              </tr>
            }
          </tbody>
        </table>
      </div>
      <p xuiText class="text-foreground-muted text-xs">
        * Scene Ignore: while a block runs that channel, scene changes leave its bypass and channel alone.
      </p>
    }
  `
})
export class SceneManagerPage {
  protected readonly live = inject(LiveStore);
  private readonly catalog = inject(CatalogStore);

  protected readonly from = signal(0);
  protected readonly to = signal(1);
  protected readonly sceneIndexes = ['0', '1', '2', '3', '4', '5', '6', '7'];
  protected readonly String = String;
  protected readonly scenes = computed(() => this.live.preset()?.scenes ?? []);
  protected readonly sceneName = (index: string) => `${+index + 1}. ${this.scenes()[+index]?.name ?? ''}`;
  protected readonly rows = computed(() => {
    const preset = this.live.preset();
    return preset ? sceneRows(preset, type => this.catalog.info(type)) : [];
  });
}
