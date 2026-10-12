// @ts-check
import eslint from "@eslint/js";
import tseslint from "typescript-eslint";
import eslintPluginPrettierRecommended from "eslint-plugin-prettier/recommended";

// Shared base rules for every TypeScript package in this monorepo (the pitstop-ui SPA and the
// standalone Playwright integration-test packages). Each consumer supplies its own
// `tsconfigRootDir` (always its own directory -- see https://tseslint.com/parser-tsconfigrootdir,
// this can't be inferred automatically once more than one tsconfig-rooted project is linted in the
// same run) plus its own `files`/`ignores` glob.
export function sharedTypeScriptConfig({ tsconfigRootDir, files, ignores = [] }) {
  return tseslint.config(
    {
      files,
      ignores,
      languageOptions: {
        parserOptions: { tsconfigRootDir },
      },
      extends: [eslint.configs.recommended, ...tseslint.configs.recommended, ...tseslint.configs.stylistic],
    },
    eslintPluginPrettierRecommended,
  );
}
