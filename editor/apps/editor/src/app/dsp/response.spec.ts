import { decodeWav, impulseResponseDb, logFrequencies, sectionDb } from './response';

describe('response', () => {
  it('draws peaks, shelves and cuts with the right magnitudes', () => {
    expect(sectionDb('peak', 1_000, 6, 1, 1_000, 48_000)).toBeCloseTo(6, 3);
    expect(sectionDb('peak', 1_000, 6, 1, 20, 48_000)).toBeCloseTo(0, 1);
    expect(sectionDb('lowshelf', 200, -9, 0.7071, 20, 48_000)).toBeCloseTo(-9, 0);
    expect(sectionDb('highshelf', 4_000, 5, 0.7071, 18_000, 48_000)).toBeCloseTo(5, 0);
    expect(sectionDb('highpass', 100, 0, 0.7071, 100, 48_000)).toBeCloseTo(-3.01, 1);
    expect(sectionDb('lowpass', 5_000, 0, 0.7071, 50, 48_000)).toBeCloseTo(0, 2);
  });

  it('spaces frequencies on a log axis', () => {
    const f = logFrequencies(4, 10, 10_000);
    expect(f.map(Math.round)).toEqual([10, 100, 1_000, 10_000]);
  });

  it('decodes 16-bit PCM and float WAV files', () => {
    expect(decodeWav(wav(1, 16, [0.5, -0.25])).channels[0][0]).toBeCloseTo(0.5, 4);
    const float = decodeWav(wav(3, 32, [0.125, -1]));
    expect(float.sampleRate).toBe(48_000);
    expect(Array.from(float.channels[0])).toEqual([0.125, -1]);
    expect(() => decodeWav(new ArrayBuffer(16))).toThrow('Not a WAV file');
  });

  it('measures an impulse response: a unit impulse is flat at 0 dB', () => {
    const impulse = new Float32Array(64);
    impulse[0] = 1;
    for (const db of impulseResponseDb(impulse, 48_000, [100, 1_000, 10_000])) {
      expect(db).toBeCloseTo(0, 6);
    }
  });
});

/** A mono 48 kHz WAV: format 1 (PCM) or 3 (float). */
function wav(format: number, bits: number, samples: number[]): ArrayBuffer {
  const bytes = bits / 8;
  const buffer = new ArrayBuffer(44 + samples.length * bytes);
  const view = new DataView(buffer);
  const text = (at: number, s: string) => [...s].forEach((c, i) => view.setUint8(at + i, c.charCodeAt(0)));
  text(0, 'RIFF');
  view.setUint32(4, 36 + samples.length * bytes, true);
  text(8, 'WAVE');
  text(12, 'fmt ');
  view.setUint32(16, 16, true);
  view.setUint16(20, format, true);
  view.setUint16(22, 1, true);
  view.setUint32(24, 48_000, true);
  view.setUint32(28, 48_000 * bytes, true);
  view.setUint16(32, bytes, true);
  view.setUint16(34, bits, true);
  text(36, 'data');
  view.setUint32(40, samples.length * bytes, true);
  samples.forEach((s, i) =>
    format === 3 ? view.setFloat32(44 + i * 4, s, true) : view.setInt16(44 + i * 2, Math.round(s * 32_767), true)
  );
  return buffer;
}
