/**
 * Preset Leveling: measure how loud a preset plays and work out the level change that brings it to a target. The
 * engine meters the output with ITU-R BS.1770 (momentary loudness, 400 ms windows); this gates and averages those
 * readings the way BS.1770 integrates a programme, so pauses between phrases do not drag the result down.
 */

/** Readings below this are silence, not playing (BS.1770's absolute gate). */
export const AbsoluteGateLufs = -70;

/** Readings more than this below the ungated average are pauses and decays (BS.1770's relative gate). */
export const RelativeGateLu = 10;

const toPower = (lufs: number) => 10 ** ((lufs + 0.691) / 10);
const toLufs = (power: number) => -0.691 + 10 * Math.log10(power);

/** Accumulates momentary loudness readings and gives their gated (integrated) loudness. */
export class LoudnessIntegrator {
  private readonly readings: number[] = [];

  get count(): number {
    return this.readings.length;
  }

  add(momentaryLufs: number | undefined): void {
    if (momentaryLufs !== undefined && Number.isFinite(momentaryLufs) && momentaryLufs > AbsoluteGateLufs) {
      this.readings.push(momentaryLufs);
    }
  }

  reset(): void {
    this.readings.length = 0;
  }

  /** The gated loudness of everything played so far, LUFS, or null before anything was heard. */
  integrated(): number | null {
    if (this.readings.length === 0) {
      return null;
    }

    const powers = this.readings.map(toPower);
    const gate = toLufs(powers.reduce((a, b) => a + b, 0) / powers.length) - RelativeGateLu;
    const kept = powers.filter(p => toLufs(p) > gate);
    return toLufs(kept.reduce((a, b) => a + b, 0) / kept.length);
  }
}

/** The block whose level the tool changes: an amp's Level if the preset has one, else the Output's Main Level. */
export interface LevelTarget {
  block: string;
  param: string;
  label: string;
}

export const AmpTypes = ['circuitamp', 'nam'];

export function levelTarget(
  blocks: readonly { id: string; type: string }[],
  hasLevel: (type: string) => boolean,
  name: (type: string) => string = type => type
): LevelTarget | null {
  const amp = blocks.find(b => AmpTypes.includes(b.type) && hasLevel(b.type));
  if (amp) {
    return { block: amp.id, param: 'level', label: `${name(amp.type)} (${amp.id}) Level` };
  }

  const output = blocks.find(b => b.type === 'output' && hasLevel(b.type));
  return output ? { block: output.id, param: 'level', label: `Output (${output.id}) Main Level` } : null;
}

export interface LevelSuggestion {
  /** The change in dB, after clamping to the parameter's range. */
  change: number;
  /** The new level. */
  next: number;
  /** Whether the range stopped it short of the target. */
  clamped: boolean;
}

/** How to reach <code>target</code> LUFS from <code>measured</code>, given the level now and its range. */
export function suggestLevel(
  measured: number,
  target: number,
  current: number,
  min: number,
  max: number
): LevelSuggestion {
  const wanted = current + (target - measured);
  const next = Math.round(Math.min(max, Math.max(min, wanted)) * 10) / 10;
  return { change: Math.round((next - current) * 10) / 10, next, clamped: Math.abs(next - wanted) > 0.05 };
}
