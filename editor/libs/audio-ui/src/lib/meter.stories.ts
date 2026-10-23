import type { Meta, StoryObj } from '@storybook/angular-vite';
import { CthMeter } from './meter';

/** A source that wanders like a played guitar: attacks every half second, decaying in between, with a rare clip. */
function guitar(seed: number): () => number {
  const start = performance.now() + seed * 120;
  return () => {
    const t = (performance.now() - start) / 1000;
    const note = t % 0.5;
    const accent = Math.floor(t / 0.5) % 7 === 3 ? 1.15 : 0.6;
    return accent * Math.exp(-note * 6) * (0.85 + 0.15 * Math.random());
  };
}

/**
 * A peak meter fed straight from telemetry on animation frames: instant attack, 24 dB/s fall, 1.5 s peak hold, and a
 * clip light that stays on until clicked.
 */
const meta: Meta<CthMeter> = {
  title: 'Audio UI/Meter',
  component: CthMeter,
  render: args => ({ props: args, template: `<cth-meter class="h-40" [sources]="sources" [label]="label" />` })
};

export default meta;
type Story = StoryObj<CthMeter>;

export const Mono: Story = { args: { label: 'Input level', sources: [guitar(0)] } };

export const Stereo: Story = { args: { label: 'Output level', sources: [guitar(0), guitar(1)] } };

export const Silence: Story = { args: { label: 'Input level', sources: [() => 0] } };
