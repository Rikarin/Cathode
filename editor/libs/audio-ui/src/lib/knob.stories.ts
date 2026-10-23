import type { Meta, StoryObj } from '@storybook/angular-vite';
import { CthKnob } from './knob';

/**
 * A rotary control. Drag vertically (⇧ for fine), scroll, use the arrow and Page keys, double-click to type a value
 * with its unit, ⌥-click to reset to the default.
 */
const meta: Meta<CthKnob> = {
    title: 'Audio UI/Knob',
    component: CthKnob,
    args: { label: 'Drive', unit: '', min: 0, max: 10, value: 5, defaultValue: 5, size: 'md' }
};

export default meta;
type Story = StoryObj<CthKnob>;

export const Default: Story = {};

/** A bipolar range fills from the centre. */
export const Bipolar: Story = { args: { label: 'Bass', unit: 'dB', min: -12, max: 12, value: 4.5, defaultValue: 0 } };

/** Frequency on a log taper: the middle of the travel is the geometric mean (632 Hz for 20 Hz–20 kHz). */
export const LogFrequency: Story = {
    args: { label: 'Frequency', unit: 'Hz', min: 20, max: 20_000, value: 632, defaultValue: 1_000, taper: 'log' }
};

export const Sizes: Story = {
    render: args => ({
        props: args,
        template: `
      <div class="flex items-end gap-6">
        <cth-knob label="Small" size="sm" [value]="3" [min]="0" [max]="10" />
        <cth-knob label="Medium" size="md" [value]="5" [min]="0" [max]="10" />
        <cth-knob label="Large" size="lg" [value]="7" [min]="0" [max]="10" />
      </div>
    `
    })
};

export const Disabled: Story = { args: { disabled: true } };
