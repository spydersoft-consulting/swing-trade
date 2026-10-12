import { ACTIVE_TICKERS, EXPECTED, FAILING_TICKER, PULLBACK_TICKER, latestExpectedBar } from "./market-data";
import { expect, expectFieldErrors, json, test } from "./fixtures";
import type { WatchlistRowDto } from "./types";

test("GET /watchlist returns one row per active ticker with the Rules library's trend and signal", async ({ api }) => {
  const rows = await json<WatchlistRowDto[]>(await api.reader.get("/api/v1/watchlist"));

  expect(rows.map((r) => r.ticker).sort()).toEqual([...ACTIVE_TICKERS].sort());
  for (const [ticker, expected] of Object.entries(EXPECTED)) {
    const row = rows.find((r) => r.ticker === ticker)!;
    expect(row.trend, `${ticker} trend`).toBe(expected.trend);
    expect(row.signal, `${ticker} signal`).toBe(expected.signal);
    expect(row.name.length).toBeGreaterThan(0);
  }
});

test("rows carry as-of date, last, averages and % vs the 20-day average", async ({ api }) => {
  const rows = await json<WatchlistRowDto[]>(await api.reader.get("/api/v1/watchlist"));
  const qqq = rows.find((r) => r.ticker === PULLBACK_TICKER)!;

  expect(qqq.asOfDate).toBe("2026-10-09");
  expect(qqq.last).toBeGreaterThan(qqq.ma20!);
  expect(qqq.ma20).toBeGreaterThan(qqq.ma50!);
  expect(qqq.pctVs20).toBeCloseTo(qqq.last! / qqq.ma20! - 1, 6);
  expect(qqq.pctVs20).toBeGreaterThanOrEqual(-0.01);
  expect(qqq.pctVs20).toBeLessThanOrEqual(0.02);
  expect(qqq.signalText).toBe("Pullback candidate");
  expect(qqq.trendText).toBe("Uptrend");
  expect(qqq.reentryOkAfter).toBeNull();

  const xli = rows.find((r) => r.ticker === "XLI")!; // 49 bars
  expect(xli.ma50).toBeNull();
  expect(xli.signalText).toBe("Insufficient data");
});

test("the default sort puts Pullback candidates first, then tickers alphabetically", async ({ api }) => {
  const rows = await json<WatchlistRowDto[]>(await api.reader.get("/api/v1/watchlist"));

  expect(rows[0].ticker).toBe(PULLBACK_TICKER);
  const rest = rows.slice(1).map((r) => r.ticker);
  expect(rest).toEqual([...rest].sort());
});

test("the signal filter accepts the name or the display text", async ({ api }) => {
  for (const filter of ["PullbackCandidate", "Pullback%20candidate", "pullback%20CANDIDATE"]) {
    const rows = await json<WatchlistRowDto[]>(await api.reader.get(`/api/v1/watchlist?signal=${filter}`));
    expect(rows.map((r) => r.ticker), filter).toEqual([PULLBACK_TICKER]);
  }
  const extended = await json<WatchlistRowDto[]>(await api.reader.get("/api/v1/watchlist?signal=Extended%20-%20wait"));
  expect(extended.map((r) => r.ticker)).toEqual(["IWM"]);
  const avoid = await json<WatchlistRowDto[]>(await api.reader.get("/api/v1/watchlist?signal=avoid"));
  expect(avoid.map((r) => r.ticker)).toEqual(["XLF"]);
  const watch = await json<WatchlistRowDto[]>(await api.reader.get("/api/v1/watchlist?signal=Watch"));
  expect(watch.every((r) => r.signal === "Watch")).toBe(true);
  expect(watch.length).toBe(Object.values(EXPECTED).filter((e) => e.signal === "Watch").length);
});

test("sort options, ascending and descending", async ({ api }) => {
  const byPct = await json<WatchlistRowDto[]>(await api.reader.get("/api/v1/watchlist?sort=-pctVs20"));
  const numeric = byPct.filter((r) => r.pctVs20 !== null).map((r) => r.pctVs20!);
  expect(numeric).toEqual([...numeric].sort((a, b) => b - a));
  expect(byPct[byPct.length - 1].pctVs20, "rows without data sort last when descending").toBeNull();

  const byTickerDesc = await json<WatchlistRowDto[]>(await api.reader.get("/api/v1/watchlist?sort=-ticker"));
  expect(byTickerDesc.map((r) => r.ticker)).toEqual([...ACTIVE_TICKERS].sort().reverse());

  const byLast = await json<WatchlistRowDto[]>(await api.reader.get("/api/v1/watchlist?sort=last"));
  const lasts = byLast.filter((r) => r.last !== null).map((r) => r.last!);
  expect(lasts).toEqual([...lasts].sort((a, b) => a - b));
});

test("an unknown signal or sort is 400 with field errors", async ({ api }) => {
  await expectFieldErrors(await api.reader.get("/api/v1/watchlist?signal=moonshot"), "Signal");
  await expectFieldErrors(await api.reader.get("/api/v1/watchlist?sort=sideways"), "Sort");
});

test("stale: a ticker with no data is stale; the others are stale exactly when older than the latest expected bar", async ({ api }) => {
  const rows = await json<WatchlistRowDto[]>(await api.reader.get("/api/v1/watchlist"));
  const expected = latestExpectedBar();

  const failed = rows.find((r) => r.ticker === FAILING_TICKER)!;
  expect(failed.asOfDate).toBeNull();
  expect(failed.stale).toBe(true);

  // The canned bars end on 2026-10-09, so they stay fresh only until the next session closes.
  for (const row of rows.filter((r) => r.asOfDate !== null)) {
    expect(row.stale, `${row.ticker} as of ${row.asOfDate}, latest expected ${expected}`).toBe(row.asOfDate! < expected);
  }
});
