import type { Meta, StoryObj } from '@storybook/angular-vite';
import { CthScope, type ScopeTrace } from './scope';

/** A decaying 110 Hz note with some second harmonic, as 12 kHz scope frames. */
function note(): () => ScopeTrace {
    const start = performance.now();
    return () => {
        const t0 = (performance.now() - start) / 1000;
        const envelope = Math.exp(-(t0 % 2) * 1.5);
        const samples = Array.from({ length: 512 }, (_, i) => {
            const t = t0 + i / 12_000;
            return envelope * (0.7 * Math.sin(2 * Math.PI * 110 * t) + 0.25 * Math.sin(2 * Math.PI * 220 * t));
        });
        return { sampleRate: 12_000, samples };
    };
}

/** The engine's output waveform; a rising-zero-crossing trigger holds a steady note still. */
const meta: Meta<CthScope> = {
    title: 'Audio UI/Scope',
    component: CthScope,
    render: args => ({ props: args, template: `<cth-scope class="h-48 w-[480px]" [source]="source" />` })
};

export default meta;
type Story = StoryObj<CthScope>;

export const Note: Story = { args: { source: note() } };

export const NoSignal: Story = { args: { source: () => null } };
