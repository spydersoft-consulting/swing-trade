// The week's main loop as a trader does it: pick a candidate on the watchlist, size it, plan it, fill it, close it, and see
// the dashboard move. Every figure the UI shows is compared with what the API returns for the same data.
import { expect, test } from "@playwright/test";
import { apiAs, login, openScreen, uiMoney, watchConsole, watchRow } from "./support";

interface Dashboard {
  realizedPnl: number;
  accountValue: number;
  closedCount: number;
}
interface Trade {
  id: number;
  ticker: string;
  status: string;
  pnl: number | null;
}

test("plan a trade from the watchlist, size it, create it, fill it, close it, and the dashboard reflects it", async ({
  page,
}) => {
  test.setTimeout(120_000);
  const consoleErrors = watchConsole(page);
  const admin = await apiAs("admin");
  const before = (await (await admin.get("/api/v1/dashboard")).json()) as Dashboard;
  const quote = ((await (await admin.get("/api/v1/watchlist")).json()) as { ticker: string; last: number }[]).find(
    (r) => r.ticker === "QQQ",
  )!;
  const entry = quote.last;
  const stop = (entry - 8).toFixed(2);
  const target = (entry + 22).toFixed(2);

  try {
    await login(page, "trader");

    // 1. Watchlist: open the chart drawer for the Pullback candidate and plan a trade from it.
    await expect(watchRow(page, "QQQ")).toContainText("Pullback candidate");
    await page.getByRole("button", { name: "Open QQQ chart" }).click();
    const drawer = page.getByTestId("instrument-drawer");
    await expect(drawer.locator("canvas")).toBeVisible();
    await expect(page.getByTestId("chart-asof")).toContainText("completed sessions");
    await drawer.getByRole("button", { name: "Plan a trade" }).click();

    // 2. Sizer, prefilled with the last close. The result on screen is the API's answer for the same inputs.
    await expect(page.getByRole("heading", { level: 1, name: "Sizer" })).toBeVisible();
    await expect(page.getByLabel("Ticker (optional)")).toHaveValue("QQQ");
    await expect(page.getByLabel("Entry")).toHaveValue(String(entry));
    await page.getByLabel("Stop").fill(stop);
    await page.getByLabel("Target").fill(target);
    await expect(page.getByTestId("sizer-verdict")).toHaveText("OK - log it");

    const sized = (await (
      await admin.post("/api/v1/sizer", { data: { ticker: "QQQ", entry, stop: Number(stop), target: Number(target) } })
    ).json()) as {
      shares: number;
      maxEntryForMinRewardRisk: number;
    };
    await expect(page.getByTestId("sizer-shares")).toHaveText(String(sized.shares));
    await expect(page.getByTestId("sizer-max-entry")).toHaveText(uiMoney(sized.maxEntryForMinRewardRisk));

    // 3. Create the planned trade.
    await page.getByRole("button", { name: "Create planned trade" }).click();
    await expect(page.getByTestId("created-message")).toContainText("Planned trade saved");
    await page.getByRole("link", { name: "See it in Trades" }).click();

    // 4. Trades: it is on the Planned tab. Fill it.
    await expect(page.getByRole("tab", { name: /^Planned/ })).toHaveAttribute("aria-selected", "true");
    const row = page.getByRole("row").filter({ hasText: "QQQ" });
    await expect(row).toBeVisible();
    await row.getByRole("button", { name: "Fill QQQ" }).click();
    const fillDialog = page.getByRole("dialog", { name: "Fill QQQ" });
    await expect(fillDialog.getByLabel("Fill price")).toHaveValue(String(entry));
    await fillDialog.getByRole("button", { name: "Mark as filled" }).click();
    await expect(fillDialog).toBeHidden();

    // 5. Open tab: close it at a profit.
    await page.getByRole("tab", { name: /^Open/ }).click();
    const openRow = page.getByRole("row").filter({ hasText: "QQQ" });
    await openRow.getByRole("button", { name: "Close QQQ" }).click();
    const closeDialog = page.getByRole("dialog", { name: "Close QQQ" });
    const exit = (entry + 12).toFixed(2);
    await closeDialog.getByLabel("Exit price").fill(exit);
    await closeDialog.getByLabel("Lessons (optional)").fill("Followed the plan; e2e");
    await closeDialog.getByRole("button", { name: "Close trade" }).click();
    await expect(closeDialog).toBeHidden();

    // 6. Closed tab: the P&L shown is the API's P&L.
    await page.getByRole("tab", { name: /^Closed/ }).click();
    const trades = (await (await admin.get("/api/v1/trades?status=Closed")).json()) as Trade[];
    const mine = trades.find((t) => t.ticker === "QQQ")!;
    expect(mine.pnl).not.toBeNull();
    await expect(page.getByRole("row").filter({ hasText: "QQQ" })).toContainText(uiMoney(mine.pnl!));

    // 7. Dashboard reflects it.
    await openScreen(page, "Dashboard");
    const after = (await (await admin.get("/api/v1/dashboard")).json()) as Dashboard;
    expect(after.closedCount).toBe(before.closedCount + 1);
    expect(after.realizedPnl).not.toBe(before.realizedPnl);
    await expect(page.getByTestId("kpi-account")).toContainText(uiMoney(after.accountValue));
    await expect(page.getByTestId("kpi-realized")).toContainText(uiMoney(after.realizedPnl));
    await expect(page.getByTestId("performance")).toBeVisible();
    await expect(page.getByTestId("by-setup")).toContainText("Pullback");
    expect(consoleErrors()).toEqual([]);
  } finally {
    const all = (await (await admin.get("/api/v1/trades")).json()) as Trade[];
    for (const t of all.filter((x) => x.ticker === "QQQ")) await admin.delete(`/api/v1/trades/${t.id}`);
    await admin.dispose();
  }
});
