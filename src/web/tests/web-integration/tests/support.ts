// Helpers for the browser specs: sign in through the mock OIDC server as a persona, and reach the API directly (with a
// token minted from the same mock) to read what the UI should be showing and to clean up after a spec.
import { expect, type APIRequestContext, type Page, type ConsoleMessage, request as pwRequest } from "@playwright/test";

export const API_URL = process.env.SWINGTRADE_API_URL ?? "http://localhost:8180";
export const OIDC_URL = process.env.SWINGTRADE_OIDC_URL ?? "http://localhost:8210";

export const PERSONAS = ["norole", "reader", "trader", "admin"] as const;
export type Persona = (typeof PERSONAS)[number];

const PASSWORD = "Test123!";

/** Signs in through the BFF and the mock IdP's login page, and waits until the app shell is showing. */
export async function login(page: Page, persona: Persona): Promise<void> {
  await page.goto("/oauth2/sign_in");
  await page.getByLabel("Username").fill(persona);
  await page.getByLabel("Password").fill(PASSWORD);
  await page.getByRole("button", { name: "Login" }).click();
  const base = new URL(process.env.SWINGTRADE_WEB_BASE_URL ?? "http://localhost:8190");
  await page.waitForURL((url) => url.origin === base.origin);
  await expect(page.getByRole("complementary", { name: "Primary" })).toBeVisible();
}

/** Opens a screen by its navigation link (a client-side route change, like a person would). */
export async function openScreen(
  page: Page,
  name: "Watchlist" | "Trades" | "Sizer" | "Dashboard" | "Journal" | "Settings",
) {
  await page.getByRole("navigation", { name: "Screens" }).getByRole("link", { name }).click();
  await expect(page.getByRole("heading", { level: 1, name })).toBeVisible();
}

/** Collects console errors so a spec can assert the screens ran clean. */
export function watchConsole(page: Page): () => string[] {
  const errors: string[] = [];
  const onConsole = (message: ConsoleMessage) => {
    if (message.type() === "error") errors.push(message.text());
  };
  page.on("console", onConsole);
  page.on("pageerror", (error) => errors.push(`pageerror: ${error.message}`));
  return () => errors;
}

async function mintToken(persona: Persona): Promise<string> {
  const response = await fetch(`${OIDC_URL}/connect/token`, {
    method: "POST",
    headers: { "content-type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      grant_type: "password",
      client_id: "swingtrade-api-test",
      client_secret: "swingtrade-api-test-secret",
      username: persona,
      password: PASSWORD,
      scope: "swingtrade",
    }),
  });
  if (!response.ok) throw new Error(`Could not mint a token for ${persona}: HTTP ${response.status}`);
  return ((await response.json()) as { access_token: string }).access_token;
}

/** A request context that talks to the API directly as a persona (the UI goes through the BFF; this does not). */
export async function apiAs(persona: Persona): Promise<APIRequestContext> {
  return pwRequest.newContext({
    baseURL: API_URL,
    extraHTTPHeaders: { Authorization: `Bearer ${await mintToken(persona)}` },
  });
}

export interface WatchlistRow {
  ticker: string;
  asOfDate: string | null;
  last: number | null;
  pctVs20: number | null;
  signal: string;
  signalText: string;
  stale: boolean;
}

export async function apiWatchlist(): Promise<WatchlistRow[]> {
  const api = await apiAs("admin");
  try {
    const response = await api.get("/api/v1/watchlist");
    expect(response.status()).toBe(200);
    return (await response.json()) as WatchlistRow[];
  } finally {
    await api.dispose();
  }
}

/** Waits for a refresh run started through the API (or the UI) to leave the Running state. */
export async function waitForRefreshIdle(api: APIRequestContext): Promise<void> {
  await expect
    .poll(
      async () => {
        const runs = (await (await api.get("/api/v1/refresh/runs?pageSize=1")).json()) as {
          items: { status: string }[];
        };
        return runs.items[0]?.status ?? "none";
      },
      { timeout: 60_000 },
    )
    .not.toBe("Running");
}

/** "2026-10-09" as the UI shows it: "Oct 9, 2026". */
export const uiDate = (iso: string): string => {
  const [y, m, d] = iso.split("-").map(Number);
  return new Date(Date.UTC(y, m - 1, d)).toLocaleDateString("en-US", {
    year: "numeric",
    month: "short",
    day: "numeric",
    timeZone: "UTC",
  });
};

/** 1234.5 as the UI shows money: "$1,234.50". */
export const uiMoney = (value: number): string =>
  value.toLocaleString("en-US", {
    style: "currency",
    currency: "USD",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });

/** The watchlist table row for one ticker. */
export const watchRow = (page: Page, ticker: string) =>
  page.getByRole("row").filter({ has: page.getByRole("button", { name: `Open ${ticker} chart` }) });

/** Today's date in US market time as yyyy-MM-dd (the same convention as the API). */
export const todayInNewYork = (): string =>
  new Intl.DateTimeFormat("en-CA", {
    timeZone: "America/New_York",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date());
