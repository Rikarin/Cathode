import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild
} from '@angular/core';
import {
  type BlockTypeInfo,
  BuiltInTypes,
  type ChannelName,
  ChannelNames,
  type PresetBlock,
  type PresetCable
} from '@cathode/api-client';
import { CthBlockTile, CthMeter } from '@cathode/audio-ui';
import { provideIcons } from '@ng-icons/core';
import {
  matAddRound,
  matCableRound,
  matContentCopyRound,
  matContentCutRound,
  matContentPasteRound,
  matDeleteRound,
  matFindReplaceRound,
  matLinkOffRound,
  matLinkRound,
  matPowerSettingsNewRound,
  matPublicRound,
  matSaveRound,
  matSwapVertRound
} from '@ng-icons/material-icons/round';
import { XuiContextMenuImports } from '@xui/context-menu';
import { XuiMenuImports } from '@xui/menu';
import { EventStream } from '../core/events';
import { ShortcutService } from '../shell/shortcuts';
import { BlockFileExtension, BlockPresetsStore } from '../state/block-presets.store';
import { CatalogStore } from '../state/catalog.store';
import { GlobalStore } from '../state/global.store';
import { LiveStore } from '../state/live.store';
import { UiStore } from '../state/ui.store';
import { abbreviate } from './block-info';
import { BlockPicker } from './block-picker';
import { Cell, type Point, cablePath, canConnect, cellOrigin, gridSize, inlet, nearestCell, outlet } from './geometry';
import { monoHintText, monoHints } from './mono-hint';
import { TouchGestures } from './touch-gestures';

interface GridCell {
  key: string;
  row: number;
  column: number;
  x: number;
  y: number;
  block: PresetBlock | null;
}

interface DrawnCable {
  key: string;
  cable: PresetCable;
  d: string;
}

/** A block's type, parameters and assets, as copied to the clipboard. */
export interface CopiedBlock {
  type: string;
  channels: PresetBlock['channels'];
  assets: PresetBlock['assets'];
}

const BlockMime = 'application/x-cathode-block';
const MinScale = 0.6;
const MaxScale = 2;

function isIo(block: PresetBlock): boolean {
  return block.type === BuiltInTypes.input || block.type === BuiltInTypes.output;
}

/** What pasting does on a cell: place a copy (empty or shunt), copy the settings across (same type), or nothing. */
export function pasteAction(copied: CopiedBlock | null, target: PresetBlock | null): 'place' | 'settings' | null {
  if (!copied) {
    return null;
  }

  if (!target || target.type === BuiltInTypes.shunt) {
    return 'place';
  }

  return target.type === copied.type ? 'settings' : null;
}

/**
 * What dropping a dragged block on a cell does: onto an empty cell or a shunt it moves (⌥ copies), onto another
 * block it swaps the two (⌘ replaces the target). The grid's Input and Output never copy or replace.
 */
export function dropAction(
  dragged: PresetBlock,
  target: PresetBlock | null,
  keys: { altKey: boolean; metaKey: boolean; ctrlKey: boolean }
): 'move' | 'copy' | 'swap' | 'replace' | null {
  if (target?.id === dragged.id) {
    return null;
  }

  if (!target || target.type === BuiltInTypes.shunt) {
    return keys.altKey && !isIo(dragged) ? 'copy' : 'move';
  }

  if (keys.metaKey || keys.ctrlKey) {
    return isIo(dragged) || isIo(target) ? null : 'replace';
  }

  return 'swap';
}

/**
 * The 6 x 14 grid (docs/02-architecture.md §7.3): CSS-positioned cells with an SVG cable layer. Keyboard: arrows move
 * the focus and Enter opens the block palette on an empty cell; the block commands (cut, copy, paste, delete,
 * disconnect, shunt, replace) are app-wide shortcuts that act on the focused cell (shell/shortcuts.ts). Drag a block
 * to move it (⌥ copies), onto another to swap them (⌘ replaces); drag from a block's outlet to a block in the next
 * column to cable them. Library files dropped on a cell are imported and recalled there.
 */
