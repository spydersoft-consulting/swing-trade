import { expect, test } from "@playwright/test";
import { login, openScreen, watchConsole } from "./support";

test.describe("OIDC login through the mock IdP", () => {
  test("before login the BFF answers 401 for userinfo and for the proxied API", async ({ request }) => {
    expect((await request.get("/oauth2/userinfo")).status()).toBe(401);
    expect((await request.get("/api/v1/watchlist")).status()).toBe(401);
  });

  test("the public landing page offers sign in", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByRole("heading", { level: 1 })).toContainText("swing-trading routine");
    await expect(page.getByRole("button", { name: "Sign in" })).toBeVisible();
  });

  test("a trader signs in, lands on the watchlist, and the whole shell is there", async ({ page }) => {
    const consoleErrors = watchConsole(page);
    await login(page, "trader");

    await expect(page.getByRole("heading", { level: 1, name: "Watchlist" })).toBeVisible();
    const nav = page.getByRole("navigation", { name: "Screens" });
    for (const name of ["Watchlist", "Trades", "Sizer", "Dashboard", "Journal", "Settings"]) {
      await expect(nav.getByRole("link", { name })).toBeVisible();
    }
    await expect(page.getByTestId("account-summary")).toContainText("trader");

    // The BFF holds the token and forwards it: the signed-in session can call the API through /api/v1.
    const response = await page.request.get("/api/v1/auth/info");
    expect(response.status()).toBe(200);
    expect(((await response.json()) as { roles: string[] }).roles).toEqual(["swingtrade.trader"]);

    // /mcp and /.well-known are not proxied by the BFF (they are published separately).
    const mcp = await page.request.get("/mcp", { headers: { accept: "application/json" } });
    expect(mcp.headers()["content-type"] ?? "").toContain("text/html");

    for (const screen of ["Trades", "Sizer", "Dashboard", "Journal", "Settings", "Watchlist"] as const) {
      await openScreen(page, screen);
    }
    expect(consoleErrors()).toEqual([]);
  });

  test("signing out ends the BFF session", async ({ page }) => {
    await login(page, "reader");
    const signOut = await page.request.get("/oauth2/sign_out", { maxRedirects: 0 });
    expect(signOut.status()).toBe(302);
    expect((await page.request.get("/oauth2/userinfo")).status()).toBe(401);
  });
});
