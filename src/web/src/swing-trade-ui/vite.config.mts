import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

// The BFF (Spydersoft.SwingTrade.Frontend) owns sign-in and forwards /api/v1 with the user's access token. In dev the
// Vite server proxies those paths to it (the AppHost sets BFF_URL; 8190 is the BFF's fixed local port).
const bff = process.env.BFF_URL ?? "http://localhost:8190";
const proxied = Object.fromEntries(
  ["^/api/", "^/oauth2", "^/livez", "^/readyz", "^/config.js"].map((path) => [
    path,
    { target: bff, changeOrigin: false, secure: false },
  ]),
);

export default defineConfig({
  plugins: [react()],
  optimizeDeps: {
    include: ["chart.js/auto"],
  },
  build: {
    chunkSizeWarningLimit: 1200,
  },
  server: {
    proxy: proxied,
    port: parseInt(process.env.PORT ?? "5290"),
  },
  test: {
    globals: true,
    environment: "jsdom",
    setupFiles: "./tests/setup.js",
    reporters: ["default", "junit"],
    outputFile: { junit: "./output/test/junit.xml" },
    coverage: {
      provider: "v8",
      reporter: ["html", "cobertura", "lcov", "text-summary"],
      reportsDirectory: "./output/coverage",
      include: ["src/**/*.{ts,tsx}"],
      exclude: [
        "**/node_modules/**",
        "**/tests/**",
        "**/dist/**",
        "**/output/**",
        "**/*.test.{ts,tsx}",
        "**/src/api/generated/**",
        "**/src/main.tsx",
        "**/src/test/**",
        "**/*.d.ts",
      ],
    },
  },
});