@Component({
  selector: 'cth-grid-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BlockPicker, CthBlockTile, CthMeter, XuiContextMenuImports, XuiMenuImports],
  providers: [
    provideIcons({
      matAddRound,
      matCableRound,
      matContentCopyRound,
      matContentCutRound,
      matContentPasteRound,
      matDeleteRound,
      matFindReplaceRound,
      matLinkOffRound,
      matLinkRound,
      matPowerSettingsNewRound,
      matPublicRound,
      matSaveRound,
      matSwapVertRound
    })
  ],
  host: { class: 'block' },
  template: `
    <div
      #surface
      class="group/grid relative touch-manipulation rounded-lg outline-none select-none [-webkit-touch-callout:none]"
      role="grid"
      tabindex="0"
      aria-label="Signal grid"
      [attr.aria-rowcount]="rows()"
      [attr.aria-colcount]="columns()"
      [attr.aria-activedescendant]="'cell-' + focus().row + '-' + focus().column"
      [style.width.px]="size().width"
      [style.height.px]="size().height"
      [style.zoom]="scale()"
      (keydown)="key($event)"
      (pointermove)="moveWire($event); touches.move($event)"
      (pointerup)="endWire($event); touches.cancel()"
      (pointercancel)="touches.cancel()"
    >
      @for (cell of cells(); track cell.key) {
        <div
          role="gridcell"
          class="outline-focus data-[wire=candidate]:outline-primary data-[wire=target]:outline-primary absolute rounded-lg outline-offset-4 data-focused:group-focus-visible/grid:outline-2 data-[wire=candidate]:outline-2 data-[wire=candidate]:outline-dashed data-[wire=target]:outline-2"
          [id]="'cell-' + cell.row + '-' + cell.column"
          [attr.data-testid]="'cell-' + cell.row + '-' + cell.column"
          [attr.aria-label]="describe(cell)"
          [attr.aria-selected]="cell.block !== null && cell.block.id === live.selectedId()"
          [attr.data-focused]="isFocused(cell) ? '' : null"
          [attr.data-wire]="wireState(cell)"
          [style.left.px]="cell.x"
          [style.top.px]="cell.y"
          [style.width.px]="width"
          [style.height.px]="height"
          [xuiContextMenuTriggerFor]="cell.block ? blockMenu : emptyMenu"
          (pointerdown)="pick(cell); touches.down($event, $any($event.currentTarget))"
          (dblclick)="openPalette()"
          (dragover)="dragOver($event, cell)"
          (drop)="drop($event, cell)"
        >
          @if (cell.block; as block) {
            @if (block.type === 'shunt') {
              <div
                class="group border-border-muted hover:border-border flex size-full items-center rounded-lg border border-dashed"
                draggable="true"
                [attr.title]="'Shunt (' + block.id + ')'"
                (dragstart)="dragStart($event, block)"
              >
                <div class="bg-foreground-muted h-0.5 w-full" [class.bg-primary]="block.id === live.selectedId()"></div>
              </div>
            } @else {
              <cth-block-tile
                class="outline-foreground size-full cursor-grab outline-offset-1"
                draggable="true"
                [class.outline-2]="block.id === live.selectedId()"
                [attr.title]="title(block) + ' (' + block.id + ')'"
                [name]="title(block)"
                [category]="category(block)"
                [typeId]="block.type"
                [bypassed]="live.isBypassed(block.id)"
                (dragstart)="dragStart($event, block)"
              >
                @if (ui.blockInfo()) {
                  <span
                    class="absolute inset-0 grid place-items-center bg-black/55 text-base font-bold tracking-wide"
                    [attr.data-testid]="'info-' + block.id"
                  >
                    {{ abbreviation(block) }}
                  </span>
                  @if (block.global) {
                    <span class="absolute bottom-1 left-1.5 rounded bg-white/85 px-1 text-[9px] font-bold text-black">
                      GLOBAL
                    </span>
                  }
                } @else if (instance(block); as n) {
                  <span class="absolute top-1 left-1.5 text-[10px] font-semibold opacity-80">{{ n }}</span>
                }
                @if (hasChannels(block)) {
                  <span
                    class="absolute top-1 right-1 rounded px-1 text-[10px] font-semibold"
                    [class]="ui.blockInfo() ? 'bg-white/85 text-black' : 'bg-black/35'"
                  >
                    {{ live.channelOf(block.id) }}
                  </span>
                }
                <cth-meter
                  class="pointer-events-none absolute right-1 bottom-0.5 left-1"
                  orientation="horizontal"
                  [attr.data-testid]="'meter-' + block.id"
                  [label]="title(block) + ' output level'"
                  [sources]="meterSources(block.id)"
                />
                @if (monoHint(block); as hint) {
                  <span
                    class="absolute right-1 bottom-3 rounded bg-amber-500/80 px-1 text-[9px] font-bold text-black"
                    data-testid="mono-hint"
                    role="img"
                    [attr.title]="hint"
                    [attr.aria-label]="hint"
                    >MONO</span
                  >
                }
              </cth-block-tile>
            }
          } @else {
            <div
              class="border-border-muted hover:border-border text-foreground-subtle grid size-full place-items-center rounded-lg border border-dashed text-lg"
              aria-hidden="true"
            >
              +
            </div>
          }
        </div>
      }

      <svg
        class="pointer-events-none absolute inset-0 overflow-visible"
        [attr.width]="size().width"
        [attr.height]="size().height"
        aria-hidden="true"
      >
        @for (cable of drawnCables(); track cable.key) {
          <path
            [attr.d]="cable.d"
            class="fill-none"
            [class]="cable.key === selectedCable() ? 'stroke-primary' : 'stroke-foreground-muted'"
            stroke-width="2"
          />
          <path
            [attr.d]="cable.d"
            [attr.data-testid]="'cable-' + cable.key"
            class="pointer-events-auto cursor-pointer fill-none stroke-transparent"
            stroke-width="12"
            (pointerdown)="selectCable($event, cable)"
          />
        }
        @for (block of outlets(); track block.id) {
          <!-- A generous grab area around the small dot, so a cable starts instead of the block moving. -->
          <g
            class="group/outlet pointer-events-auto cursor-crosshair"
            [attr.data-testid]="'outlet-' + block.id"
            (pointerdown)="startWire($event, block)"
          >
            <circle [attr.cx]="outletOf(block).x" [attr.cy]="outletOf(block).y" r="13" class="fill-transparent" />
            <circle
              [attr.cx]="outletOf(block).x"
              [attr.cy]="outletOf(block).y"
              r="5"
              class="fill-surface stroke-foreground-muted group-hover/outlet:stroke-primary transition-[r] group-hover/outlet:[r:7]"
              [class.stroke-primary]="block.id === wiring()?.id"
              stroke-width="2"
            />
          </g>
        }
        @if (wire(); as w) {
          <path [attr.d]="w" class="stroke-primary fill-none" stroke-width="2" stroke-dasharray="6 4" />
        }
      </svg>
    </div>

    <ng-template #blockMenu>
      <xui-menu>
        @if (focusedBlock(); as block) {
          @if (!isIo(block) && block.type !== 'shunt') {
            <button
              xuiMenuItem
              icon="matPowerSettingsNewRound"
              (click)="live.setBypass(block.id, !live.isBypassed(block.id))"
            >
              {{ live.isBypassed(block.id) ? 'Engage' : 'Bypass' }}<span menuItemLabel>Space</span>
            </button>
          }
          @if (hasChannels(block)) {
            <xui-menu-divider title="Channel" />
            @for (channel of channelNames.slice(0, channelCount(block)); track channel) {
              <button
                xuiMenuItem
                [selected]="live.channelOf(block.id) === channel"
                (click)="live.setChannel(block.id, channel)"
              >
                Channel {{ channel }}<span menuItemLabel>{{ channel }}</span>
              </button>
            }
          }
          @if (!isIo(block) && block.type !== 'shunt') {
            <xui-menu-divider title="All scenes" />
            <button xuiMenuItem (click)="ui.allScenes({ bypassed: false }, block)">
              Engage in all scenes<span menuItemLabel>⇧Space</span>
            </button>
            <button xuiMenuItem (click)="ui.allScenes({ bypassed: true }, block)">
              Bypass in all scenes<span menuItemLabel>⇧⌘Space</span>
            </button>
          }
          @if (hasChannels(block)) {
            <button xuiMenuItem [xuiMenuTriggerFor]="allChannels" [xuiMenuTriggerData]="{ block }">
              Channel in all scenes
            </button>
          }
          <xui-menu-divider />
          @if (!isIo(block) && block.type !== 'shunt') {
            <button xuiMenuItem icon="matContentCutRound" (click)="cut(block)">Cut<span menuItemLabel>⌘X</span></button>
            <button xuiMenuItem icon="matContentCopyRound" (click)="copy(block)">
              Copy<span menuItemLabel>⌘C</span>
            </button>
          }
          @if (pasteAction(block) !== null) {
            <button xuiMenuItem icon="matContentPasteRound" (click)="paste()">
              {{ pasteAction(block) === 'settings' ? 'Paste settings (all channels)' : 'Paste' }}
              <span menuItemLabel>⌘V</span>
            </button>
          }
          @if (!isIo(block)) {
            <button xuiMenuItem icon="matFindReplaceRound" (click)="openPalette(true)">
              Replace with…<span menuItemLabel>Q</span>
            </button>
          }
          <button xuiMenuItem icon="matLinkOffRound" (click)="live.disconnect(block.id)">
            Disconnect all cables<span menuItemLabel>⌘D</span>
          </button>
          @if (!isIo(block) && block.type !== 'shunt') {
            <button xuiMenuItem icon="matSaveRound" (click)="ui.saveBlockOpen.set(true)">
              Save to the Library…<span menuItemLabel>⌘B</span>
            </button>
            @if (block.global) {
              <button xuiMenuItem icon="matLinkOffRound" (click)="live.unlinkGlobal(block.id)">
                Unlink from global
              </button>
            } @else {
              <button xuiMenuItem icon="matPublicRound" (click)="ui.makeGlobal(block)">
                Make global<span menuItemLabel>G</span>
              </button>
            }
            @if (globalsOf(block).length > 0) {
              <button xuiMenuItem icon="matLinkRound" [xuiMenuTriggerFor]="linkGlobal" [xuiMenuTriggerData]="{ block }">
                Link to global
              </button>
            }
          }
          <button xuiMenuItem icon="matSwapVertRound" [xuiMenuTriggerFor]="moveLines" [xuiMenuTriggerData]="{ block }">
            Move row or column
          </button>
          <xui-menu-divider />
          <button xuiMenuItem icon="matDeleteRound" color="error" (click)="live.removeBlock(block.id)">
            Remove<span menuItemLabel>⌫</span>
          </button>
        }
      </xui-menu>
    </ng-template>

    <ng-template #linkGlobal let-block="block">
      <xui-menu>
        @for (shared of globalsOf(block); track shared.id) {
          <button
            xuiMenuItem
            [selected]="block.global === shared.id"
            [disabled]="block.global === shared.id"
            (click)="live.useGlobal(block.id, shared.id)"
          >
            {{ shared.name }}
          </button>
        }
      </xui-menu>
    </ng-template>

    <ng-template #allChannels let-block="block">
      <xui-menu>
        @for (channel of channelNames.slice(0, channelCount(block)); track channel) {
          <button xuiMenuItem (click)="ui.allScenes({ channel }, block)">Channel {{ channel }}</button>
        }
      </xui-menu>
    </ng-template>

    <ng-template #moveLines let-block="block">
      <xui-menu>
        <button xuiMenuItem [disabled]="block.row === 0" (click)="moveLine('row', block.row, block.row - 1)">
          Row up
        </button>
        <button xuiMenuItem [disabled]="block.row === rows() - 1" (click)="moveLine('row', block.row, block.row + 1)">
          Row down
        </button>
        <button
          xuiMenuItem
          [disabled]="block.column === 0"
          (click)="moveLine('column', block.column, block.column - 1)"
        >
          Column left
        </button>
        <button
          xuiMenuItem
          [disabled]="block.column === columns() - 1"
          (click)="moveLine('column', block.column, block.column + 1)"
        >
          Column right
        </button>
      </xui-menu>
    </ng-template>

    <ng-template #emptyMenu>
      <xui-menu>
        <button xuiMenuItem icon="matAddRound" (click)="openPalette()">Add block…<span menuItemLabel>↵</span></button>
        <button xuiMenuItem icon="matCableRound" (click)="addShunt()">Add shunt<span menuItemLabel>⌘U</span></button>
        <button xuiMenuItem icon="matContentPasteRound" [disabled]="!clipboard()" (click)="paste()">
          Paste<span menuItemLabel>⌘V</span>
        </button>
        <xui-menu-divider />
        <button xuiMenuItem [disabled]="focus().row === 0" (click)="moveLine('row', focus().row, focus().row - 1)">
          Move row up
        </button>
        <button
          xuiMenuItem
          [disabled]="focus().row === rows() - 1"
          (click)="moveLine('row', focus().row, focus().row + 1)"
        >
          Move row down
        </button>
      </xui-menu>
    </ng-template>

    <cth-block-picker
      [types]="catalog.placeable()"
      [isFull]="atLimit"
      [title]="replacing() ? 'Replace ' + title(replacing()!) + ' with' : 'Add a block'"
      [(open)]="paletteOpen"
      (picked)="place($event)"
    />
  `
})
export class GridEditor {
  protected readonly live = inject(LiveStore);
  private readonly events = inject(EventStream);
  private readonly meters = new Map<string, ReadonlyArray<() => number>>();

