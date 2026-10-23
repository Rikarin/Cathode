import type { Meta, StoryObj } from '@storybook/angular-vite';
import { CthRta, type RtaSpectrum } from './rta';

const Centers = Array.from({ length: 61 }, (_, i) => 1_000 * 2 ** ((i - 34) / 6)).filter(f => f >= 20 && f <= 20_000);

/** A guitar-like spectrum: a broad mid hump rolling off above 5 kHz, gently moving. */
function guitar(): () => RtaSpectrum {
  const start = performance.now();
  return () => {
    const t = (performance.now() - start) / 1000;
    const levels = Centers.map(f => {
      const octaves = Math.log2(f / 700);
      return -18 - 6 * octaves * octaves - (f > 5_000 ? 24 * Math.log2(f / 5_000) : 0) + 3 * Math.sin(t * 3 + f);
    });
    return { frequencies: Centers, levels };
  };
}

/** The output spectrum in sixth-octave bands with a falling peak hold. */
const meta: Meta<CthRta> = {
  title: 'Audio UI/RTA',
  component: CthRta,
  render: args => ({ props: args, template: `<cth-rta class="h-48 w-[480px]" [source]="source" />` })
};

export default meta;
type Story = StoryObj<CthRta>;

export const Guitar: Story = { args: { source: guitar() } };

export const NoSignal: Story = { args: { source: () => null } };
