// @ts-check
import tseslint from "typescript-eslint";
import reactHooks from "eslint-plugin-react-hooks";
import reactRefresh from "eslint-plugin-react-refresh";
import { sharedTypeScriptConfig } from "../../../../eslint.shared.mjs";

export default tseslint.config(
  {
    // A bare `ignores` entry (no `files`/other keys) is a *global* ignore -- it excludes these
    // paths even when explicitly passed on the CLI (e.g. by lint-staged), unlike an `ignores` field
    // nested inside a rule block, which only scopes that one block. Codegen output shouldn't be
    // held to hand-written lint rules at all.
    ignores: ["**/build/**/*", "**/dist/**/*", "**/output/**/*", "**/coverage/**/*", "src/api/generated/**/*"],
  },
  ...sharedTypeScriptConfig({
    tsconfigRootDir: import.meta.dirname,
    files: ["**/*.ts", "**/*.tsx"],
  }),
  {
    files: ["**/*.ts", "**/*.tsx"],
    plugins: {
      "react-hooks": reactHooks,
      "react-refresh": reactRefresh,
    },
    rules: {
      "react-hooks/rules-of-hooks": "error",
      "react-hooks/exhaustive-deps": "warn",
      "react-refresh/only-export-components": ["warn", { allowConstantExport: true }],
      "@typescript-eslint/no-inferrable-types": "off",
    },
  },
  {
    files: ["**/context/**/*.ts", "**/context/**/*.tsx"],
    rules: {
      "react-refresh/only-export-components": "off",
    },
  },
);