  /** A block tile's meter source: its output peak from the latest telemetry frame. One array per block, kept. */
  protected meterSources(id: string): ReadonlyArray<() => number> {
    let sources = this.meters.get(id);
    if (!sources) {
      sources = [() => this.events.blockMeter(id, 'peak') ?? 0];
      this.meters.set(id, sources);
    }

    return sources;
  }
  protected readonly catalog = inject(CatalogStore);
  protected readonly ui = inject(UiStore);
  private readonly blockPresets = inject(BlockPresetsStore);
  private readonly globals = inject(GlobalStore);
  private readonly surface = viewChild.required<ElementRef<HTMLElement>>('surface');

  protected readonly width = Cell.width;
  protected readonly height = Cell.height;
  protected readonly channelNames = ChannelNames;
  protected readonly isIo = isIo;
  protected readonly focus = signal({ row: 0, column: 1 });
  protected readonly paletteOpen = signal(false);
  /** The block the palette replaces, or null when it adds one. */
  protected readonly replacing = signal<PresetBlock | null>(null);
  protected readonly selectedCable = signal<string | null>(null);
  protected readonly clipboard = signal<CopiedBlock | null>(null);
  protected readonly wire = signal<string | null>(null);
  /** The block a cable is being dragged from, the blocks it can reach, and the one under the pointer. */
  protected readonly wiring = signal<PresetBlock | null>(null);
  private readonly wireTargets = computed(() => {
    const from = this.wiring();
    const cables = this.live.preset()?.cables ?? [];
    return new Set(
      from
        ? this.blocks()
            .filter(b => canConnect(from, b, cables, this.blocks()))
            .map(b => b.id)
        : []
    );
  });
  private readonly wireHover = signal<string | null>(null);

