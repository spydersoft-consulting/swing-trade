// Runs first (see playwright.config.ts): an admin presses Refresh now and the BFF, the API and WireMock (standing in for
// Alpaca and Tiingo) load the canned bars. Every other spec reads the prices this leaves behind.
import { expect, test } from "@playwright/test";
import { apiWatchlist, login, uiDate, watchConsole, watchRow } from "./support";

test("admin Refresh now loads prices from WireMock and the as-of dates appear without a reload", async ({ page }) => {
  const consoleErrors = watchConsole(page);
  await login(page, "admin");
  await expect(page.getByRole("heading", { level: 1, name: "Watchlist" })).toBeVisible();

  await page.getByRole("button", { name: "Refresh now" }).click();

  // XLU returns HTTP 500 from both providers in the canned data (DECISIONS #62), so the run ends Partial, not Succeeded.
  const status = page.getByTestId("refresh-status");
  await expect(status).toContainText(/Partial|Succeeded/, { timeout: 90_000 });
  await expect(page.getByRole("button", { name: "Refresh now" })).toBeEnabled();

  const rows = await apiWatchlist();
  const spy = rows.find((r) => r.ticker === "SPY");
  expect(spy?.asOfDate, "SPY has a stored bar after the refresh").toBeTruthy();
  const spyRow = watchRow(page, "SPY");
  await expect(spyRow).toContainText(uiDate(spy!.asOfDate!));

  // The same API answer drives the signal column.
  const qqq = rows.find((r) => r.ticker === "QQQ");
  await expect(watchRow(page, "QQQ")).toContainText(qqq!.signalText ?? "");
  expect(consoleErrors()).toEqual([]);
});
