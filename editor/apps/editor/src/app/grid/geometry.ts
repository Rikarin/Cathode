import type { PresetBlock, PresetCable } from '@cathode/api-client';

/** Grid metrics in CSS pixels. */
export const Cell = { width: 84, height: 60, gap: 12 } as const;

export interface Point {
  x: number;
  y: number;
}

export function cellOrigin(row: number, column: number): Point {
  return { x: column * (Cell.width + Cell.gap), y: row * (Cell.height + Cell.gap) };
}

export function gridSize(rows: number, columns: number): { width: number; height: number } {
  return {
    width: columns * Cell.width + (columns - 1) * Cell.gap,
    height: rows * Cell.height + (rows - 1) * Cell.gap
  };
}

/** Where a block's output leaves (right edge, middle). */
export function outlet(block: Pick<PresetBlock, 'row' | 'column'>): Point {
  const o = cellOrigin(block.row, block.column);
  return { x: o.x + Cell.width, y: o.y + Cell.height / 2 };
}

/** Where a block's input arrives (left edge, middle). */
export function inlet(block: Pick<PresetBlock, 'row' | 'column'>): Point {
  const o = cellOrigin(block.row, block.column);
  return { x: o.x, y: o.y + Cell.height / 2 };
}

/** An S-curve between two points, flat at both ends like a patch cable on the Axe-Edit grid. */
export function cablePath(from: Point, to: Point): string {
  const bend = Math.max(Cell.gap, (to.x - from.x) / 2);
  return `M ${from.x} ${from.y} C ${from.x + bend} ${from.y}, ${to.x - bend} ${to.y}, ${to.x} ${to.y}`;
}

/** The engine's rule (GraphCompiler): signal flows left to right, one column at a time. */
export function canConnect(from: PresetBlock, to: PresetBlock, cables: readonly PresetCable[]): boolean {
  return (
    to.column === from.column + 1 &&
    to.type !== 'input' &&
    from.type !== 'output' &&
    !cables.some(c => c.from === from.id && c.to === to.id)
  );
}

/** The cell under a point in grid coordinates, or null in a gap or outside. */
export function cellAt(point: Point, rows: number, columns: number): { row: number; column: number } | null {
  const column = Math.floor(point.x / (Cell.width + Cell.gap));
  const row = Math.floor(point.y / (Cell.height + Cell.gap));
  const inside =
    point.x - column * (Cell.width + Cell.gap) <= Cell.width && point.y - row * (Cell.height + Cell.gap) <= Cell.height;
  return inside && row >= 0 && column >= 0 && row < rows && column < columns ? { row, column } : null;
}
