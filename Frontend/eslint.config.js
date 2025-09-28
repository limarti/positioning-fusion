import { defineConfig, globalIgnores } from 'eslint/config';
import globals from 'globals';
import js from '@eslint/js';
import pluginVue from 'eslint-plugin-vue';
import pluginVitest from '@vitest/eslint-plugin';
import stylistic from '@stylistic/eslint-plugin';

export default defineConfig([
  {
    name: 'app/files-to-lint',
    files: ['**/*.{js,mjs,jsx,vue}'],
  },

  globalIgnores(['**/dist/**', '**/dist-ssr/**', '**/coverage/**']),

  {
    ...pluginVitest.configs.recommended,
    files: ['src/**/__tests__/*'],
  },

  ...pluginVue.configs['flat/recommended'],

  {
    files: ['**/*.{js,mjs,jsx}'],
    languageOptions: {
      globals: {
        ...globals.browser,
      },
    },
    plugins: {
      '@stylistic': stylistic,
    },
    rules: {
      "semi": [2, "always"],
      "no-multiple-empty-lines": ["warn", { "max": 1 }],
      "brace-style": ["error", "allman", { "allowSingleLine": true }],
      "arrow-spacing": ["error"],
      "keyword-spacing": ["error", { "before": true, "after": true }],
      "object-curly-spacing": ["error", "always"],
      "@stylistic/indent": ["error", 2],
    },
  },

  {
    files: ["**/*.vue"],
    languageOptions: {
      globals: {
        ...globals.browser,
      },
    },
    plugins: {
      '@stylistic': stylistic,
    },
    rules: {
      "semi": [2, "always"],
      "no-multiple-empty-lines": ["warn", { "max": 1 }],
      "brace-style": ["error", "allman", { "allowSingleLine": true }],
      "arrow-spacing": ["error"],
      "keyword-spacing": ["error", { "before": true, "after": true }],
      "object-curly-spacing": ["error", "always"],
      "vue/html-closing-bracket-newline": ["error", {
        "singleline": "never",
        "multiline": "never"
      }],
      "vue/first-attribute-linebreak": ["error", {
        "singleline": "beside",
        "multiline": "beside"
      }],
      "vue/max-attributes-per-line": ["error", {
        "singleline": 1000,
        "multiline": 1
      }],
      "vue/attribute-hyphenation": ["error", "never", {
        "ignore": []
      }],
      "vue/v-on-event-hyphenation": ["error", "never", {
        "ignore": []
      }],
      "vue/script-indent": ["error", 2, {
        "baseIndent": 1,
      }],
      "vue/html-indent": ["error", 2, {
        "baseIndent": 1,
      }],
      "vue/multiline-html-element-content-newline": ["error", {
        "ignoreWhenEmpty": true,
        "ignores": ["VueComponent", "pre"],
        "allowEmptyLines": true
      }],
      'vue/multi-word-component-names': 'off',
      'vue/block-order': ['error', {
        'order': ['template', 'script', 'style']
      }]
    },
  }
]);
