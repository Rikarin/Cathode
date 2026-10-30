import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { BuiltInTypes, type ChannelName, ChannelNames, type Preset, type SceneBlockState } from '@cathode/api-client';
import { blockColor } from '@cathode/audio-ui';
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
  /** The block type's name, and its category's colour (as on its tile in the grid). */
  title: string;
  color: string;
  channels: ChannelName[];
  /** The Input and Output have channels but no bypass. */
  bypassable: boolean;
  ignores: ChannelName[];
  states: SceneBlockState[];
}

/** The blocks × scenes table: every block but shunts, in grid order (row, then column). */
export function sceneRows(
  preset: Preset,
  info: (type: string) => { name: string; channels: number; category?: string } | undefined
): SceneRow[] {
  return preset.blocks
    .flatMap(b => {
      const type = b.type === BuiltInTypes.shunt ? undefined : info(b.type);
      return type ? [{ b, type }] : [];
    })
    .sort((x, y) => x.b.row - y.b.row || x.b.column - y.b.column)
    .map(({ b, type }) => ({
      id: b.id,
      name: `${type.name} (${b.id})`,
      title: type.name,
      color: blockColor(type.category ?? ''),
      channels: ChannelNames.slice(0, Math.max(1, type.channels)),
      bypassable: b.type !== BuiltInTypes.input && b.type !== BuiltInTypes.output,
      ignores: b.sceneIgnore as ChannelName[],
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
        type="button"
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
        type="button"
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
      <!-- The table scrolls in its pane, both ways, so its sticky header stays at the pane's top; the page doesn't. -->
      <div class="pane min-h-0 overflow-auto!">
        <table class="w-full border-separate border-spacing-0 text-sm" data-testid="scene-matrix">
          <thead>
            <tr>
              <th class="pane-header text-foreground-muted sticky top-0 z-10 px-3 py-2 text-left">Block</th>
              @for (scene of scenes(); track $index) {
                <th
                  class="pane-header sticky top-0 z-10 px-3 py-2 text-left whitespace-nowrap"
                  [class.text-primary]="$index === live.activeScene()"
                  [class.text-foreground-muted]="$index !== live.activeScene()"
                >
                  <span class="readout me-1 opacity-70">{{ $index + 1 }}</span
                  >{{ scene.name }}
                </th>
              }
            </tr>
          </thead>
          <tbody>
            @for (row of rows(); track row.id) {
              <tr class="hover:bg-hover-overlay">
                <th class="border-hairline border-t px-3 py-1.5 text-left font-normal whitespace-nowrap">
                  <span class="flex items-center gap-2">
                    <span
                      class="h-4 w-1 shrink-0 rounded-full"
                      [style.background]="row.color"
                      aria-hidden="true"
                    ></span>
                    <span class="font-medium">{{ row.title }}</span>
                    <span class="text-foreground-subtle text-xs">{{ row.id }}</span>
                  </span>
                </th>
                @for (state of row.states; track $index; let scene = $index) {
                  <td class="border-hairline border-t px-3 py-1.5" [class.bg-primary/8]="scene === live.activeScene()">
                    <div class="flex items-center gap-2">
                      @if (row.bypassable) {
                        <button
                          type="button"
                          class="focus-visible:outline-focus size-2.5 shrink-0 rounded-full outline-offset-2 transition-[background,box-shadow] focus-visible:outline-2"
                          [class]="
                            state.bypassed
                              ? 'bg-black/50 shadow-[inset_0_1px_1px_rgb(0_0_0/0.6),0_0.5px_0_var(--highlight)]'
                              : 'bg-success shadow-[0_0_6px_var(--color-success)]'
                          "
                          [attr.aria-label]="
                            row.name + ' in scene ' + (scene + 1) + ': ' + (state.bypassed ? 'bypassed' : 'engaged')
                          "
                          [attr.aria-pressed]="!state.bypassed"
                          (click)="
                            live.setSceneBlock(scene, row.id, { bypassed: !state.bypassed, channel: state.channel })
                          "
                        ></button>
                      } @else {
                        <!-- Input and Output can't be bypassed: keep the LED's place so the channels line up. -->
                        <span class="size-2.5 shrink-0" aria-hidden="true"></span>
                      }
                      @if (row.channels.length > 1) {
                        <span
                          class="bg-surface-inset border-hairline flex rounded-md border p-px shadow-[inset_0_1px_1px_rgb(0_0_0/0.3)]"
                        >
                          @for (channel of row.channels; track channel) {
                            <button
                              type="button"
                              class="focus-visible:outline-focus rounded-[5px] px-1.5 text-[11px] leading-5 font-semibold focus-visible:outline-2"
                              [class]="
                                state.channel === channel
                                  ? 'bg-primary text-primary-foreground shadow-[inset_0_1px_0_rgb(255_255_255/0.2)]'
                                  : 'text-foreground-subtle hover:text-foreground'
                              "
                              [attr.aria-label]="row.name + ' in scene ' + (scene + 1) + ': channel ' + channel"
                              [attr.aria-pressed]="state.channel === channel"
                              [attr.title]="
                                row.ignores.includes(channel) ? 'Scene Ignore is on for this channel' : null
                              "
                              (click)="live.setSceneBlock(scene, row.id, { bypassed: state.bypassed, channel })"
                            >
                              {{ channel }}{{ row.ignores.includes(channel) ? '*' : '' }}
                            </button>
                          }
                        </span>
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
