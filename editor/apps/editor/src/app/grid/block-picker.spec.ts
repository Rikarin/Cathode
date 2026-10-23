import type { BlockTypeInfo } from '@cathode/api-client';
import { bestMatch, groupBlocks } from './block-picker';

function type(typeId: string, name: string, category: string): BlockTypeInfo {
    return { typeId, name, category, maxInstances: 2, channels: 4, assetSlots: [], params: [], cpuCost: 0 };
}

const types = [
    type('reverb', 'Reverb', 'Reverb'),
    type('vocoder', 'Vocoder', 'Spectral'),
    type('multidelay', 'Multitap Delay', 'Delay'),
    type('delay', 'Delay', 'Delay'),
    type('drive', 'Drive', 'Drive'),
    type('granular', 'Granular', 'Atmosphere')
];
const none = () => false;

describe('the block picker', () => {
    it('groups known categories in signal-chain order, then new ones alphabetically', () => {
        const groups = groupBlocks(types, '', none);
        expect(groups.map(g => g.category)).toEqual(['Drive', 'Delay', 'Reverb', 'Atmosphere', 'Spectral']);
        expect(groups[1].items.map(i => i.type.name)).toEqual(['Delay', 'Multitap Delay']);
        expect(groups.flatMap(g => g.items).map(i => i.index)).toEqual([0, 1, 2, 3, 4, 5]);
    });

    it('matches every word against name, category and type id', () => {
        expect(groupBlocks(types, 'delay', none).flatMap(g => g.items.map(i => i.type.typeId))).toEqual([
            'delay',
            'multidelay'
        ]);
        expect(groupBlocks(types, 'tap del', none).flatMap(g => g.items.map(i => i.type.typeId))).toEqual([
            'multidelay'
        ]);
        expect(groupBlocks(types, 'spectral', none)[0].items[0].type.typeId).toBe('vocoder');
        expect(groupBlocks(types, 'nothing', none)).toEqual([]);
    });

    it('offers the exact name first, then a prefix, and never a full type', () => {
        const items = groupBlocks(types, 'delay', none).flatMap(g => g.items);
        expect(bestMatch(items, 'delay')?.type.typeId).toBe('delay');
        expect(bestMatch(items, 'multi')?.type.typeId).toBe('multidelay');

        const full = groupBlocks(types, 'delay', t => t.typeId === 'delay').flatMap(g => g.items);
        expect(bestMatch(full, 'delay')?.type.typeId).toBe('multidelay');
        expect(bestMatch([], 'delay')).toBeNull();
    });
});
