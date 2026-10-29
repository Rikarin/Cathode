import type { Meta, StoryObj } from '@storybook/angular-vite';
import { CthTuner, type TunerReading } from './tuner';
import { findTuning, tuneTo } from './tuning';

/** A low E string settling into tune. */
function settling(): () => TunerReading {
  const start = performance.now();
  return () => {
    const t = (performance.now() - start) / 1000;
    const cents = 30 * Math.exp(-((t % 6) / 1.2)) * Math.cos(t * 2);
    return { note: 'E', octave: 2, cents, frequency: 82.41 * Math.pow(2, cents / 1200) };
  };
}

/** A tuner: a needle spanning ±50 cents or a strobe, green within ±2. */
const meta: Meta<CthTuner> = {
  title: 'Audio UI/Tuner',
  component: CthTuner
};

export default meta;
type Story = StoryObj<CthTuner>;

export const Settling: Story = { args: { source: settling() } };

export const InTune: Story = {
  args: { source: () => ({ note: 'A', octave: 4, cents: 0.4, frequency: 440.1 }) }
};

export const NoSignal: Story = { args: { source: () => null } };

/** The strobe: the marks drift right when sharp, left when flat, and stand still in tune. */
export const Strobe: Story = { args: { source: settling(), view: 'strobe' } };

/** Drop D's low string, targeted by `tuneTo`: the string's number shows under its note. */
export const DropDString: Story = {
  args: { source: () => tuneTo(73.1, 440, findTuning('dropD')) }
};
