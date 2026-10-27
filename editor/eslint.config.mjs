// One flat config for the whole workspace. @nx/eslint infers a `lint` target for every project under it
// (`pnpm exec nx run-many -t lint`), run with --max-warnings=0 (nx.json targetDefaults).
//
// TypeScript is linted with type information (typescript-eslint's project service picks each file's nearest
// tsconfig, following the solution-style references), so the strict type-checked rule sets apply everywhere.
// Formatting belongs to Prettier: eslint-config-prettier comes last and switches off every rule it would fight.
import js from '@eslint/js';
import angular from 'angular-eslint';
import prettier from 'eslint-config-prettier';
import { defineConfig, globalIgnores } from 'eslint/config';
import globals from 'globals';
import tseslint from 'typescript-eslint';

export default defineConfig(
  globalIgnores([
    '**/dist',
    '**/coverage',
    '**/.nx',
    '**/.angular',
    '**/node_modules',
    '**/storybook-static',
    'tmp',
    // openapi-typescript's output; its consumers (libs/api-client/src/lib, the editor) are linted.
    'libs/api-client/src/generated'
  ]),

  {
    linterOptions: { reportUnusedDisableDirectives: 'error', reportUnusedInlineConfigs: 'error' }
  },

  // Plain JavaScript: build scripts, Storybook config. Not part of any tsconfig, so no type information.
  {
    files: ['**/*.{js,mjs,cjs}'],
    extends: [js.configs.recommended],
    languageOptions: { globals: { ...globals.node } }
  },

  // TypeScript, type-aware.
  {
    files: ['**/*.{ts,mts,cts}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.strictTypeChecked,
      tseslint.configs.stylisticTypeChecked,
      angular.configs.tsRecommended
    ],
    languageOptions: {
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname
      }
    },
    processor: angular.processInlineTemplates,
    rules: {
      '@typescript-eslint/switch-exhaustiveness-check': [
        'error',
        { considerDefaultExhaustiveForUnions: true, requireDefaultForNonUnion: false }
      ],
      // A bare `void promise` is how a fire-and-forget call says so; the rule still flags every promise that is
      // neither awaited, returned, caught nor voided. Voided promises must handle their own errors (reviewed).
      '@typescript-eslint/no-floating-promises': ['error', { ignoreVoid: true }],
      '@typescript-eslint/no-misused-promises': 'error',
      '@typescript-eslint/no-non-null-assertion': 'error',
      // The stylistic set asks for `x!` over `x as T` when T only drops null; `!` is forbidden just above.
      '@typescript-eslint/non-nullable-type-assertion-style': 'off',
      // `() => this.close()` (Prettier's arrow style) is how handlers and callbacks are written throughout; a void
      // expression in any other position (a ternary, a `return` in a block, an operand) is still an error.
      '@typescript-eslint/no-confusing-void-expression': ['error', { ignoreArrowShorthand: true }],
      // Numbers and booleans read fine in messages; objects, nullish values and `any` still do not.
      // `name || 'Untitled'`: an empty string falls back too, on purpose (names, tokens, folders). `??` everywhere else.
      '@typescript-eslint/prefer-nullish-coalescing': ['error', { ignorePrimitives: { string: true } }],
      '@typescript-eslint/restrict-template-expressions': ['error', { allowNumber: true, allowBoolean: true }],
      '@typescript-eslint/consistent-type-imports': ['error', { fixStyle: 'inline-type-imports' }],
      '@typescript-eslint/no-unused-vars': [
        'error',
        { argsIgnorePattern: '^_', varsIgnorePattern: '^_', caughtErrors: 'all', caughtErrorsIgnorePattern: '^_' }
      ],
      eqeqeq: ['error', 'always', { null: 'ignore' }],
      'no-console': ['error', { allow: ['warn', 'error'] }],
      'prefer-const': 'error',

      // Angular: the codebase's own conventions, made rules.
      '@angular-eslint/component-selector': ['error', { type: 'element', prefix: 'cth', style: 'kebab-case' }],
      '@angular-eslint/directive-selector': ['error', { type: 'attribute', prefix: 'cth', style: 'camelCase' }],
      '@angular-eslint/prefer-on-push-component-change-detection': 'error',
      '@angular-eslint/prefer-signals': 'error',
      '@angular-eslint/prefer-output-emitter-ref': 'error',
      '@angular-eslint/prefer-host-metadata-property': 'error',
      '@angular-eslint/use-lifecycle-interface': 'error',
      '@angular-eslint/no-async-lifecycle-method': 'error',
      '@angular-eslint/no-lifecycle-call': 'error',
      '@angular-eslint/no-attribute-decorator': 'error',
      '@angular-eslint/no-implicit-take-until-destroyed': 'error',
      '@angular-eslint/no-uncalled-signals': 'error',
      '@angular-eslint/computed-must-return': 'error',
      '@angular-eslint/reactive-context-must-read-signal': 'error',
      '@angular-eslint/no-duplicates-in-metadata-arrays': 'error',
      '@angular-eslint/consistent-component-styles': 'error',
      '@angular-eslint/use-component-view-encapsulation': 'error'
    }
  },

  // Angular templates (all inline, extracted by the processor above).
  {
    files: ['**/*.html'],
    extends: [angular.configs.templateRecommended, angular.configs.templateAccessibility],
    rules: {
      '@angular-eslint/template/button-has-type': 'error',
      '@angular-eslint/template/no-any': 'error',
      '@angular-eslint/template/no-non-null-assertion': 'error',
      // `class="static"` next to `[class]="dynamic"` is Angular's documented way to merge the two.
      '@angular-eslint/template/no-duplicate-attributes': ['error', { allowStylePrecedenceDuplicates: true }],
      // xUI and Cathode's own form controls are controls a <label> can wrap, like <input>.
      '@angular-eslint/template/label-has-associated-control': [
        'error',
        {
          controlComponents: [
            'xui-select',
            'xui-switch',
            'xui-segmented-control',
            'xui-slider',
            'xui-numeric-input',
            'cth-knob',
            'cth-toggle',
            'cth-asset-picker'
          ]
        }
      ],
      '@angular-eslint/template/no-interpolation-in-attributes': 'error',
      '@angular-eslint/template/no-positive-tabindex': 'error',
      '@angular-eslint/template/prefer-at-else': 'error'
    }
  },

  // Electron main/preload and the desktop app's E2E run under Node.
  {
    files: ['apps/desktop/**/*.ts', '**/*.config.{ts,mts}', 'libs/audio-ui/.storybook/**'],
    languageOptions: { globals: { ...globals.node } }
  },

  // Unit tests (vitest, with globals) and E2E (Playwright).
  {
    files: ['**/*.spec.ts', '**/test-setup.ts', 'apps/desktop/e2e/**/*.ts'],
    languageOptions: { globals: { ...globals.vitest } },
    rules: {
      // Test hosts are tiny throwaway components; change detection strategy is irrelevant to them.
      '@angular-eslint/prefer-on-push-component-change-detection': 'off',
      // `expect(spy.method).toHaveBeenCalled()` passes vitest mock methods unbound; that is how the API works.
      '@typescript-eslint/unbound-method': 'off'
    }
  },

  prettier
);