  protected readonly rows = computed(() => this.live.preset()?.rows ?? 6);
  protected readonly columns = computed(() => this.live.preset()?.columns ?? 14);
  protected readonly size = computed(() => gridSize(this.rows(), this.columns()));
  /** The panel's width, so the grid can scale to fill it. */
  private readonly available = signal(0);
  /**
   * How much the grid is scaled (CSS zoom, so text and icons stay sharp): it fills the panel's width, up to twice its
   * natural size, and below 60 % it scrolls instead.
   */
  protected readonly scale = computed(() => {
    const available = this.available();
    return available > 0 ? Math.min(MaxScale, Math.max(MinScale, available / this.size().width)) : 1;
  });
  protected readonly blocks = computed(() => this.live.preset()?.blocks ?? []);

  /** The global blocks a block can link to: those of its type. */
  protected globalsOf(block: PresetBlock) {
    return (this.globals.settings()?.blocks ?? []).filter(g => g.type === block.type);
  }

  protected readonly cells = computed<GridCell[]>(() => {
    const byCell = new Map(this.blocks().map(b => [`${b.row}:${b.column}`, b]));
    const cells: GridCell[] = [];
    for (let row = 0; row < this.rows(); row++) {
      for (let column = 0; column < this.columns(); column++) {
        const key = `${row}:${column}`;
        cells.push({ key, row, column, ...cellOrigin(row, column), block: byCell.get(key) ?? null });
      }
    }

    return cells;
  });

