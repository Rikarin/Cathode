import { PeqType, peqBandDb, peqGainFor, peqTotalDb, variableQ } from './peq';

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

describe('peqGainFor', () => {
  const eq = () => [
    band({ band: 1, type: PeqType.shelf, frequency: 80 }),
    band({ band: 2, type: PeqType.peak, frequency: 400 }),
    band({ band: 3, type: PeqType.peak, frequency: 1_000, gain: 12, q: 0.7 }),
    band({ band: 4, type: PeqType.peak, frequency: 3_000 }),
    band({ band: 5, type: PeqType.blocking, frequency: 8_000 })
  ];

  it('finds the gain that puts the whole curve where a handle is dropped, over the other bands', () => {
    const bands = eq();
    const gain = peqGainFor(bands, 2, 600, 3)!;
    const moved = bands.map(b => (b.band === 2 ? { ...b, frequency: 600, gain } : b));
    expect(peqTotalDb(moved, 600)).toBeCloseTo(3, 3);
    expect(gain).toBeLessThan(3); // band 3's boost already lifts the curve at 600 Hz
  });

  it('counts a shelf as half its gain at its frequency', () => {
    const bands = eq();
    const gain = peqGainFor(bands, 1, 80, 6)!;
    const moved = bands.map(b => (b.band === 1 ? { ...b, gain } : b));
    expect(peqTotalDb(moved, 80)).toBeCloseTo(6, 3);
  });

  it('leaves a cut band without a gain to set', () => {
    expect(peqGainFor(eq(), 5, 8_000, 6)).toBeNull();
  });
});
