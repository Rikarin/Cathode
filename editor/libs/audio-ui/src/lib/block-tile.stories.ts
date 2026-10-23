import type { Meta, StoryObj } from '@storybook/angular-vite';
import { BlockTypeIcons } from './block-icons';
import { BlockCategories, CthBlockTile } from './block-tile';

/**
 * A block as a coloured tile, as the grid and the block picker show it: the category picks the gradient, the type its icon.
 * Unknown categories get a stable colour from their name.
 */
const meta: Meta<CthBlockTile> = {
    title: 'Audio UI/Block tile',
    component: CthBlockTile,
    render: args => ({
        props: args,
        template: `<cth-block-tile
            class="h-[60px] w-[84px]"
            [name]="name"
            [category]="category"
            [typeId]="typeId"
            [bypassed]="bypassed"
        />`
    })
};

export default meta;
type Story = StoryObj<CthBlockTile>;

export const Drive: Story = { args: { name: 'Drive', category: 'Drive', typeId: 'drive', bypassed: false } };

export const Bypassed: Story = { args: { name: 'Delay', category: 'Delay', typeId: 'delay', bypassed: true } };

export const NewCategory: Story = { args: { name: 'Freeze', category: 'Spectral', typeId: 'freeze', bypassed: false } };

export const AllCategories: Story = {
    render: () => ({
        props: { categories: [...Object.keys(BlockCategories), 'Spectral'] },
        template: `
            <div class="flex flex-wrap gap-3">
                @for (category of categories; track category) {
                    <cth-block-tile class="h-[60px] w-[84px]" [name]="category" [category]="category" />
                }
            </div>
        `
    })
};

/** Every built-in type's own icon, at the block picker's size. */
export const AllTypes: Story = {
    render: () => ({
        props: { types: Object.keys(BlockTypeIcons) },
        template: `
            <div class="flex flex-wrap gap-3">
                @for (type of types; track type) {
                    <cth-block-tile class="h-24 w-30" size="lg" [name]="type" [typeId]="type" category="Utility" />
                }
            </div>
        `
    })
};
