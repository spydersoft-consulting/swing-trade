import { defineConfig, devices } from "@playwright/test";
import path from "node:path";

const appHostProject = path.resolve(import.meta.dirname, "../../../Spydersoft.SwingTrade.AppHost");
// The BFF (Spydersoft.SwingTrade.Frontend) on its plain-http port. The mock OIDC server and the BFF are both http,
// so the AppHost sets OidcProxySettings__AlwaysRedirectToHttps=false for the mock.
const baseUrl = process.env.SWINGTRADE_WEB_BASE_URL ?? "http://localhost:8190";

export default defineConfig({
  testDir: "./tests",
  // One shared database and one shared mock IdP: specs run one at a time, in a fixed order, each cleaning up after itself.
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: [["list"], ["html", { open: "never" }]],
  use: {
    baseURL: baseUrl,
    trace: "retain-on-failure",
  },
  // "refresh" drives the admin Refresh now button against WireMock first; the rest read the prices it loaded.
  projects: [
    { name: "refresh", testMatch: /refresh\.spec\.ts/, use: { ...devices["Desktop Chrome"] } },
    {
      name: "chromium",
      testMatch: /.*\.spec\.ts/,
      testIgnore: /refresh\.spec\.ts/,
      dependencies: ["refresh"],
      use: { ...devices["Desktop Chrome"] },
    },
  ],
  // Boots the whole stack: Postgres, the mock OIDC server, WireMock standing in for Alpaca and Tiingo, the API and the BFF.
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
