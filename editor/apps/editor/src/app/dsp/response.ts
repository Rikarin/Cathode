/** Filter shapes the editor draws (BiquadType in Cathode.Dsp). */
export type FilterShape = 'peak' | 'lowshelf' | 'highshelf' | 'lowpass' | 'highpass';

/**
 * Magnitude in dB of one RBJ-cookbook section at `frequency`. The engine's TPT state-variable filters have the same
 * analog prototypes, so the curves match what is heard (to within the bilinear warping both share).
 */
export function sectionDb(
  shape: FilterShape,
  centre: number,
  gainDb: number,
  q: number,
  frequency: number,
  sampleRate: number
): number {
  const w0 = (2 * Math.PI * Math.min(centre, sampleRate * 0.49)) / sampleRate;
  const cos = Math.cos(w0);
  const alpha = Math.sin(w0) / (2 * q);
  const a = Math.pow(10, gainDb / 40);
  let b: [number, number, number];
  let d: [number, number, number];
  switch (shape) {
    case 'peak':
      b = [1 + alpha * a, -2 * cos, 1 - alpha * a];
      d = [1 + alpha / a, -2 * cos, 1 - alpha / a];
      break;
    case 'lowshelf': {
      const s = 2 * Math.sqrt(a) * alpha;
      b = [a * (a + 1 - (a - 1) * cos + s), 2 * a * (a - 1 - (a + 1) * cos), a * (a + 1 - (a - 1) * cos - s)];
      d = [a + 1 + (a - 1) * cos + s, -2 * (a - 1 + (a + 1) * cos), a + 1 + (a - 1) * cos - s];
      break;
    }
    case 'highshelf': {
      const s = 2 * Math.sqrt(a) * alpha;
      b = [a * (a + 1 + (a - 1) * cos + s), -2 * a * (a - 1 + (a + 1) * cos), a * (a + 1 + (a - 1) * cos - s)];
      d = [a + 1 - (a - 1) * cos + s, 2 * (a - 1 - (a + 1) * cos), a + 1 - (a - 1) * cos - s];
      break;
    }
    case 'lowpass':
      b = [(1 - cos) / 2, 1 - cos, (1 - cos) / 2];
      d = [1 + alpha, -2 * cos, 1 - alpha];
      break;
    case 'highpass':
      b = [(1 + cos) / 2, -(1 + cos), (1 + cos) / 2];
      d = [1 + alpha, -2 * cos, 1 - alpha];
      break;
  }

  const w = (2 * Math.PI * frequency) / sampleRate;
  return 20 * Math.log10(polyMagnitude(b, w) / polyMagnitude(d, w));
}

function polyMagnitude([c0, c1, c2]: [number, number, number], w: number): number {
  const re = c0 + c1 * Math.cos(w) + c2 * Math.cos(2 * w);
  const im = -(c1 * Math.sin(w) + c2 * Math.sin(2 * w));
  return Math.hypot(re, im);
}

/** `count` frequencies spaced evenly on a log axis. */
export function logFrequencies(count: number, from = 20, to = 20_000): number[] {
  return Array.from({ length: count }, (_, i) => from * Math.pow(to / from, i / (count - 1)));
}

export interface DecodedWav {
  sampleRate: number;
  channels: Float32Array[];
}

/** Decodes a PCM (16/24/32-bit) or IEEE float (32-bit) WAV file, as impulse responses come. */
export function decodeWav(buffer: ArrayBuffer): DecodedWav {
  const view = new DataView(buffer);
  const tag = (offset: number) => String.fromCharCode(...new Uint8Array(buffer, offset, 4));
  if (tag(0) !== 'RIFF' || tag(8) !== 'WAVE') {
    throw new Error('Not a WAV file.');
  }

  let format = 0;
  let channelCount = 0;
  let sampleRate = 0;
  let bits = 0;
  for (let offset = 12; offset + 8 <= view.byteLength;) {
    const id = tag(offset);
    const size = view.getUint32(offset + 4, true);
    const body = offset + 8;
    if (id === 'fmt ') {
      format = view.getUint16(body, true);
      channelCount = view.getUint16(body + 2, true);
      sampleRate = view.getUint32(body + 4, true);
      bits = view.getUint16(body + 14, true);
      if (format === 0xfffe) {
        format = view.getUint16(body + 24, true); // WAVE_FORMAT_EXTENSIBLE: the sub-format GUID starts with it
      }
    } else if (id === 'data') {
      const bytes = bits / 8;
      const frames = Math.floor(Math.min(size, view.byteLength - body) / (bytes * channelCount));
      const channels = Array.from({ length: channelCount }, () => new Float32Array(frames));
      for (let frame = 0; frame < frames; frame++) {
        for (let c = 0; c < channelCount; c++) {
          channels[c][frame] = readSample(view, body + (frame * channelCount + c) * bytes, format, bits);
        }
      }

      return { sampleRate, channels };
    }

    offset = body + size + (size % 2);
  }

  throw new Error('The WAV file has no audio data.');
}

function readSample(view: DataView, at: number, format: number, bits: number): number {
  if (format === 3 && bits === 32) {
    return view.getFloat32(at, true);
  }

  if (format !== 1) {
    throw new Error(`Unsupported WAV encoding (format ${format}, ${bits} bit).`);
  }

  switch (bits) {
    case 16:
      return view.getInt16(at, true) / 32_768;
    case 24:
      return ((view.getUint8(at) | (view.getUint8(at + 1) << 8) | (view.getInt8(at + 2) << 16)) as number) / 8_388_608;
    case 32:
      return view.getInt32(at, true) / 2_147_483_648;
    default:
      throw new Error(`Unsupported WAV bit depth ${bits}.`);
  }
}

/** Magnitude in dB of an impulse response at each frequency (a direct DTFT: a few hundred points need no FFT). */
export function impulseResponseDb(samples: Float32Array, sampleRate: number, frequencies: readonly number[]): number[] {
  return frequencies.map(f => {
    const w = (2 * Math.PI * f) / sampleRate;
    let re = 0;
    let im = 0;
    for (let n = 0; n < samples.length; n++) {
      re += samples[n] * Math.cos(w * n);
      im -= samples[n] * Math.sin(w * n);
    }

    return 20 * Math.log10(Math.max(Math.hypot(re, im), 1e-9));
  });
}
