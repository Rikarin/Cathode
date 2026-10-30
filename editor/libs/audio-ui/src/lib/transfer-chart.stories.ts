import type { Meta, StoryObj } from '@storybook/angular-vite';
import { CthTransferChart, type TransferPoint } from './transfer-chart';

/** A soft-knee compressor's static curve: unity under the knee, `ratio` over it, lifted by `makeup` (all dB). */
function compressor(threshold: number, ratio: number, knee: number, makeup = 0): (input: number) => number {
  return input => {
    const over = input - threshold;
    if (2 * over < -knee) {
      return input + makeup;
    }

    if (knee > 0 && 2 * Math.abs(over) <= knee) {
      const t = over + knee / 2;
      return input + ((1 / ratio - 1) * t * t) / (2 * knee) + makeup;
    }

    return threshold + over / ratio + makeup;
  };
}

/** A played guitar through `curve`: each pick attack jumps up, then the note decays; the output follows the curve. */
function playing(curve: (input: number) => number): () => TransferPoint {
  const start = performance.now();
  return () => {
    const note = ((performance.now() - start) / 1000) % 1.2;
    const input = -6 - 40 * (note / 1.2);
    return { input, output: curve(input) };
  };
}

const studio = compressor(-24, 4, 6, 6);

/**
 * A compressor's transfer curve: output against input level, the 1:1 diagonal for reference, and a dot that rides
 * the curve where the signal is, eased between telemetry frames (it jumps under prefers-reduced-motion).
 */
const meta: Meta<CthTransferChart> = {
  title: 'Audio UI/Transfer Chart',
  component: CthTransferChart,
  render: args => ({
    props: args,
    template: `<cth-transfer-chart [curve]="curve" [source]="source" [label]="label" [size]="size" [accent]="accent" />`
  })
};

export default meta;
type Story = StoryObj<CthTransferChart>;

export const Compressor: Story = {
  args: {
    curve: studio,
    source: playing(studio),
    label: 'Compressor transfer curve',
    size: 160,
    accent: 'oklch(0.72 0.13 150)'
  }
};

/** With nothing playing the dot hides and the curve stands alone. */
export const Resting: Story = {
  args: { curve: studio, source: () => null, label: 'Compressor transfer curve', size: 160 }
};

/** ∞:1 with a hard knee: a limiter's flat top. */
export const Limiter: Story = {
  args: {
    curve: compressor(-18, 1e9, 0),
    source: playing(compressor(-18, 1e9, 0)),
    label: 'Limiter transfer curve',
    size: 160,
    accent: 'oklch(0.72 0.13 150)'
  }
};
