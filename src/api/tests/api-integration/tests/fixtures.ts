import { test as base, expect, type APIRequestContext, type APIResponse } from "@playwright/test";
import { BASE_URL, PERSONAS, mintExpiredToken, mintToken, mintWrongAudienceToken, type Persona } from "./auth";
import type {
  CreateTradeRequest,
  InstrumentDto,
  RefreshRunDto,
  SettingsDto,
  TradeDto,
  ValidationProblem,
} from "./types";

export interface Api {
  norole: APIRequestContext;
  reader: APIRequestContext;
  trader: APIRequestContext;
  admin: APIRequestContext;
  mcponly: APIRequestContext;
  /** No Authorization header at all. */
  anonymous: APIRequestContext;
  /** A token signed by the mock authority but issued for another audience. */
  wrongAudience: APIRequestContext;
  /** A token whose lifetime has already ended. */
  expired: APIRequestContext;
  as(persona: Persona): APIRequestContext;
}

interface TestFixtures {
  /** Creates trades and instruments that are removed (trades deleted, instruments deactivated) when the test ends. */
  tracked: {
    trade(body: CreateTradeRequest): Promise<TradeDto>;
    track(id: number): void;
    instrument(ticker: string, name?: string): Promise<InstrumentDto>;
  };
  /** Restores the Settings row to what it was before the test. */
  settingsGuard: SettingsDto;
}

interface WorkerFixtures {
  api: Api;
}

export const test = base.extend<TestFixtures, WorkerFixtures>({
  api: [
    async ({ playwright }, use) => {
      const make = async (token?: string) =>
        playwright.request.newContext({
          baseURL: BASE_URL,
          extraHTTPHeaders: token ? { Authorization: `Bearer ${token}` } : {},
        });

      const contexts: Record<string, APIRequestContext> = {};
      for (const persona of PERSONAS) contexts[persona] = await make(await mintToken(persona));
      const anonymous = await make();
      const wrongAudience = await make(await mintWrongAudienceToken());
      const expired = await make(await mintExpiredToken());

      const api: Api = {
        norole: contexts.norole,
        reader: contexts.reader,
        trader: contexts.trader,
        admin: contexts.admin,
        mcponly: contexts.mcponly,
        anonymous,
        wrongAudience,
        expired,
        as: (persona) => contexts[persona],
      };
      await use(api);

      await Promise.all([...Object.values(contexts), anonymous, wrongAudience, expired].map((c) => c.dispose()));
    },
    { scope: "worker" },
  ],

  tracked: async ({ api }, use) => {
    const trades: number[] = [];
    const instruments: string[] = [];
    await use({
      async trade(body) {
        const response = await api.trader.post("/api/v1/trades", { data: body });
        expect(response.status(), await response.text()).toBe(201);
        const dto = (await response.json()) as TradeDto;
        trades.push(dto.id);
        return dto;
      },
      track: (id) => void trades.push(id),
      async instrument(ticker, name = `Test ${ticker}`) {
        const response = await api.trader.post("/api/v1/instruments", { data: { ticker, name, active: true } });
        expect(response.status(), await response.text()).toBe(201);
        instruments.push(ticker);
        return (await response.json()) as InstrumentDto;
      },
    });

    for (const id of trades) await api.admin.delete(`/api/v1/trades/${id}`);
    // There is no instrument delete endpoint, so a test instrument is deactivated: it leaves the watchlist and refresh.
    for (const ticker of instruments) {
      await api.admin.put(`/api/v1/instruments/${ticker}`, { data: { name: `Test ${ticker}`, active: false } });
    }
  },

  settingsGuard: async ({ api }, use) => {
    const original = (await (await api.admin.get("/api/v1/settings")).json()) as SettingsDto;
    await use(original);
    const restored = await api.admin.put("/api/v1/settings", { data: original });
    expect(restored.status(), "restoring the original settings").toBe(200);
  },
});

export { expect };

/** A unique ticker that satisfies the API pattern (letters and digits, at most 16). */
export function uniqueTicker(prefix = "T"): string {
  return (prefix + Math.random().toString(36).slice(2, 9)).toUpperCase().slice(0, 12);
}

export async function json<T>(response: APIResponse, expectedStatus = 200): Promise<T> {
  expect(response.status(), await response.text()).toBe(expectedStatus);
  return (await response.json()) as T;
}

/** Asserts a 400 whose ValidationProblemDetails names (at least) these fields. */
export async function expectFieldErrors(response: APIResponse, ...fields: string[]): Promise<ValidationProblem> {
  expect(response.status(), await response.text()).toBe(400);
  const problem = (await response.json()) as ValidationProblem;
  for (const field of fields) {
    const keys = Object.keys(problem.errors ?? {}).map((k) => k.toLowerCase());
    expect(keys, `field errors should name ${field}`).toContain(field.toLowerCase());
  }
  return problem;
}

const POLL_INTERVAL_MS = 250;
const REFRESH_TIMEOUT_MS = 90_000;

/** Polls a refresh run until it leaves Running. */
export async function waitForRun(ctx: APIRequestContext, id: number): Promise<RefreshRunDto> {
  const deadline = Date.now() + REFRESH_TIMEOUT_MS;
  for (;;) {
    const run = await json<RefreshRunDto>(await ctx.get(`/api/v1/refresh/runs/${id}`));
    if (run.status !== "Running") return run;
    if (Date.now() > deadline) throw new Error(`Refresh run ${id} was still Running after ${REFRESH_TIMEOUT_MS} ms.`);
    await new Promise((resolve) => setTimeout(resolve, POLL_INTERVAL_MS));
  }
}

/** Starts a refresh as admin and waits for it. If one is already running (409), waits for that one instead. */
export async function runRefresh(api: Api): Promise<RefreshRunDto> {
  const response = await api.admin.post("/api/v1/refresh");
  if (response.status() === 202) return waitForRun(api.admin, ((await response.json()) as RefreshRunDto).id);
  expect(response.status(), await response.text()).toBe(409);
  const page = await json<{ items: RefreshRunDto[] }>(await api.admin.get("/api/v1/refresh/runs?page=1&pageSize=5"));
  const running = page.items.find((r) => r.status === "Running");
  if (!running) return runRefresh(api);
  return waitForRun(api.admin, running.id);
}

/** Today in the test process, as yyyy-MM-dd (UTC). */
export function isoDate(daysFromNow = 0): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + daysFromNow);
  return d.toISOString().slice(0, 10);
}
