import { EXPECTED, FAILING_TICKER, LAST_CANNED_BAR } from "./market-data";
import { expect, expectFieldErrors, json, test } from "./fixtures";
import type { BarsDto, WatchlistRowDto } from "./types";

test("GET /instruments/{ticker}/bars returns completed closes oldest first with MA20 and MA50 series", async ({ api }) => {
  const bars = await json<BarsDto>(await api.reader.get("/api/v1/instruments/QQQ/bars?days=120"));

  expect(bars.ticker).toBe("QQQ");
  expect(bars.bars).toHaveLength(EXPECTED.QQQ.bars);
  const dates = bars.bars.map((b) => b.date);
  expect(dates).toEqual([...dates].sort());
  expect(bars.bars[bars.bars.length - 1].date).toBe(LAST_CANNED_BAR);
});

test("an average is null until enough bars exist, then equals the plain mean of the closes", async ({ api }) => {
  const { bars } = await json<BarsDto>(await api.reader.get("/api/v1/instruments/QQQ/bars"));
  const closes = bars.map((b) => b.close);
  const mean = (end: number, n: number) => closes.slice(end - n + 1, end + 1).reduce((a, b) => a + b, 0) / n;

  expect(bars.slice(0, 19).every((b) => b.ma20 === null)).toBe(true);
  expect(bars.slice(0, 49).every((b) => b.ma50 === null)).toBe(true);
  for (const i of [19, 30, 49, 60, bars.length - 1]) {
    expect(bars[i].ma20, `MA20 at ${i}`).toBeCloseTo(mean(i, 20), 3);
  }
  for (const i of [49, 60, bars.length - 1]) {
    expect(bars[i].ma50, `MA50 at ${i}`).toBeCloseTo(mean(i, 50), 3);
  }
});

test("the last point of the series matches the watchlist row", async ({ api }) => {
  const rows = await json<WatchlistRowDto[]>(await api.reader.get("/api/v1/watchlist"));
  for (const ticker of ["QQQ", "SPY", "GLD"]) {
    const row = rows.find((r) => r.ticker === ticker)!;
    const { bars } = await json<BarsDto>(await api.reader.get(`/api/v1/instruments/${ticker}/bars`));
    const last = bars[bars.length - 1];

    expect(last.date, ticker).toBe(row.asOfDate);
    expect(last.close, ticker).toBeCloseTo(row.last!, 4);
    expect(last.ma20, ticker).toBeCloseTo(row.ma20!, 4);
    expect(last.ma50, ticker).toBeCloseTo(row.ma50!, 4);
  }
});

test("days limits the window, and is capped at 120", async ({ api }) => {
  const ten = await json<BarsDto>(await api.reader.get("/api/v1/instruments/QQQ/bars?days=10"));
  expect(ten.bars).toHaveLength(10);
  expect(ten.days).toBe(10);
  expect(ten.bars[0].ma50, "history before the window still feeds the averages").not.toBeNull();

  const capped = await json<BarsDto>(await api.reader.get("/api/v1/instruments/QQQ/bars?days=5000"));
  expect(capped.days).toBe(120);
  expect(capped.bars.length).toBeLessThanOrEqual(120);
});

test("a ticker with fewer than 50 bars has no MA50 anywhere", async ({ api }) => {
  const { bars } = await json<BarsDto>(await api.reader.get("/api/v1/instruments/XLI/bars"));

  expect(bars).toHaveLength(EXPECTED.XLI.bars);
  expect(bars.every((b) => b.ma50 === null)).toBe(true);
  expect(bars[bars.length - 1].ma20).not.toBeNull();
});

test("the ticker is case-insensitive; an instrument whose load failed has no bars", async ({ api }) => {
  const lower = await json<BarsDto>(await api.reader.get("/api/v1/instruments/qqq/bars?days=5"));
  expect(lower.ticker).toBe("QQQ");

  const failed = await json<BarsDto>(await api.reader.get(`/api/v1/instruments/${FAILING_TICKER}/bars`));
  expect(failed.bars).toEqual([]);
});

test("unknown ticker is 404 and days below one is 400", async ({ api }) => {
  expect((await api.reader.get("/api/v1/instruments/NOPE9/bars")).status()).toBe(404);
  await expectFieldErrors(await api.reader.get("/api/v1/instruments/QQQ/bars?days=0"), "Days");
});