  protected readonly drawnCables = computed<DrawnCable[]>(() => {
    const byId = new Map(this.blocks().map(b => [b.id, b]));
    return (this.live.preset()?.cables ?? []).flatMap(cable => {
      const from = byId.get(cable.from);
      const to = byId.get(cable.to);
      return from && to ? [{ key: `${cable.from}>${cable.to}`, cable, d: cablePath(outlet(from), inlet(to)) }] : [];
    });
  });

  /** Blocks offer an outlet to cable from; a shunt only while nothing leaves it, so a shunt row stays quiet. */
  protected readonly outlets = computed(() => {
    const cables = this.live.preset()?.cables ?? [];
    return this.blocks().filter(
      b => b.type !== BuiltInTypes.output && (b.type !== BuiltInTypes.shunt || !cables.some(c => c.from === b.id))
    );
  });
  protected readonly focusedBlock = computed(() => this.cellBlock(this.focus().row, this.focus().column));

  protected readonly atLimit = (type: BlockTypeInfo) =>
    this.blocks().filter(b => b.type === type.typeId).length >= type.maxInstances;

  constructor() {
    const host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    const observer = new ResizeObserver(([entry]) => this.available.set(entry.contentRect.width));
    afterNextRender(() => observer.observe(host));
    const withBlock = (action: (block: PresetBlock) => void) => () => {
      const block = this.focusedBlock();
      if (block) {
        action(block);
      }
    };
    const unregister = inject(ShortcutService).register({
      addBlock: () => this.openPalette(true),
      cut: withBlock(block => this.cut(block)),
      copy: withBlock(block => this.copy(block)),
      paste: () => void this.paste(),
      disconnect: withBlock(block => void this.live.disconnect(block.id)),
      shunt: () => this.addShunt(),
      remove: () => this.remove(),
      removeBackspace: () => this.remove()
    });
    inject(DestroyRef).onDestroy(() => {
      observer.disconnect();
      unregister();
    });
  }

