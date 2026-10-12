// Runs first (see the "refresh" project in playwright.config.ts): it loads the canned WireMock bars that every other
// spec reads. Assertions about the first load are written so they also hold when a reused stack was already refreshed.
import { EXPECTED, FAILING_TICKER, TIINGO_TICKER } from "./market-data";
import { expect, json, runRefresh, test, waitForRun } from "./fixtures";
import type { BarsDto, PagedResult, RefreshRunDto, WatchlistRowDto } from "./types";

const loadedTickers = Object.keys(EXPECTED);

async function barCounts(api: Parameters<typeof runRefresh>[0]): Promise<Record<string, number>> {
  const counts: Record<string, number> = {};
  for (const ticker of [...loadedTickers, FAILING_TICKER]) {
    const bars = await json<BarsDto>(await api.reader.get(`/api/v1/instruments/${ticker}/bars?days=120`));
    counts[ticker] = bars.bars.length;
  }
  return counts;
}

test.describe.configure({ mode: "serial" });

test("POST /refresh against WireMock loads bars and indicators and records a RefreshRun", async ({ api }) => {
  const run = await runRefresh(api);

  // 12 active tickers (SPY, the benchmark, is one of them); one fails, so the run is Partial.
  expect(run.status).toBe("Partial");
  expect(run.tickersOk).toBe(11);
  expect(run.tickersFailed).toBe(1);
  expect(run.finishedAt).not.toBeNull();
  expect(run.provider).toContain("alpaca");
  expect(run.provider).toContain("tiingo");
  expect(run.message).toContain(FAILING_TICKER);
  expect(run.message).toContain(`${TIINGO_TICKER} via tiingo`);

  const bars = await barCounts(api);
  for (const [ticker, expected] of Object.entries(EXPECTED)) {
    expect(bars[ticker], `bars stored for ${ticker}`).toBe(expected.bars);
  }
  expect(bars[FAILING_TICKER], "the failing ticker stores nothing").toBe(0);
});

test("the indicators the run computed give each ticker its trend and signal", async ({ api }) => {
  const rows = await json<WatchlistRowDto[]>(await api.reader.get("/api/v1/watchlist"));

  for (const [ticker, expected] of Object.entries(EXPECTED)) {
    const row = rows.find((r) => r.ticker === ticker)!;
    expect(row.trend, `${ticker} trend`).toBe(expected.trend);
    expect(row.signal, `${ticker} signal`).toBe(expected.signal);
    expect(row.asOfDate, `${ticker} as-of`).toBe("2026-10-09");
  }
  const failed = rows.find((r) => r.ticker === FAILING_TICKER)!;
  expect(failed.asOfDate).toBeNull();
  expect(failed.stale).toBe(true);
});

test("a failing ticker is recorded without stopping the others", async ({ api }) => {
  const page = await json<PagedResult<RefreshRunDto>>(await api.reader.get("/api/v1/refresh/runs?page=1&pageSize=1"));
  const latest = page.items[0];

  expect(latest.tickersFailed).toBe(1);
  expect(latest.tickersOk).toBe(11);
  expect(latest.message).toMatch(new RegExp(`${FAILING_TICKER}: `));
});

test("running the refresh again is idempotent", async ({ api }) => {
  const before = await barCounts(api);
  const beforeRows = await json<WatchlistRowDto[]>(await api.reader.get("/api/v1/watchlist"));

  const second = await runRefresh(api);

  expect(second.status).toBe("Partial");
  expect(second.tickersOk).toBe(11);
  expect(second.tickersFailed).toBe(1);
  expect(await barCounts(api)).toEqual(before);
  const afterRows = await json<WatchlistRowDto[]>(await api.reader.get("/api/v1/watchlist"));
  expect(afterRows).toEqual(beforeRows);
});

test("run history lists runs newest first, paged, and a single run can be read", async ({ api }) => {
  const page = await json<PagedResult<RefreshRunDto>>(await api.reader.get("/api/v1/refresh/runs?page=1&pageSize=2"));

  expect(page.page).toBe(1);
  expect(page.pageSize).toBe(2);
  expect(page.totalCount).toBeGreaterThanOrEqual(2);
  expect(page.items.length).toBe(2);
  expect(page.items[0].id).toBeGreaterThan(page.items[1].id);

  const one = await json<RefreshRunDto>(await api.reader.get(`/api/v1/refresh/runs/${page.items[0].id}`));
  expect(one).toEqual(page.items[0]);
  expect((await api.reader.get("/api/v1/refresh/runs/999999")).status()).toBe(404);
});

test("POST /refresh answers 202 with a Location, and a run in progress answers 409", async ({ api }) => {
  const first = await api.admin.post("/api/v1/refresh");
  expect([202, 409]).toContain(first.status());
  let id: number;
  if (first.status() === 202) {
    const run = (await first.json()) as RefreshRunDto;
    expect(run.status).toBe("Running");
    expect(first.headers()["location"]).toBe(`/api/v1/refresh/runs/${run.id}`);
    id = run.id;
  } else {
    id = (await json<PagedResult<RefreshRunDto>>(await api.admin.get("/api/v1/refresh/runs?page=1&pageSize=1"))).items[0].id;
  }
  await waitForRun(api.admin, id);
});
