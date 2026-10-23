import { describeMidi } from './global-page';

describe('describeMidi', () => {
    it('reads channel messages the way a guitarist would say them', () => {
        expect(describeMidi({ status: 0xb0, data1: 11, data2: 64 })).toBe('CC 11 = 64 (ch 1)');
        expect(describeMidi({ status: 0x93, data1: 60, data2: 100 })).toBe('Note on 60 vel 100 (ch 4)');
        expect(describeMidi({ status: 0x90, data1: 60, data2: 0 })).toBe('Note off 60 (ch 1)');
        expect(describeMidi({ status: 0xcf, data1: 5, data2: 0 })).toBe('Program 5 (ch 16)');
    });
});
