import { defineConfig } from "@playwright/test";
import path from "node:path";

const appHostProject = path.resolve(import.meta.dirname, "../../../Spydersoft.SwingTrade.AppHost");
const baseUrl = process.env.SWINGTRADE_BASE_URL ?? "http://localhost:8180";

export default defineConfig({
  testDir: "./tests",
  // One shared database and one shared WireMock: specs run one at a time, in a fixed order, each cleaning up after itself.
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: [["list"], ["html", { open: "never" }]],
  use: {
    baseURL: baseUrl,
  },
  // "refresh" runs first and loads the canned bars; every other spec reads the data it left behind.
  projects: [
    { name: "refresh", testMatch: /refresh\.spec\.ts/ },
    { name: "api", testMatch: /.*\.spec\.ts/, testIgnore: /refresh\.spec\.ts/, dependencies: ["refresh"] },
  ],
  // Boots the whole stack: Postgres, the mock OIDC server, WireMock standing in for Alpaca and Tiingo, and the API.
  // Needs a container runtime (Docker, or set ASPIRE_CONTAINER_RUNTIME=podman).
  webServer: {
    command: `dotnet run --project "${appHostProject}" --launch-profile Testing`,
    url: `${baseUrl}/livez`,
    timeout: 300_000,
    reuseExistingServer: !process.env.CI,
    stdout: "pipe",
    stderr: "pipe",
  },
});
