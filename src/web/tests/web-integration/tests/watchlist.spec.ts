import { expect, test } from "@playwright/test";
import { apiAs, apiWatchlist, login, uiDate, watchConsole, watchRow } from "./support";

test("the table shows the API's rows, Pullback candidates first, with as-of dates and stale badges", async ({
  page,
}) => {
  const consoleErrors = watchConsole(page);
  const rows = await apiWatchlist();
  await login(page, "reader");

  const table = page.getByRole("table", { name: "Watchlist" }).or(page.locator("table").first());
  for (const header of [
    "Ticker",
    "Name",
    "As of",
    "Last",
    "20-day",
    "50-day",
    "% vs 20-day",
    "Trend",
    "Signal",
    "Re-entry date",
  ]) {
    await expect(page.getByRole("columnheader", { name: header, exact: true })).toBeVisible();
  }

  // The API's default order is Pullback candidates first, and the table keeps it.
  const firstTicker = await table.locator("tbody tr").first().getByRole("button").first().textContent();
  expect(firstTicker?.trim()).toBe(rows[0].ticker);
  await expect(table.locator("tbody tr").first()).toContainText("Pullback candidate");

  // Every row's as-of date is shown, and a stale badge appears exactly where the API says stale.
  for (const row of rows.filter((r) => r.asOfDate)) {
    await expect(watchRow(page, row.ticker)).toContainText(uiDate(row.asOfDate!));
  }
  const staleTickers = rows.filter((r) => r.stale).map((r) => r.ticker);
  await expect(page.getByTestId("stale-badge")).toHaveCount(staleTickers.length);
  if (staleTickers.length > 0) {
    await expect(page.getByTestId("stale-banner")).toContainText(String(staleTickers.length));
    await expect(watchRow(page, staleTickers[0]).getByTestId("stale-badge")).toBeVisible();
  }
  expect(consoleErrors()).toEqual([]);
});

test("signal chips filter the table and the sort headers reorder it", async ({ page }) => {
  const rows = await apiWatchlist();
  await login(page, "reader");

  const avoid = rows.filter((r) => r.signalText === "Avoid").map((r) => r.ticker);
  await page.getByRole("button", { name: /^Avoid/ }).click();
  await expect(page.getByRole("button", { name: /^Avoid/ })).toHaveAttribute("aria-pressed", "true");
  await expect(page.locator("tbody tr")).toHaveCount(avoid.length);
  for (const ticker of avoid) await expect(watchRow(page, ticker)).toBeVisible();

  await page.getByRole("button", { name: /^All/ }).click();
  await expect(page.locator("tbody tr")).toHaveCount(rows.length);

  await page.getByRole("columnheader", { name: "Ticker" }).click();
  const sorted = [...rows.map((r) => r.ticker)].sort();
  await expect(page.locator("tbody tr").first().getByRole("button").first()).toHaveText(sorted[0]);
});

test("a trader adds a ticker (400 errors on the fields) and deactivates it from the drawer", async ({ page }) => {
  const ticker = `E${Math.random().toString(36).slice(2, 7).toUpperCase()}`;
  const admin = await apiAs("admin");
  try {
    await login(page, "trader");
    await page.getByRole("button", { name: "Add ticker" }).click();
    const dialog = page.getByRole("dialog", { name: "Add ticker" });

    // No name: the API refuses and the message lands under the Name field.
    await dialog.getByLabel("Ticker", { exact: true }).fill(ticker);
    await dialog.getByRole("button", { name: "Add ticker" }).click();
    await expect(dialog.locator("#add-name-error")).toBeVisible();

    await dialog.getByLabel("Name").fill("E2E test fund");
    await dialog.getByRole("button", { name: "Add ticker" }).click();
    await expect(dialog).toBeHidden();
    const row = watchRow(page, ticker);
    await expect(row).toBeVisible();
    // No prices yet: the row says so instead of showing a number without a date.
    await expect(row).toContainText("Insufficient data");
    await expect(row.getByTestId("stale-badge")).toBeVisible();

    await page.getByRole("button", { name: `Open ${ticker} chart` }).click();
    const drawer = page.getByTestId("instrument-drawer");
    await expect(drawer).toContainText("No completed bars are stored");
    await drawer.getByRole("button", { name: "Deactivate ticker" }).click();
    await page.getByRole("button", { name: "Deactivate", exact: true }).click();
    await expect(row).toHaveCount(0);
  } finally {
    // Deactivated tickers cannot be deleted through the API; make sure it is inactive even if the UI step failed.
    await admin.put(`/api/v1/instruments/${ticker}`, { data: { name: "E2E test fund", active: false } });
    await admin.dispose();
  }
});
