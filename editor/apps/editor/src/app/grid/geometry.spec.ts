import type { PresetBlock } from '@cathode/api-client';
import { Cell, canConnect, cellAt, cellOrigin, inlet, outlet } from './geometry';

const block = (id: string, row: number, column: number, type = 'delay'): PresetBlock => ({
    id,
    type,
    row,
    column,
    channels: {},
    assets: {},
    bypassMode: 'thru'
});

describe('grid geometry', () => {
    it('places cells with gaps and finds them again', () => {
        const origin = cellOrigin(2, 3);
        expect(origin).toEqual({ x: 3 * (Cell.width + Cell.gap), y: 2 * (Cell.height + Cell.gap) });
        expect(cellAt({ x: origin.x + 1, y: origin.y + 1 }, 6, 14)).toEqual({ row: 2, column: 3 });
        expect(cellAt({ x: origin.x + Cell.width + 2, y: origin.y }, 6, 14)).toBeNull(); // in the gap
        expect(cellAt({ x: -1, y: 0 }, 6, 14)).toBeNull();
    });

    it('runs cables from the right edge to the left edge', () => {
        expect(outlet(block('a', 0, 0)).x).toBe(Cell.width);
        expect(inlet(block('b', 0, 1)).x).toBe(Cell.width + Cell.gap);
    });

    it('only connects to the next column, never into an input or twice', () => {
        const a = block('a', 0, 2);
        expect(canConnect(a, block('b', 3, 3), [])).toBe(true);
        expect(canConnect(a, block('b', 0, 4), [])).toBe(false);
        expect(canConnect(a, block('b', 0, 1), [])).toBe(false);
        expect(canConnect(a, block('in', 0, 3, 'input'), [])).toBe(false);
        expect(canConnect(a, block('b', 0, 3), [{ from: 'a', to: 'b' }])).toBe(false);
    });
});
