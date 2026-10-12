// @ts-check
import { sharedTypeScriptConfig } from "../../../../eslint.shared.mjs";

export default sharedTypeScriptConfig({
  tsconfigRootDir: import.meta.dirname,
  files: ["**/*.ts"],
  ignores: ["**/playwright-report/**/*", "**/test-results/**/*"],
});
