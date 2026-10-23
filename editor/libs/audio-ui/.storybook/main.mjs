import tailwindcss from '@tailwindcss/vite';
import { dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import tsconfigPaths from 'vite-tsconfig-paths';

// Set up as xUI's own Storybook (apps/ui-storybook in the xUI repo): Angular through Vite, Tailwind through its Vite
// plugin, workspace paths resolved from tsconfig.base.json.
const config = {
    stories: ['../src/**/*.stories.ts'],
    addons: [
        getAbsolutePath('@storybook/addon-docs'),
        getAbsolutePath('@storybook/addon-a11y'),
        getAbsolutePath('@storybook/addon-themes'),
        getAbsolutePath('@chromatic-com/storybook'),
        getAbsolutePath('@storybook/addon-vitest')
    ],
    framework: { name: getAbsolutePath('@storybook/angular-vite'), options: { compodoc: false } },
    viteFinal: async config => {
        config.plugins = [
            ...(config.plugins ?? []),
            tailwindcss(),
            tsconfigPaths({ projects: [fileURLToPath(import.meta.resolve('../../../tsconfig.base.json'))] })
        ];
        return config;
    }
};

export default config;

function getAbsolutePath(value) {
    return dirname(fileURLToPath(import.meta.resolve(`${value}/package.json`)));
}
