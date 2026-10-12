// An admin changes a threshold and the Watchlist signal changes on the next load, with no refresh in between.
import { expect, test } from "@playwright/test";
import { apiAs, apiWatchlist, login, openScreen, watchConsole, watchRow } from "./support";

type Settings = Record<string, number | string>;

test("admin changes the pullback band and the signal changes on the next load; reset to defaults restores it", async ({
  page,
}) => {
  test.setTimeout(90_000);
  const consoleErrors = watchConsole(page);
  const admin = await apiAs("admin");
  const original = (await (await admin.get("/api/v1/settings")).json()) as Settings;

  try {
    const before = await apiWatchlist();
    const candidate = before.find((r) => r.signalText === "Pullback candidate");
    expect(candidate, "the canned data has a Pullback candidate").toBeTruthy();
    const asOfBefore = candidate!.asOfDate;
    // Put the band's top just under the candidate's distance from its 20-day average, so it falls out of the band.
    const justBelowPct = Math.floor(candidate!.pctVs20! * 100 * 10) / 10 - 0.1;

    await login(page, "admin");
    await expect(watchRow(page, candidate!.ticker)).toContainText("Pullback candidate");

    await openScreen(page, "Settings");
    const high = page.getByLabel(/Pullback band, high/);
    await expect(high).toHaveValue("2");
    await high.fill(String(justBelowPct));
    await page.getByRole("button", { name: "Save settings" }).click();
    await expect(page.getByText("Settings saved")).toBeVisible();

    const saved = (await (await admin.get("/api/v1/settings")).json()) as Settings;
    expect(saved.pullbackBandHigh).toBeCloseTo(justBelowPct / 100, 6);

    // Back on the Watchlist (a fresh load of the screen, no refresh): the same bars now give a different signal.
    await openScreen(page, "Watchlist");
    await expect(watchRow(page, candidate!.ticker)).not.toContainText("Pullback candidate");
    await expect(watchRow(page, candidate!.ticker)).toContainText("Watch");
    const after = await apiWatchlist();
    expect(after.find((r) => r.ticker === candidate!.ticker)!.asOfDate).toBe(asOfBefore);

    // Reset to defaults loads the plan's values into the form; Save applies them.
    await openScreen(page, "Settings");
    await page.getByRole("button", { name: "Reset to defaults" }).click();
    await expect(page.getByLabel(/Pullback band, high/)).toHaveValue("2");
    await page.getByRole("button", { name: "Save settings" }).click();
    await expect(page.getByText("Settings saved").first()).toBeVisible();
    await openScreen(page, "Watchlist");
    await expect(watchRow(page, candidate!.ticker)).toContainText("Pullback candidate");
    expect(consoleErrors()).toEqual([]);
  } finally {
    await admin.put("/api/v1/settings", { data: original });
    await admin.dispose();
  }
});

test("a value the API rejects is shown against its field", async ({ page }) => {
  const admin = await apiAs("admin");
  const original = (await (await admin.get("/api/v1/settings")).json()) as Settings;
  try {
    await login(page, "admin");
    await openScreen(page, "Settings");
    await page.getByLabel(/Max open positions/).fill("0");
    await page.getByRole("button", { name: "Save settings" }).click();
    await expect(page.getByRole("alert").filter({ hasText: /./ }).first()).toBeVisible();
    await expect(page.getByText("Settings saved")).toHaveCount(0);

    // Text that is not a number is caught before it is sent.
    await page.getByLabel(/Max open positions/).fill("abc");
    await page.getByRole("button", { name: "Save settings" }).click();
    await expect(page.locator("#setting-maxOpenPositions-error")).toContainText("number");
  } finally {
    await admin.put("/api/v1/settings", { data: original });
    await admin.dispose();
  }
});
