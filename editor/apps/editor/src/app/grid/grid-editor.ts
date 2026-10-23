import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import {
  type BlockTypeInfo,
  BuiltInTypes,
  type ChannelName,
  ChannelNames,
  type PresetBlock,
  type PresetCable
} from '@cathode/api-client';
import { CthBlockTile } from '@cathode/audio-ui';
import { provideIcons } from '@ng-icons/core';
import {
  matAddRound,
  matCableRound,
  matContentCopyRound,
  matContentPasteRound,
  matDeleteRound,
  matPowerSettingsNewRound
} from '@ng-icons/material-icons/round';
import { XuiContextMenuImports } from '@xui/context-menu';
import { XuiMenuImports } from '@xui/menu';
import { CatalogStore } from '../state/catalog.store';
import { LiveStore } from '../state/live.store';
import { BlockPicker } from './block-picker';
import { Cell, type Point, cablePath, canConnect, cellAt, cellOrigin, gridSize, inlet, outlet } from './geometry';

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
interface CopiedBlock {
  type: string;
  channels: PresetBlock['channels'];
  assets: PresetBlock['assets'];
}

const BlockMime = 'application/x-cathode-block';

/**
 * The 6 x 14 grid (docs/02-architecture.md §7.3): CSS-positioned cells with an SVG cable layer. Keyboard: arrows move
 * the focus, Enter opens the block palette on an empty cell, Delete removes the focused block or selected cable,
 * B bypasses, ⌘C/⌘V copy and paste. Drag a block to move it (⌥ to copy); drag from a block's outlet to a block in
 * the next column to cable them.
 */
@Component({
  selector: 'cth-grid-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BlockPicker, CthBlockTile, XuiContextMenuImports, XuiMenuImports],
  providers: [
    provideIcons({
      matAddRound,
      matCableRound,
      matContentCopyRound,
      matContentPasteRound,
      matDeleteRound,
      matPowerSettingsNewRound
    })
  ],
  host: { class: 'block' },
  template: `
    <div
      #surface
      class="group/grid relative rounded-lg outline-none"
      role="grid"
      tabindex="0"
      aria-label="Signal grid"
      [attr.aria-rowcount]="rows()"
      [attr.aria-colcount]="columns()"
      [attr.aria-activedescendant]="'cell-' + focus().row + '-' + focus().column"
      [style.width.px]="size().width"
      [style.height.px]="size().height"
      (keydown)="key($event)"
      (pointermove)="moveWire($event)"
      (pointerup)="endWire($event)"
    >
      @for (cell of cells(); track cell.key) {
        <div
          role="gridcell"
          class="outline-focus absolute rounded-lg outline-offset-4 data-focused:group-focus-visible/grid:outline-2"
          [id]="'cell-' + cell.row + '-' + cell.column"
          [attr.data-testid]="'cell-' + cell.row + '-' + cell.column"
          [attr.aria-label]="describe(cell)"
          [attr.aria-selected]="cell.block !== null && cell.block.id === live.selectedId()"
          [attr.data-focused]="isFocused(cell) ? '' : null"
          [style.left.px]="cell.x"
          [style.top.px]="cell.y"
          [style.width.px]="width"
          [style.height.px]="height"
          [xuiContextMenuTriggerFor]="cell.block ? blockMenu : emptyMenu"
          (pointerdown)="pick(cell)"
          (dblclick)="openPalette()"
          (dragover)="dragOver($event, cell)"
          (drop)="drop($event, cell)"
        >
          @if (cell.block; as block) {
            @if (block.type === 'shunt') {
              <div
                class="group flex size-full items-center"
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
                @if (instance(block); as n) {
                  <span class="absolute top-1 left-1.5 text-[10px] font-semibold opacity-80">{{ n }}</span>
                }
                @if (hasChannels(block)) {
                  <span class="absolute top-1 right-1 rounded bg-black/35 px-1 text-[10px] font-semibold">
                    {{ live.channelOf(block.id) }}
                  </span>
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
          <circle
            [attr.cx]="outletOf(block).x"
            [attr.cy]="outletOf(block).y"
            r="5"
            [attr.data-testid]="'outlet-' + block.id"
            class="fill-surface stroke-foreground-muted hover:stroke-primary pointer-events-auto cursor-crosshair"
            stroke-width="2"
            (pointerdown)="startWire($event, block)"
          />
        }
        @if (wire(); as w) {
          <path [attr.d]="w" class="stroke-primary fill-none" stroke-width="2" stroke-dasharray="6 4" />
        }
      </svg>
    </div>

    <ng-template #blockMenu>
      <xui-menu>
        @if (focusedBlock(); as block) {
          <button
            xuiMenuItem
            icon="matPowerSettingsNewRound"
            (click)="live.setBypass(block.id, !live.isBypassed(block.id))"
          >
            {{ live.isBypassed(block.id) ? 'Engage' : 'Bypass' }}<span menuItemLabel>B</span>
          </button>
          @if (hasChannels(block)) {
            <xui-menu-divider title="Channel" />
            @for (channel of channelNames; track channel) {
              <button
                xuiMenuItem
                [selected]="live.channelOf(block.id) === channel"
                (click)="live.setChannel(block.id, channel)"
              >
                Channel {{ channel }}
              </button>
            }
          }
          <xui-menu-divider />
          <button xuiMenuItem icon="matContentCopyRound" (click)="copy(block)">
            Copy<span menuItemLabel>⌘C</span>
          </button>
          <button xuiMenuItem icon="matDeleteRound" color="error" (click)="live.removeBlock(block.id)">
            Remove<span menuItemLabel>⌫</span>
          </button>
        }
      </xui-menu>
    </ng-template>

    <ng-template #emptyMenu>
      <xui-menu>
        <button xuiMenuItem icon="matAddRound" (click)="openPalette()">Add block…<span menuItemLabel>↵</span></button>
        <button xuiMenuItem icon="matCableRound" (click)="addShunt()">Add shunt</button>
        <button xuiMenuItem icon="matContentPasteRound" [disabled]="!clipboard()" (click)="paste()">
          Paste<span menuItemLabel>⌘V</span>
        </button>
      </xui-menu>
    </ng-template>

    <cth-block-picker
      [types]="catalog.placeable()"
      [isFull]="atLimit"
      [(open)]="paletteOpen"
      (picked)="place($event)"
    />
  `
})
export class GridEditor {
  protected readonly live = inject(LiveStore);
  protected readonly catalog = inject(CatalogStore);
  private readonly surface = viewChild.required<ElementRef<HTMLElement>>('surface');

