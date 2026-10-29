import type { Meta, StoryObj } from '@storybook/angular-vite';
import { CthReductionMeter } from './reduction-meter';

/** A compressor on a played guitar: each pick attack pulls the level down, and it recovers as the note decays. */
function compressing(depth: number): () => number {
  const start = performance.now();
  return () => {
    const note = ((performance.now() - start) / 1000) % 0.6;
    return depth * Math.min(1, note / 0.005) * Math.exp(-note * 5);
  };
}

/**
 * Gain reduction read from telemetry on animation frames: the bar grows down from 0 dB, the mark holds the deepest
 * reduction for a second, and the readout shows it.
 */
const meta: Meta<CthReductionMeter> = {
  title: 'Audio UI/Reduction Meter',
  component: CthReductionMeter,
  render: args => ({
    props: args,
    template: `<cth-reduction-meter class="h-32" [source]="source" [label]="label" [range]="range" />`
  })
};

export default meta;
type Story = StoryObj<CthReductionMeter>;

export const Compressor: Story = { args: { label: 'Gain reduction', range: 24, source: compressing(12) } };

export const Resting: Story = { args: { label: 'Gain reduction', range: 24, source: () => 0 } };

/** A gate closes by tens of dB, so its scale is deeper. */
export const Gate: Story = { args: { label: 'Gate gain reduction', range: 60, source: compressing(55) } };
