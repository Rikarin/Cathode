import { PeqType, peqBandDb, variableQ } from './peq';

const band = (overrides: Partial<Parameters<typeof peqBandDb>[0]>) => ({
  band: 1,
  type: PeqType.shelf,
  frequency: 1_000,
  gain: 0,
  q: 0.7071,
  slope: 1,
  ...overrides
});

describe('peqBandDb', () => {
  it('puts half a Shelving 2 band at its frequency and all of it far away', () => {
    const shelf = band({ type: PeqType.shelf2, gain: 9 });
    expect(peqBandDb(shelf, 1_000)).toBeCloseTo(4.5, 1);
    expect(peqBandDb(shelf, 10)).toBeCloseTo(9, 0);
    expect(peqBandDb({ ...shelf, band: 5 }, 1_000)).toBeCloseTo(4.5, 1);
  });

  it('draws a blocking band at -3 dB at its frequency on every slope', () => {
    for (const slope of [0, 1, 3, 5]) {
      expect(peqBandDb(band({ type: PeqType.blocking, slope }), 1_000)).toBeCloseTo(-3, 0);
    }

    expect(peqBandDb(band({ type: PeqType.blocking, slope: 5 }), 250)).toBeLessThan(-70);
  });

  it('treats bands 2 to 4 as peaks whatever the type', () => {
    expect(peqBandDb(band({ band: 3, type: PeqType.blocking, gain: 6 }), 1_000)).toBeCloseTo(6, 3);
  });

  it('narrows Peaking 2 with gain', () => {
    expect(variableQ(2, 0)).toBeCloseTo(0.8);
    expect(variableQ(2, 12)).toBeCloseTo(2);
    expect(peqBandDb(band({ type: PeqType.peak2, gain: 6 }), 1_000)).toBeCloseTo(6, 3);
  });
});
