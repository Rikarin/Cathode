import { frequencyToX, levelToY, RtaFloorDb } from './rta';

describe('rta scales', () => {
    it('spans 20 Hz to 20 kHz on a log axis', () => {
        expect(frequencyToX(20, 300)).toBe(0);
        expect(frequencyToX(632.46, 300)).toBeCloseTo(150, 1);
        expect(frequencyToX(20_000, 300)).toBe(300);
        expect(frequencyToX(50_000, 300)).toBe(300);
    });

    it('puts 0 dB at the top and the floor at the bottom, clamping beyond', () => {
        expect(levelToY(0, 200)).toBeCloseTo(0);
        expect(levelToY(RtaFloorDb / 2, 200)).toBe(100);
        expect(levelToY(-500, 200)).toBe(200);
        expect(levelToY(6, 200)).toBeCloseTo(0);
    });
});
