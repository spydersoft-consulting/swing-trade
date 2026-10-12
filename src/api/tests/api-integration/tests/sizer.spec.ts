import { expect, expectFieldErrors, json, test } from "./fixtures";
import type { SizerResultDto } from "./types";

const size = async (api: { reader: import("@playwright/test").APIRequestContext }, entry: number, stop: number, target: number, ticker?: string) =>
  json<SizerResultDto>(await api.reader.post("/api/v1/sizer", { data: { entry, stop, target, ticker } }));

// The plan's fixtures (default settings, seeded cash of 2,053.55): entry, stop, target, shares, cost, risk $, reward:risk, max entry, verdict.
const FIXTURES: [string, number, number, number, number, number, number, number, number, string][] = [
  ["XLE plan", 62.82, 61.4, 65.9, 15, 942.3, 21.3, 2.169, 62.9, "OK - log it"],
  ["SPY, one share", 769.64, 753.5, 773.5, 1, 769.64, 16.14, 0.239, 760.17, "SKIP - reward:risk below minimum"],
  ["XLK buy the dip", 192, 187.5, 202, 5, 960, 22.5, 2.222, 192.33, "OK - log it"],
  ["XLK at limit", 196.3, 193.5, 202, 5, 981.5, 14, 2.036, 196.33, "OK - log it"],
  ["XLK just too high", 196.6, 193.5, 202, 5, 983, 15.5, 1.742, 196.33, "SKIP - reward:risk below minimum"],
  ["Cap sets the size", 62.34, 61.4, 65.9, 16, 997.44, 15.04, 3.787, 62.9, "OK - log it"],
];

for (const [name, entry, stop, target, shares, cost, risk, rr, maxEntry, verdict] of FIXTURES) {
  test(`plan fixture: ${name}`, async ({ api }) => {
    const r = await size(api, entry, stop, target);

    expect(r.shares).toBe(shares);
    expect(r.positionCost).toBeCloseTo(cost, 2);
    expect(r.riskDollars).toBeCloseTo(risk, 2);
    expect(r.rewardRisk).toBeCloseTo(rr, 3);
    expect(r.maxEntryForMinRewardRisk).toBeCloseTo(maxEntry, 2);
    expect(r.verdict).toBe(verdict);
    expect(r.isOk).toBe(verdict === "OK - log it");
  });
}

test("the response carries the breakdown by risk, cap and cash, and the capital used", async ({ api }) => {
  const r = await size(api, 62.34, 61.4, 65.9, "xle");

  expect(r.ticker).toBe("XLE");
  expect(r.capital).toBe(2000);
  expect(r.riskBudget).toBeCloseTo(30, 6);
  expect(r.riskPerShare).toBeCloseTo(0.94, 6);
  expect(r.sharesByRisk).toBe(31); // floor(30 / 0.94)
  expect(r.sharesByCap).toBe(16); // floor(1000 / 62.34)
  expect(r.sharesByCash).toBe(32); // floor(2053.55 / 62.34): the seeded XLE trade made 53.55
  expect(r.availableCash).toBeCloseTo(2053.55, 2);
  expect(r.riskPct).toBeCloseTo(15.04 / 2000, 6);
  expect(r.openCount).toBe(0);
  expect(r.warning).toBeNull();
});

test("cash sets the size: a planned order reserves cash, leaving 500 -> 8 shares", async ({ api, tracked }) => {
  // 1 share at 1,553.55 reserves exactly enough to leave 2,053.55 - 1,553.55 = 500 available.
  await tracked.trade({ ticker: "SPY", shares: 1, entryPrice: 1553.55, stopPrice: 1500, targetPrice: 1700 });

  const r = await size(api, 62.34, 61.4, 65.9);

  expect(r.availableCash).toBeCloseTo(500, 2);
  expect(r.sharesByCash).toBe(8);
  expect(r.shares).toBe(8);
  expect(r.verdict).toBe("OK - log it");
});

test("with no cash left the verdict is SKIP - not enough cash", async ({ api, tracked }) => {
  await tracked.trade({ ticker: "SPY", shares: 1, entryPrice: 2050, stopPrice: 2000, targetPrice: 2200 });

  const r = await size(api, 62.34, 61.4, 65.9);

  expect(r.shares).toBe(0);
  expect(r.verdict).toBe("SKIP - not enough cash");
});

test("stop at or above entry is the verdict 'Stop must be below entry', not an error", async ({ api }) => {
  for (const stop of [62.82, 70]) {
    const r = await size(api, 62.82, stop, 80);
    expect(r.verdict).toBe("Stop must be below entry");
    expect(r.shares).toBe(0);
    expect(r.rewardRisk).toBeNull();
    expect(r.isOk).toBe(false);
  }
});

test("three open positions add a warning but do not change the verdict", async ({ api, tracked }) => {
  for (let i = 0; i < 3; i++) {
    await tracked.trade({ ticker: "XLF", status: "Open", openDate: "2026-10-09", shares: 1, entryPrice: 50, stopPrice: 49, targetPrice: 53 });
  }

  const r = await size(api, 62.34, 61.4, 65.9);

  expect(r.openCount).toBe(3);
  expect(r.warning).toContain("3 open position");
  expect(r.verdict).toBe("OK - log it");
  // The three open positions also take their cost out of the available cash.
  expect(r.availableCash).toBeCloseTo(2053.55 - 150, 2);
});

test("the sizer is a pure calculation: it saves nothing", async ({ api }) => {
  const before = await json<unknown[]>(await api.reader.get("/api/v1/trades"));

  await size(api, 62.82, 61.4, 65.9, "XLE");

  expect(await json<unknown[]>(await api.reader.get("/api/v1/trades"))).toHaveLength(before.length);
});

test("validation: non-positive prices and an unknown ticker are 400 field errors", async ({ api }) => {
  await expectFieldErrors(await api.reader.post("/api/v1/sizer", { data: { entry: 0, stop: -1, target: 0 } }), "Entry", "Stop", "Target");
  await expectFieldErrors(await api.reader.post("/api/v1/sizer", { data: { entry: 10, stop: 9, target: 12, ticker: "NOPE9" } }), "Ticker");
  expect((await api.reader.post("/api/v1/sizer", { data: "not json", headers: { "content-type": "application/json" } })).status()).toBe(400);
});
