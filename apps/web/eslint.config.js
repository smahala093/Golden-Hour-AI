import js from '@eslint/js';
import globals from 'globals';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  { ignores: ['dist', 'dev-dist', 'coverage'] },
  {
    extends: [js.configs.recommended, ...tseslint.configs.recommendedTypeChecked],
    files: ['**/*.{ts,tsx}'],
    languageOptions: {
      ecmaVersion: 2022,
      globals: globals.browser,
      // Let TypeScript resolve the nearest project for each file. This avoids
      // coupling typed linting to either a physical OneDrive path or the
      // workspace mirror path used by sandboxed/CI runners.
      parserOptions: { projectService: true },
    },
    plugins: { 'react-hooks': reactHooks, 'react-refresh': reactRefresh },
    rules: {
      ...reactHooks.configs.recommended.rules,
      'react-refresh/only-export-components': ['warn', { allowConstantExport: true }],
      '@typescript-eslint/consistent-type-imports': 'error',
    },
  },
  {
    files: ['src/**/*.tsx'],
    rules: {
      // Hook-returned callbacks and React state setters are already lexical functions.
      '@typescript-eslint/unbound-method': 'off',
    },
  },
  {
    files: ['src/state.tsx', 'src/components/AppShell.tsx', 'src/pages/EmergencyFlowPages.tsx'],
    rules: {
      // These modules intentionally colocate a provider/component with its matching hook or pure loader.
      'react-refresh/only-export-components': 'off',
    },
  },
);