  protected readonly width = Cell.width;
  protected readonly height = Cell.height;
  protected readonly channelNames = ChannelNames;
  protected readonly focus = signal({ row: 0, column: 1 });
  protected readonly paletteOpen = signal(false);
  protected readonly selectedCable = signal<string | null>(null);
  protected readonly clipboard = signal<CopiedBlock | null>(null);
  protected readonly wire = signal<string | null>(null);
  private wireFrom: PresetBlock | null = null;

  protected readonly rows = computed(() => this.live.preset()?.rows ?? 6);
  protected readonly columns = computed(() => this.live.preset()?.columns ?? 14);
  protected readonly size = computed(() => gridSize(this.rows(), this.columns()));
  protected readonly blocks = computed(() => this.live.preset()?.blocks ?? []);

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

  protected title(block: PresetBlock): string {
    return this.catalog.info(block.type)?.name ?? block.type;
  }

  protected category(block: PresetBlock): string {
    return this.catalog.info(block.type)?.category ?? block.type;
  }

  /** The instance number when there is more than one of a type (drive2), so tiles of one type tell apart. */
  protected instance(block: PresetBlock): string | null {
    const n = /\d+$/.exec(block.id)?.[0] ?? null;
    return n !== null && n !== '1' ? n : null;
  }

  protected hasChannels(block: PresetBlock): boolean {
    return (this.catalog.info(block.type)?.channels ?? 0) > 1;
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

  protected pick(cell: GridCell): void {
    this.focus.set({ row: cell.row, column: cell.column });
    this.selectedCable.set(null);
    this.live.selectedId.set(cell.block && cell.block.type !== BuiltInTypes.shunt ? cell.block.id : null);
  }

  protected openPalette(): void {
    if (!this.focusedBlock() || this.focusedBlock()?.type === BuiltInTypes.shunt) {
      this.paletteOpen.set(true);
    }
  }

  protected async place(type: BlockTypeInfo): Promise<void> {
    const { row, column } = this.focus();
    if (await this.live.addBlock(type.typeId, row, column)) {
      this.live.selectedId.set(this.cellBlock(row, column)?.id ?? null);
    }
  }

  protected addShunt(): void {
    const { row, column } = this.focus();
    void this.live.addBlock(BuiltInTypes.shunt, row, column);
  }

  protected copy(block: PresetBlock): void {
    if (block.type !== BuiltInTypes.input && block.type !== BuiltInTypes.output) {
      this.clipboard.set({
        type: block.type,
        channels: structuredClone(block.channels),
        assets: { ...block.assets }
      });
    }
  }

  protected async paste(): Promise<void> {
    const copied = this.clipboard();
    if (copied) {
      await this.placeCopy(copied, this.focus().row, this.focus().column);
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
    const block = this.focusedBlock();
    const command = event.metaKey || event.ctrlKey;
    if (event.key in moves) {
      const [dr, dc] = moves[event.key];
      const next = {
        row: Math.max(0, Math.min(this.rows() - 1, row + dr)),
        column: Math.max(0, Math.min(this.columns() - 1, column + dc))
      };
      this.focus.set(next);
      const at = this.cellBlock(next.row, next.column);
      this.live.selectedId.set(at && at.type !== BuiltInTypes.shunt ? at.id : null);
    } else if (event.key === 'Enter') {
      this.openPalette();
    } else if (event.key === 'Delete' || event.key === 'Backspace') {
      const cable = this.drawnCables().find(c => c.key === this.selectedCable());
      if (cable) {
        void this.live.removeCable(cable.cable.from, cable.cable.to);
        this.selectedCable.set(null);
      } else if (block) {
        void this.live.removeBlock(block.id);
      }
    } else if (event.key.toLowerCase() === 'b' && !command && block && block.type !== BuiltInTypes.shunt) {
      void this.live.setBypass(block.id, !this.live.isBypassed(block.id));
    } else if (command && event.key.toLowerCase() === 'c' && block) {
      this.copy(block);
    } else if (command && event.key.toLowerCase() === 'v') {
      void this.paste();
    } else {
      return;
    }

    event.preventDefault();
  }

  protected dragStart(event: DragEvent, block: PresetBlock): void {
    event.dataTransfer?.setData(BlockMime, block.id);
    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = 'copyMove';
    }
  }

  protected dragOver(event: DragEvent, cell: GridCell): void {
    if (event.dataTransfer?.types.includes(BlockMime) && (!cell.block || cell.block.type === BuiltInTypes.shunt)) {
      event.preventDefault();
      event.dataTransfer.dropEffect = event.altKey ? 'copy' : 'move';
    }
  }

  protected async drop(event: DragEvent, cell: GridCell): Promise<void> {
    event.preventDefault();
    const block = this.blocks().find(b => b.id === event.dataTransfer?.getData(BlockMime));
    if (!block) {
      return;
    }

    this.focus.set({ row: cell.row, column: cell.column });
    if (event.altKey && block.type !== BuiltInTypes.input && block.type !== BuiltInTypes.output) {
      await this.placeCopy({ type: block.type, channels: block.channels, assets: block.assets }, cell.row, cell.column);
    } else {
      await this.live.moveBlock(block.id, cell.row, cell.column);
    }
  }

  protected selectCable(event: PointerEvent, cable: DrawnCable): void {
    event.stopPropagation();
    this.selectedCable.set(cable.key);
    this.surface().nativeElement.focus();
  }

  protected startWire(event: PointerEvent, block: PresetBlock): void {
    event.stopPropagation();
    event.preventDefault();
    this.wireFrom = block;
    this.surface().nativeElement.setPointerCapture(event.pointerId);
    this.moveWire(event);
  }

  protected moveWire(event: PointerEvent): void {
    if (this.wireFrom) {
      this.wire.set(cablePath(outlet(this.wireFrom), this.local(event)));
    }
  }

  protected endWire(event: PointerEvent): void {
    const from = this.wireFrom;
    if (!from) {
      return;
    }

    this.wireFrom = null;
    this.wire.set(null);
    const target = cellAt(this.local(event), this.rows(), this.columns());
    const to = target ? this.cellBlock(target.row, target.column) : null;
    if (to && canConnect(from, to, this.live.preset()?.cables ?? [])) {
      void this.live.addCable(from.id, to.id);
    }
  }

  private local(event: PointerEvent): Point {
    const rect = this.surface().nativeElement.getBoundingClientRect();
    return { x: event.clientX - rect.left, y: event.clientY - rect.top };
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
