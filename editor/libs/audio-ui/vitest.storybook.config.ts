import { storybookTest } from '@storybook/addon-vitest/vitest-plugin';
import { storybookAngularVitest } from '@storybook/angular-vite/vitest';
import { playwright } from '@vitest/browser-playwright';
import path from 'node:path';
import { defineConfig } from 'vitest/config';

// Every story renders in a real browser and must pass its axe checks (preview.js: a11y.test = 'error').
export default defineConfig({
    test: {
        projects: [
            {
                extends: true,
                root: import.meta.dirname,
                plugins: [
                    storybookAngularVitest({}),
                    storybookTest({ configDir: path.join(import.meta.dirname, '.storybook') })
                ],
                test: {
                    name: 'audio-ui-storybook',
                    browser: {
                        enabled: true,
                        headless: true,
                        provider: playwright({}),
                        instances: [{ browser: 'chromium' }]
                    }
                }
            }
        ]
    }
});
