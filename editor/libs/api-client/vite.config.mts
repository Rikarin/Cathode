/// <reference types='vitest' />
import { defineConfig } from 'vite';

export default defineConfig(() => ({
  root: import.meta.dirname,
  cacheDir: '../../node_modules/.vite/libs/api-client',
  test: {
    name: 'api-client',
    watch: false,
    environment: 'node',
    globals: true,
    include: ['src/**/*.spec.ts'],
    reporters: ['default']
  }
}));
