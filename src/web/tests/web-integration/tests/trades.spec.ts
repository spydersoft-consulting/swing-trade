// Trades screen behaviours that depend on what the API says: row highlights from its flags, field errors from a 400,
// an illegal transition from a 409, and the live sizer in the new-trade form.
import { expect, test } from "@playwright/test";
import { apiAs, login, todayInNewYork, watchConsole, watchRow } from "./support";

interface Trade {
  id: number;
  ticker: string;
  status: string;
}

const cleanup = async (admin: Awaited<ReturnType<typeof apiAs>>, ids: number[]) => {
  for (const id of ids) await admin.delete(`/api/v1/trades/${id}`);
  await admin.dispose();
};

test("rows are highlighted from the API's flags: risk over the limit, reward:risk below the minimum", async ({
  page,
}) => {
  const trader = await apiAs("trader");
  const admin = await apiAs("admin");
  const ids: number[] = [];
  try {
    // 10 shares risking 10 each is $100 against a $30 budget, with a reward:risk of 0.5.
    const created = await trader.post("/api/v1/trades", {
      data: {
        ticker: "QQQ",
        status: "Planned",
        setup: "Pullback",
        shares: 10,
        entryPrice: 100,
        stopPrice: 90,
        targetPrice: 105,
      },
    });
    expect(created.status()).toBe(201);
    const trade = (await created.json()) as Trade & { riskOverLimit: boolean; rewardRiskBelowMin: boolean };
    ids.push(trade.id);
    expect(trade.riskOverLimit).toBe(true);
    expect(trade.rewardRiskBelowMin).toBe(true);

    await login(page, "trader");
    await page.goto("/trades?tab=planned");
    const row = page.getByRole("row").filter({ hasText: "QQQ" });
    await expect(row).toBeVisible();
    await expect(row).toHaveClass(/row-bad/);
    await expect(row.getByTestId("flag-risk")).toHaveText("Risk over limit");
    await expect(row.getByTestId("flag-rr")).toHaveText("R:R below min");
  } finally {
    await trader.dispose();
    await cleanup(admin, ids);
  }
});

test("a wash-sale flag shows on the trade and the watchlist shows the re-entry date", async ({ page }) => {
  const trader = await apiAs("trader");
  const admin = await apiAs("admin");
  const ids: number[] = [];
  const today = todayInNewYork();
  try {
    const first = await trader.post("/api/v1/trades", {
      data: {
        ticker: "XLV",
        status: "Open",
        openDate: today,
        setup: "Pullback",
        shares: 5,
        entryPrice: 50,
        stopPrice: 48,
        targetPrice: 56,
      },
    });
    const loser = (await first.json()) as Trade;
    ids.push(loser.id);
    const closed = await trader.post(`/api/v1/trades/${loser.id}/close`, {
      data: { exitPrice: 49, exitDate: today, followedPlan: true },
    });
    expect(closed.status()).toBe(200);
    const second = await trader.post("/api/v1/trades", {
      data: {
        ticker: "XLV",
        status: "Open",
        openDate: today,
        setup: "Pullback",
        shares: 5,
        entryPrice: 50,
        stopPrice: 48,
        targetPrice: 56,
      },
    });
    const flagged = (await second.json()) as Trade & { washSaleFlag: boolean };
    ids.push(flagged.id);
    expect(flagged.washSaleFlag).toBe(true);

    await login(page, "trader");
    await expect(watchRow(page, "XLV")).toContainText("after ");
    await page.goto("/trades?tab=open");
    await expect(page.getByRole("row").filter({ hasText: "XLV" }).getByTestId("flag-wash")).toHaveText("Wash sale");
  } finally {
    await trader.dispose();
    await cleanup(admin, ids);
  }
});

test("the new-trade form shows the sizer live and maps 400 field errors onto the fields", async ({ page }) => {
  const consoleErrors = watchConsole(page);
  await login(page, "trader");
  await page.goto("/trades");
  await page.getByRole("button", { name: "New trade" }).click();
  const dialog = page.getByRole("dialog", { name: "New trade" });

  await dialog.getByLabel("Ticker", { exact: true }).selectOption("XLK");
  await dialog.getByLabel("Entry (limit price)").fill("192");
  await dialog.getByLabel("Stop price").fill("187.5");
  await dialog.getByLabel("Target price").fill("202");
  await expect(dialog.getByTestId("sizer-verdict")).toHaveText("OK - log it");
  await expect(dialog.getByTestId("sizer-max-entry")).toHaveText("$192.33");

  // Changing a price re-sizes without a button press.
  await dialog.getByLabel("Target price").fill("195");
  await expect(dialog.getByTestId("sizer-verdict")).toHaveText("SKIP - reward:risk below minimum");
  await dialog.getByLabel("Target price").fill("202");
  await expect(dialog.getByTestId("sizer-verdict")).toHaveText("OK - log it");

  // The sizer's share count is offered, not applied behind the user's back.
  await dialog.getByRole("button", { name: /^Use \d+ shares$/ }).click();
  await expect(dialog.locator("#trade-shares")).not.toHaveValue("");

  // A stop above the entry is rejected by the API with a field error, shown under the entry field.
  await dialog.getByLabel("Stop price").fill("200");
  await dialog.getByRole("button", { name: "Create trade" }).click();
  await expect(dialog.locator("#trade-entry-error")).toContainText("Entry must be above the stop");
  await expect(dialog).toBeVisible();
  expect(consoleErrors().filter((e) => !e.includes("400"))).toEqual([]);
});

test("a transition the API refuses (409) is explained, and the screen stays usable", async ({ page }) => {
  const trader = await apiAs("trader");
  const admin = await apiAs("admin");
  const ids: number[] = [];
  try {
    const created = await trader.post("/api/v1/trades", {
      data: {
        ticker: "XLK",
        status: "Planned",
        setup: "Pullback",
        shares: 1,
        entryPrice: 200,
        stopPrice: 190,
        targetPrice: 230,
      },
    });
    const trade = (await created.json()) as Trade;
    ids.push(trade.id);

    await login(page, "trader");
    await page.goto("/trades?tab=planned");
    const row = page.getByRole("row").filter({ hasText: "XLK" });
    await expect(row).toBeVisible();

    // Someone else (here: the API) fills the trade while this page still shows it as Planned.
    const filled = await trader.post(`/api/v1/trades/${trade.id}/fill`, {
      data: { fillPrice: 200, fillDate: todayInNewYork() },
    });
    expect(filled.status()).toBe(200);

    await row.getByRole("button", { name: "Fill XLK" }).click();
    const dialog = page.getByRole("dialog", { name: "Fill XLK" });
    await dialog.getByRole("button", { name: "Mark as filled" }).click();
    await expect(dialog.getByRole("alert")).toContainText(/Planned|status|fill/i);
    await expect(page.getByText("Cannot fill this trade")).toBeVisible();
    await dialog.getByRole("button", { name: "Cancel" }).click();
    await expect(page.getByRole("heading", { level: 1, name: "Trades" })).toBeVisible();
  } finally {
    await trader.dispose();
    await cleanup(admin, ids);
  }
});
