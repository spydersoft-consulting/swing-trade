// Trend and Signal are derived on read from the current Settings: a threshold change applies on the next GET with no
// refresh. These specs prove it by changing settings between two watchlist reads.
import type { APIRequestContext } from "@playwright/test";
import { PULLBACK_TICKER } from "./market-data";
import { expect, json, test } from "./fixtures";
import type { SettingsDto, SizerResultDto, WatchlistRowDto } from "./types";

const signals = async (api: { reader: APIRequestContext }) =>
  Object.fromEntries(
    (await json<WatchlistRowDto[]>(await api.reader.get("/api/v1/watchlist"))).map((r) => [r.ticker, r]),
  ) as Record<string, WatchlistRowDto>;

test("narrowing the pullback band turns the Pullback candidate into Watch on the next GET, with no refresh", async ({ api, settingsGuard }) => {
  const before = (await signals(api))[PULLBACK_TICKER];
  expect(before.signal).toBe("PullbackCandidate");
  const runsBefore = (await json<{ totalCount: number }>(await api.reader.get("/api/v1/refresh/runs"))).totalCount;

  // Put the top of the band just under the ticker's % vs 20-day.
  const narrowed: SettingsDto = { ...settingsGuard, pullbackBandHigh: before.pctVs20! / 2 };
  await json(await api.admin.put("/api/v1/settings", { data: narrowed }));

  const after = (await signals(api))[PULLBACK_TICKER];
  expect(after.signal).toBe("Watch");
  expect(after.pctVs20).toBe(before.pctVs20); // same stored data, different thresholds
  expect(after.asOfDate).toBe(before.asOfDate);
  expect((await json<{ totalCount: number }>(await api.reader.get("/api/v1/refresh/runs"))).totalCount, "no refresh ran").toBe(runsBefore);
});

test("widening the band promotes a Watch row to a Pullback candidate", async ({ api, settingsGuard }) => {
  const dia = (await signals(api))["DIA"];
  expect(dia.signal).toBe("Watch");
  expect(dia.pctVs20!).toBeGreaterThan(settingsGuard.pullbackBandHigh);

  await json(await api.admin.put("/api/v1/settings", { data: { ...settingsGuard, pullbackBandHigh: dia.pctVs20! + 0.001 } }));

  const rows = await signals(api);
  expect(rows["DIA"].signal).toBe("PullbackCandidate");
  expect(rows["XLK"].signal).toBe("PullbackCandidate"); // same ratio as DIA
  expect(rows["QQQ"].signal).toBe("PullbackCandidate");
  const ordered = await json<WatchlistRowDto[]>(await api.reader.get("/api/v1/watchlist"));
  expect(ordered.slice(0, 3).map((r) => r.signal)).toEqual(["PullbackCandidate", "PullbackCandidate", "PullbackCandidate"]);
  expect(ordered[3].signal).not.toBe("PullbackCandidate");
});

test("lowering the Extended threshold turns an uptrend row into Extended - wait", async ({ api, settingsGuard }) => {
  const rows = await signals(api);
  const xlk = rows["XLK"];
  expect(xlk.signal).toBe("Watch");

  await json(await api.admin.put("/api/v1/settings", { data: { ...settingsGuard, extendedThreshold: xlk.pctVs20! - 0.001 } }));

  const after = await signals(api);
  expect(after["XLK"].signal).toBe("ExtendedWait");
  expect(after["XLK"].signalText).toBe("Extended - wait");
});

test("changing MinRewardRisk changes the very next sizer verdict and the flags on trades", async ({ api, settingsGuard, tracked }) => {
  const size = async () =>
    json<SizerResultDto>(await api.reader.post("/api/v1/sizer", { data: { entry: 62.82, stop: 61.4, target: 65.9 } }));
  expect((await size()).verdict).toBe("OK - log it"); // reward:risk 2.169
  const trade = await tracked.trade({ ticker: "XLE", shares: 10, entryPrice: 62.82, stopPrice: 61.4, targetPrice: 65.9 });
  expect(trade.rewardRiskBelowMin).toBe(false);

  await json(await api.admin.put("/api/v1/settings", { data: { ...settingsGuard, minRewardRisk: 2.5 } }));

  const strict = await size();
  expect(strict.verdict).toBe("SKIP - reward:risk below minimum");
  expect(strict.isOk).toBe(false);
  expect(strict.maxEntryForMinRewardRisk).toBeCloseTo((65.9 + 2.5 * 61.4) / 3.5, 4);
  expect((await json<{ rewardRiskBelowMin: boolean }>(await api.reader.get(`/api/v1/trades/${trade.id}`))).rewardRiskBelowMin).toBe(true);
});

test("the Settings RiskBasis, tax rate and position cap flow into the next dashboard and sizer", async ({ api, settingsGuard }) => {
  await json(await api.admin.put("/api/v1/settings", { data: { ...settingsGuard, taxSetAsideRate: 0.5, maxPositionPct: 0.25 } }));

  const dashboard = await json<{ taxSetAside: number }>(await api.reader.get("/api/v1/dashboard"));
  const sized = await json<SizerResultDto>(await api.reader.post("/api/v1/sizer", { data: { entry: 62.34, stop: 61.4, target: 65.9 } }));

  expect(dashboard.taxSetAside).toBeCloseTo(26.775, 3); // 53.55 x 50%
  expect(sized.sharesByCap).toBe(8); // floor(2000 x 25% / 62.34)
});
