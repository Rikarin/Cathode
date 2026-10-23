import { BlockIcons, CategoryIcons, blockIconName } from './block-icons';
import { BlockCategories, blockCategoryStyle, blockGradient } from './block-tile';

describe('block category styles', () => {
    it('give each known category its own colour', () => {
        const looks = Object.values(BlockCategories).map(s => `${s.hue}/${s.chroma}`);
        expect(new Set(looks).size).toBe(looks.length);
    });

    it('give a category the editor has not seen a stable colour', () => {
        const style = blockCategoryStyle('Spectral');
        expect(blockCategoryStyle('Spectral')).toEqual(style);
        expect(style.hue).toBeGreaterThanOrEqual(0);
        expect(style.hue).toBeLessThan(360);
        expect(blockGradient('Spectral')).toContain(`${style.hue})`);
    });
});

describe('block icons', () => {
    it('fall back from the type to its category, then to a generic block', () => {
        expect(blockIconName('wah', 'Filter')).toBe('cthBlockWah');
        expect(blockIconName('circuitamp', 'Amp')).toBe('cthBlockAmp');
        expect(blockIconName(null, 'Spectral')).toBe('cthBlockGeneric');
    });

    it('are all drawn', () => {
        for (const name of [...Object.values(CategoryIcons), 'cthBlockGeneric', blockIconName('vibe', '')]) {
            expect(BlockIcons[name]).toMatch(/^<svg .*<\/svg>$/);
        }
    });
});