  /** Mono-summing blocks with engaged stereo effects ahead of them (docs/08-blocks-parity.md §2). */
  private readonly monoSources = computed(() =>
    monoHints(
      this.blocks(),
      this.live.preset()?.cables ?? [],
      (block, param) =>
        this.live.valueOf(block, this.live.channelOf(block.id), param) ??
        this.catalog.info(block.type)?.params.find(p => p.id === param)?.default ??
        0,
      block => this.live.isBypassed(block.id)
    )
  );

  /** The marker's explanation, or null for a block that keeps the stereo it gets. */
  protected monoHint(block: PresetBlock): string | null {
    const sources = this.monoSources().get(block.id);
    return sources ? monoHintText(sources, b => `${this.title(b)} (${b.id})`) : null;
  }

  protected title(block: PresetBlock): string {
    return this.catalog.info(block.type)?.name ?? block.type;
  }

  protected category(block: PresetBlock): string {
    return this.catalog.info(block.type)?.category ?? block.type;
  }

  protected abbreviation(block: PresetBlock): string {
    return abbreviate(this.title(block), block.id);
  }

  /** The instance number when there is more than one of a type (drive2), so tiles of one type tell apart. */
  protected instance(block: PresetBlock): string | null {
    const n = /\d+$/.exec(block.id)?.[0] ?? null;
    return n !== null && n !== '1' ? n : null;
  }

  protected channelCount(block: PresetBlock): number {
    return this.catalog.info(block.type)?.channels ?? 0;
  }

  protected hasChannels(block: PresetBlock): boolean {
    return this.channelCount(block) > 1;
  }

  protected pasteAction(target: PresetBlock | null): 'place' | 'settings' | null {
    return pasteAction(this.clipboard(), target);
  }

  protected outletOf(block: PresetBlock): Point {
    return outlet(block);
  }

  protected isFocused(cell: GridCell): boolean {
    return cell.row === this.focus().row && cell.column === this.focus().column;
  }

  protected describe(cell: GridCell): string {
    const place = `row ${cell.row + 1}, column ${cell.column + 1}`;
    const block = cell.block;
    if (!block) {
      return `Empty, ${place}`;
    }

    if (block.type === BuiltInTypes.shunt) {
      return `Shunt, ${place}`;
    }

    const state = this.live.isBypassed(block.id) ? ', bypassed' : '';
    const channel = this.hasChannels(block) ? `, channel ${this.live.channelOf(block.id)}` : '';
    return `${this.title(block)} ${block.id}${state}${channel}, ${place}`;
  }

