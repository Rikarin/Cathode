import { trigger } from './scope';

describe('scope trigger', () => {
    it('starts at the first rising zero crossing', () => {
        expect(trigger([0.5, -0.2, -0.1, 0.3, 0.8, -0.4, 0.2], 4)).toBe(3);
    });

    it('free-runs from the latest start when nothing crosses upward', () => {
        expect(trigger([0.1, 0.2, 0.3, 0.4], 2)).toBe(2);
    });
});
