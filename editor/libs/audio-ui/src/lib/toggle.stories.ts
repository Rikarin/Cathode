import { type Meta, type StoryObj, moduleMetadata } from '@storybook/angular-vite';
import { CthKnob } from './knob';
import { CthToggle } from './toggle';

/** A vertical toggle for a row of knobs. Click or Space/Enter flips it; ↑ turns it on, ↓ off. */
const meta: Meta<CthToggle> = {
  title: 'Audio UI/Toggle',
  component: CthToggle,
  args: { label: 'Bright', checked: true, size: 'md' }
};

export default meta;
type Story = StoryObj<CthToggle>;

export const On: Story = {};

export const Off: Story = { args: { checked: false } };

/** Beside knobs of the same size, in a block's category colour. */
export const WithKnobs: Story = {
  decorators: [moduleMetadata({ imports: [CthKnob] })],
  render: args => ({
    props: args,
    template: `
      <div class="flex items-start gap-6">
        <cth-knob label="Gain" [value]="6" [min]="0" [max]="10" color="oklch(0.72 0.17 30)" />
        <cth-toggle label="Bright" [checked]="true" color="oklch(0.72 0.17 30)" />
        <cth-toggle label="Fat" [checked]="false" color="oklch(0.72 0.17 30)" />
        <cth-knob label="Small" size="sm" [value]="3" [min]="0" [max]="10" />
        <cth-toggle label="Small" size="sm" [checked]="true" />
      </div>
    `
  })
};

export const Disabled: Story = { args: { disabled: true } };
