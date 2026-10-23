import { shape } from './modifier-dialog';

describe('modifier curves (as the engine applies them)', () => {
    it('pin both ends and bend the middle the documented way', () => {
        for (const curve of ['linear', 'exp', 'log', 'scurve']) {
            expect(shape(curve, 0)).toBeCloseTo(0, 6);
            expect(shape(curve, 1)).toBeCloseTo(1, 6);
        }

        expect(shape('linear', 0.5)).toBe(0.5);
        expect(shape('exp', 0.5)).toBeLessThan(0.5);
        expect(shape('log', 0.5)).toBeGreaterThan(0.5);
        expect(shape('scurve', 0.5)).toBeCloseTo(0.5, 6);
        expect(shape('scurve', 0.25)).toBeLessThan(0.25);
    });
});
