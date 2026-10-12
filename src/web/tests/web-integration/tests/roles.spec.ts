// Role gating in the browser. The API is the only enforcement point; these specs check that the UI tells each person the
// truth about what they may do (hidden or disabled actions, the Access not granted page) and that a 403 is never masked.
import { expect, test } from "@playwright/test";
import { login, openScreen, watchConsole, watchRow } from "./support";

test.describe("a user with no swingtrade role", () => {
  test("sees Access not granted, naming the roles to request, and no app", async ({ page }) => {
    const consoleErrors = watchConsole(page);
    await login(page, "norole");

    const notice = page.getByTestId("access-not-granted");
    await expect(notice.getByRole("heading", { level: 1, name: "Access not granted" })).toBeVisible();
    for (const role of ["swingtrade.reader", "swingtrade.trader", "swingtrade.admin"]) {
      await expect(notice).toContainText(role);
    }
    await expect(page.getByRole("navigation", { name: "Screens" }).getByRole("link")).toHaveCount(0);
    await expect(page.getByRole("heading", { level: 1, name: "Watchlist" })).toHaveCount(0);

    // Deep links get the same page, not an empty screen.
    await page.goto("/trades");
    await expect(page.getByTestId("access-not-granted")).toBeVisible();

    // The BFF forwards the token and does not mask the API's 403.
    expect((await page.request.get("/api/v1/watchlist")).status()).toBe(403);

    await expect(page.getByRole("button", { name: "Sign out" }).first()).toBeVisible();
    // The expected 403 above is the browser logging a failed resource; the app itself must not have errored.
    expect(consoleErrors().filter((e) => !e.includes("403"))).toEqual([]);
  });
});

test.describe("a reader", () => {
  test("can look at everything and is offered no write action", async ({ page }) => {
    const consoleErrors = watchConsole(page);
    await login(page, "reader");

    // Watchlist: no Add ticker, no Refresh now; the drawer has Plan a trade but not Deactivate.
    await expect(page.getByRole("heading", { level: 1, name: "Watchlist" })).toBeVisible();
    await expect(watchRow(page, "QQQ")).toBeVisible();
    await expect(page.getByRole("button", { name: "Add ticker" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Refresh now" })).toHaveCount(0);
    await page.getByRole("button", { name: "Open QQQ chart" }).click();
    const drawer = page.getByTestId("instrument-drawer");
    await expect(drawer.getByRole("button", { name: "Plan a trade" })).toBeVisible();
    await expect(drawer.getByRole("button", { name: "Deactivate ticker" })).toHaveCount(0);
    await page.keyboard.press("Escape");

    // Trades: no New trade, no row actions.
    await openScreen(page, "Trades");
    await expect(page.getByRole("button", { name: "New trade" })).toHaveCount(0);
    await page.getByRole("tab", { name: /^All/ }).click();
    await expect(page.getByRole("button", { name: /^(Fill|Close|Edit|Cancel) / })).toHaveCount(0);

    // Sizer: the calculation works for a reader, saving does not.
    await openScreen(page, "Sizer");
    await page.getByLabel("Entry").fill("62.82");
    await page.getByLabel("Stop").fill("61.40");
    await page.getByLabel("Target").fill("65.90");
    await expect(page.getByTestId("sizer-verdict")).toHaveText("OK - log it");
    await expect(page.getByRole("button", { name: "Create planned trade" })).toHaveCount(0);

    // Journal: notes are visible, adding is not offered.
    await openScreen(page, "Journal");
    await expect(page.getByRole("button", { name: "Record a pass" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Add note" })).toHaveCount(0);

    // Settings: read-only, with no Save and no MCP panel.
    await openScreen(page, "Settings");
    await expect(page.getByTestId("settings-readonly")).toBeVisible();
    await expect(page.getByLabel(/Max risk per trade/)).toBeDisabled();
    await expect(page.getByRole("button", { name: "Save settings" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Reset to defaults" })).toHaveCount(0);
    await expect(page.getByTestId("mcp-panel")).toHaveCount(0);
    await expect(page.getByTestId("access-info")).toContainText("swingtrade.reader");

    expect(consoleErrors()).toEqual([]);
  });

  test("a write the UI would not offer is still refused by the API through the BFF", async ({ page }) => {
    await login(page, "reader");
    const response = await page.request.post("/api/v1/notes", {
      data: { date: "2026-10-12", kind: "General", text: "nope" },
    });
    expect(response.status()).toBe(403);
  });
});

test.describe("a trader", () => {
  test("can write trades and notes but not change settings or refresh", async ({ page }) => {
    await login(page, "trader");
    await expect(watchRow(page, "QQQ")).toBeVisible();
    await expect(page.getByRole("button", { name: "Add ticker" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Refresh now" })).toHaveCount(0);

    await openScreen(page, "Trades");
    await expect(page.getByRole("button", { name: "New trade" })).toBeVisible();

    await openScreen(page, "Journal");
    await expect(page.getByRole("button", { name: "Record a pass" })).toBeVisible();

    await openScreen(page, "Settings");
    await expect(page.getByTestId("settings-readonly")).toBeVisible();
    await expect(page.getByRole("button", { name: "Save settings" })).toHaveCount(0);
    await expect(page.getByTestId("mcp-panel")).toHaveCount(0);

    // The BFF passes the API's 403 through for a trader on an admin endpoint.
    expect((await page.request.get("/api/v1/mcp/calls")).status()).toBe(403);
  });
});

test.describe("an admin", () => {
  test("sees Refresh now, editable settings and the MCP panel", async ({ page }) => {
    const consoleErrors = watchConsole(page);
    await login(page, "admin");
    await expect(page.getByRole("button", { name: "Refresh now" })).toBeVisible();

    await openScreen(page, "Settings");
    await expect(page.getByLabel(/Max risk per trade/)).toBeEnabled();
    await expect(page.getByRole("button", { name: "Save settings" })).toBeVisible();
    await expect(page.getByTestId("settings-readonly")).toHaveCount(0);

    const panel = page.getByTestId("mcp-panel");
    await expect(panel).toBeVisible();
    await expect(panel).toContainText("http://localhost:8180/mcp");
    await expect(panel).toContainText("mcponly-1");
    await expect(panel).toContainText("Off (read-only)");
    await expect(panel.getByRole("columnheader", { name: "Tool" })).toBeVisible();

    // The assumption flag sits next to the tax rate.
    await expect(page.getByTestId("assumption-flag")).toContainText("Assumption");
    expect(consoleErrors()).toEqual([]);
  });
});
