import { expect, json, test } from "./fixtures";
import type { BarsDto, DashboardDto } from "./types";

test("the seeded dashboard matches the closed-trade fixture", async ({ api }) => {
  const d = await json<DashboardDto>(await api.reader.get("/api/v1/dashboard"));

  expect(d.realizedPnl).toBeCloseTo(53.55, 2);
  expect(d.accountValue).toBeCloseTo(2053.55, 2);
  expect(d.returnPct).toBeCloseTo(0.026775, 6); // 2.6775%, shown as 2.678%
  expect(d.taxSetAside).toBeCloseTo(14.46, 2);
  expect(d.afterTaxPnl).toBeCloseTo(39.09, 2);
  expect(d.closedCount).toBe(1);
  expect(d.winRate).toBe(1);
  expect(d.avgR).toBeCloseTo(3.8, 1);
  expect(d.avgWin).toBeCloseTo(53.55, 2);
  expect(d.avgLoss).toBeNull();
  expect(d.profitFactor).toBeNull(); // no losses
  expect(d.planAdherence).toBe(1);
  expect(d.avgDaysHeld).toBe(7);
  expect(d.openCount).toBe(0);
  expect(d.openCountOverLimit).toBe(false);
  expect(d.capitalAtWork).toBe(0);
  expect(d.riskAtStops).toBe(0);
  expect(d.reservedByPlanned).toBe(0);
  expect(d.availableCash).toBeCloseTo(2053.55, 2);
});

test("results by setup and the equity curve", async ({ api }) => {
  const d = await json<DashboardDto>(await api.reader.get("/api/v1/dashboard"));

  const pullback = Object.entries(d.bySetup).find(([k]) => k.toLowerCase() === "pullback")![1];
  expect(pullback.trades).toBe(1);
  expect(pullback.winRate).toBe(1);
  expect(pullback.netPnl).toBeCloseTo(53.55, 2);

  const last = d.equityCurve[d.equityCurve.length - 1];
  expect(last.date).toBe("2026-10-12");
  expect(last.accountValue).toBeCloseTo(2053.55, 2);
});

test("the SPY comparison comes from the stored SPY bars (loaded by the refresh)", async ({ api }) => {
  const d = await json<DashboardDto>(await api.reader.get("/api/v1/dashboard"));
  const { bars } = await json<BarsDto>(await api.reader.get("/api/v1/instruments/SPY/bars?days=120"));
  // The first trade opened 2026-10-05; the benchmark close on that date and the latest close.
  const atOpen = bars.filter((b) => b.date <= "2026-10-05").pop()!.close;
  const latest = bars[bars.length - 1].close;
  const spyReturn = latest / atOpen - 1;

  expect(d.spyReturnPct).not.toBeNull();
  expect(d.spyReturnPct!).toBeCloseTo(spyReturn, 5);
  expect(d.youMinusSpy!).toBeCloseTo(d.returnPct - spyReturn, 5);
});

test("a planned order reserves cash and an open trade counts toward capital at work and risk at stops", async ({ api, tracked }) => {
  await tracked.trade({ ticker: "XLK", shares: 5, entryPrice: 192, stopPrice: 187.5, targetPrice: 202 }); // planned: 960
  await tracked.trade({ ticker: "XLF", status: "Open", openDate: "2026-10-09", shares: 10, entryPrice: 50, stopPrice: 48, targetPrice: 55 });

  const d = await json<DashboardDto>(await api.reader.get("/api/v1/dashboard"));

  expect(d.openCount).toBe(1);
  expect(d.capitalAtWork).toBeCloseTo(500, 2);
  expect(d.capitalAtWorkPct).toBeCloseTo(0.25, 6);
  expect(d.riskAtStops).toBeCloseTo(20, 2);
  expect(d.riskAtStopsPct).toBeCloseTo(0.01, 6);
  expect(d.reservedByPlanned).toBeCloseTo(960, 2);
  expect(d.availableCash).toBeCloseTo(2053.55 - 500 - 960, 2);
  expect(d.realizedPnl, "open and planned trades do not change realized P&L").toBeCloseTo(53.55, 2);
});

test("expectancy and drawdown are not returned (the plan gives no formula)", async ({ api }) => {
  const d = (await json<Record<string, unknown>>(await api.reader.get("/api/v1/dashboard")));

  expect(Object.keys(d).map((k) => k.toLowerCase())).not.toContain("expectancy");
  expect(Object.keys(d).map((k) => k.toLowerCase())).not.toContain("drawdown");
});