  /** A double tap adds a block like a double click; a long press opens the cell's menu like a right click. */
  protected readonly touches = new TouchGestures<HTMLElement>(
    () => this.openPalette(),
    (cell, x, y) =>
      cell.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: x, clientY: y }))
  );

  protected pick(cell: GridCell): void {
    this.focus.set({ row: cell.row, column: cell.column });
    this.selectedCable.set(null);
    this.live.selectedId.set(cell.block && cell.block.type !== BuiltInTypes.shunt ? cell.block.id : null);
  }

  /** Opens the palette on an empty cell (or a shunt); with `replace`, on a block too, to replace it. */
  protected openPalette(replace = false): void {
    const block = this.focusedBlock();
    if (!block || block.type === BuiltInTypes.shunt) {
      this.replacing.set(null);
      this.paletteOpen.set(true);
    } else if (replace && !isIo(block)) {
      this.replacing.set(block);
      this.paletteOpen.set(true);
    }
  }

  protected async place(type: BlockTypeInfo): Promise<void> {
    const replacing = this.replacing();
    if (replacing) {
      this.replacing.set(null);
      if (await this.live.replaceBlock(replacing.id, type.typeId)) {
        this.live.selectedId.set(this.cellBlock(replacing.row, replacing.column)?.id ?? null);
      }

      return;
    }

    const { row, column } = this.focus();
    if (await this.live.addBlock(type.typeId, row, column)) {
      this.live.selectedId.set(this.cellBlock(row, column)?.id ?? null);
    }
  }

  protected addShunt(): void {
    const { row, column } = this.focus();
    if (!this.cellBlock(row, column)) {
      void this.live.addBlock(BuiltInTypes.shunt, row, column);
    }
  }

  protected moveLine(axis: 'row' | 'column', from: number, to: number): void {
    void this.live.moveLine(axis, from, to);
    this.focus.update(f => (axis === 'row' ? { ...f, row: to } : { ...f, column: to }));
  }

  protected copy(block: PresetBlock): void {
    if (!isIo(block) && block.type !== BuiltInTypes.shunt) {
      this.clipboard.set({
        type: block.type,
        channels: structuredClone(block.channels),
        assets: { ...block.assets }
      });
    }
  }

  protected cut(block: PresetBlock): void {
    if (!isIo(block) && block.type !== BuiltInTypes.shunt) {
      this.copy(block);
      void this.live.removeBlock(block.id);
    }
  }

  /** Pastes on the focused cell: a copy of the block on an empty cell, its settings onto a block of its type. */
  protected async paste(): Promise<void> {
    const copied = this.clipboard();
    const target = this.focusedBlock();
    const action = pasteAction(copied, target);
    if (copied && action === 'place') {
      await this.placeCopy(copied, this.focus().row, this.focus().column);
    } else if (copied && target && action === 'settings') {
      await this.live.pasteSettings(target.id, copied.type, copied.channels, copied.assets);
    }
  }

  protected key(event: KeyboardEvent): void {
    const { row, column } = this.focus();
    const moves: Record<string, [number, number]> = {
      ArrowUp: [-1, 0],
      ArrowDown: [1, 0],
      ArrowLeft: [0, -1],
      ArrowRight: [0, 1]
    };
    const command = event.metaKey || event.ctrlKey;
    if (event.key in moves && !command) {
      const [dr, dc] = moves[event.key];
      const next = {
        row: Math.max(0, Math.min(this.rows() - 1, row + dr)),
        column: Math.max(0, Math.min(this.columns() - 1, column + dc))
      };
      this.focus.set(next);
      const at = this.cellBlock(next.row, next.column);
      this.live.selectedId.set(at && at.type !== BuiltInTypes.shunt ? at.id : null);
    } else if (event.key === 'Enter' && !command) {
      this.openPalette();
    } else {
      return; // the rest are app-wide shortcuts (shell/shortcuts.ts)
    }

    event.preventDefault();
  }

  protected dragStart(event: DragEvent, block: PresetBlock): void {
    event.dataTransfer?.setData(BlockMime, block.id);
    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = 'all';
    }
  }

  protected dragOver(event: DragEvent, cell: GridCell): void {
    const types = event.dataTransfer?.types ?? [];
    if (types.includes('Files')) {
      event.preventDefault();
      event.dataTransfer!.dropEffect = 'copy';
    } else if (types.includes(BlockMime)) {
      // The dragged id cannot be read before the drop: any cell accepts it, and drop() decides what it does.
      event.preventDefault();
      const onBlock = cell.block !== null && cell.block.type !== BuiltInTypes.shunt;
      event.dataTransfer!.dropEffect =
        onBlock && !(event.metaKey || event.ctrlKey) ? 'link' : event.altKey || onBlock ? 'copy' : 'move';
    }
  }

  protected async drop(event: DragEvent, cell: GridCell): Promise<void> {
    event.preventDefault();
    this.focus.set({ row: cell.row, column: cell.column });
    const files = [...(event.dataTransfer?.files ?? [])].filter(f => f.name.endsWith(BlockFileExtension));
    if (files.length > 0) {
      await this.dropLibraryFiles(files, cell);
      return;
    }

    const block = this.blocks().find(b => b.id === event.dataTransfer?.getData(BlockMime));
    const action = block ? dropAction(block, cell.block, event) : null;
    if (!block || !action) {
      return;
    }

    if (action === 'copy') {
      await this.placeCopy({ type: block.type, channels: block.channels, assets: block.assets }, cell.row, cell.column);
    } else if (action === 'move') {
      await this.live.moveBlock(block.id, cell.row, cell.column);
    } else {
      await this.live.dropBlock(block.id, cell.block!.id, action);
    }
  }

  protected selectCable(event: PointerEvent, cable: DrawnCable): void {
    event.stopPropagation();
    this.selectedCable.set(cable.key);
    this.surface().nativeElement.focus();
  }

  /** How a cell shows during a cable drag: a block it can connect to, or the one it would connect to now. */
  protected wireState(cell: GridCell): 'candidate' | 'target' | null {
    const id = cell.block?.id;
    if (!id || !this.wireTargets().has(id)) {
      return null;
    }

    return id === this.wireHover() ? 'target' : 'candidate';
  }

  protected startWire(event: PointerEvent, block: PresetBlock): void {
    event.stopPropagation();
    event.preventDefault();
    this.wiring.set(block);
    this.surface().nativeElement.setPointerCapture(event.pointerId);
    this.moveWire(event);
  }

  protected moveWire(event: PointerEvent): void {
    const from = this.wiring();
    if (from) {
      const point = this.local(event);
      this.wire.set(cablePath(outlet(from), point));
      this.wireHover.set(this.wireTarget(point)?.id ?? null);
    }
  }

  protected endWire(event: PointerEvent): void {
    const from = this.wiring();
    if (!from) {
      return;
    }

    const to = this.wireTarget(this.local(event));
    this.wiring.set(null);
    this.wire.set(null);
    this.wireHover.set(null);
    if (to) {
      void this.live.addCable(from.id, to.id);
    }
  }

  private remove(): void {
    const cable = this.drawnCables().find(c => c.key === this.selectedCable());
    const block = this.focusedBlock();
    if (cable) {
      void this.live.removeCable(cable.cable.from, cable.cable.to);
      this.selectedCable.set(null);
    } else if (block) {
      void this.live.removeBlock(block.id);
    }
  }

  /**
   * Library files dropped on the grid are imported; the first entry then lands on the cell: recalled into a block of
   * its type (all channels), or added as a new block on an empty cell.
   */
  private async dropLibraryFiles(files: File[], cell: GridCell): Promise<void> {
    const added = (await Promise.all(files.map(f => this.blockPresets.import(f)))).flat();
    const entry = added[0];
    if (!entry) {
      return;
    }

    let target = cell.block && cell.block.type !== BuiltInTypes.shunt ? cell.block : null;
    if (!target && (await this.live.addBlock(entry.type, cell.row, cell.column))) {
      target = this.cellBlock(cell.row, cell.column);
    }

    if (target?.type === entry.type) {
      await this.blockPresets.apply(target.id, entry.id, { all: true });
      this.live.selectedId.set(target.id);
    }
  }

  /** The block a cable dropped at this point would connect to, if the connection is allowed. */
  private wireTarget(point: Point): PresetBlock | null {
    const cell = nearestCell(point, this.rows(), this.columns());
    const block = cell ? this.cellBlock(cell.row, cell.column) : null;
    return block && this.wireTargets().has(block.id) ? block : null;
  }

  /** A pointer position in grid coordinates (unscaled pixels, as the geometry works in). */
  private local(event: PointerEvent): Point {
    const rect = this.surface().nativeElement.getBoundingClientRect();
    const scale = rect.width / this.size().width || 1;
    return { x: (event.clientX - rect.left) / scale, y: (event.clientY - rect.top) / scale };
  }

  private cellBlock(row: number, column: number): PresetBlock | null {
    return this.blocks().find(b => b.row === row && b.column === column) ?? null;
  }

  /** Adds a block of the copied type and gives it the copy's parameters on every channel, and its assets. */
  private async placeCopy(copied: CopiedBlock, row: number, column: number): Promise<void> {
    if (!(await this.live.addBlock(copied.type, row, column))) {
      return;
    }

    const placed = this.cellBlock(row, column);
    if (!placed) {
      return;
    }

    for (const [channel, values] of Object.entries(copied.channels)) {
      if (Object.keys(values).length > 0) {
        await this.live.mutate(c =>
          c.PATCH('/api/v1/live/blocks/{id}/params', {
            params: { path: { id: placed.id }, query: { channel: channel as ChannelName } },
            body: values
          })
        );
      }
    }

    for (const [slot, asset] of Object.entries(copied.assets)) {
      await this.live.setAsset(placed.id, slot, asset);
    }

    this.live.selectedId.set(placed.id);
  }
}
