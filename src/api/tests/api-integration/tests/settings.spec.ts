import { expect, expectFieldErrors, json, test } from "./fixtures";
import type { SettingsDto, SizerResultDto } from "./types";

const DEFAULTS: SettingsDto = {
  startingCapital: 2000,
  maxRiskPct: 0.015,
  maxOpenPositions: 3,
  minRewardRisk: 2,
  taxSetAsideRate: 0.27,
  washSaleWindowDays: 30,
  maxPositionPct: 0.5,
  pullbackBandLow: -0.01,
  pullbackBandHigh: 0.02,
  extendedThreshold: 0.05,
  riskBasis: "StartingCapital",
};

test("GET /settings returns the seeded defaults to a reader", async ({ api }) => {
  const settings = await json<SettingsDto>(await api.reader.get("/api/v1/settings"));

  expect(settings).toEqual(DEFAULTS);
});

test("PUT /settings as admin changes the row and the next GET returns it", async ({ api, settingsGuard }) => {
  const update = { ...settingsGuard, maxOpenPositions: 5, taxSetAsideRate: 0.3 };

  const put = await json<SettingsDto>(await api.admin.put("/api/v1/settings", { data: update }));
  const get = await json<SettingsDto>(await api.reader.get("/api/v1/settings"));

  expect(put).toEqual(update);
  expect(get).toEqual(update);
});

test("PUT /settings rejects invalid values with 400 field errors", async ({ api, settingsGuard }) => {
  const response = await api.admin.put("/api/v1/settings", {
    data: { ...settingsGuard, startingCapital: 0, maxRiskPct: 2, maxOpenPositions: 0, minRewardRisk: -1, pullbackBandLow: 0.5 },
  });

  await expectFieldErrors(response, "StartingCapital", "MaxRiskPct", "MaxOpenPositions", "MinRewardRisk", "PullbackBandLow");
  expect(await json<SettingsDto>(await api.reader.get("/api/v1/settings"))).toEqual(settingsGuard);
});

test("PUT /settings is admin only", async ({ api, settingsGuard }) => {
  for (const persona of ["reader", "trader"] as const) {
    const response = await api[persona].put("/api/v1/settings", { data: { ...settingsGuard, maxOpenPositions: 9 } });
    expect(response.status(), persona).toBe(403);
  }
  expect((await json<SettingsDto>(await api.reader.get("/api/v1/settings"))).maxOpenPositions).toBe(settingsGuard.maxOpenPositions);
});

test("a settings change is used by the very next sizer call", async ({ api, settingsGuard }) => {
  await json(await api.admin.put("/api/v1/settings", { data: { ...settingsGuard, maxRiskPct: 0.03 } }));

  const sized = await json<SizerResultDto>(
    await api.reader.post("/api/v1/sizer", { data: { entry: 62.82, stop: 61.4, target: 65.9 } }),
  );

  expect(sized.riskBudget).toBeCloseTo(60, 6);
  expect(sized.sharesByRisk).toBe(42); // floor(60 / 1.42)
  expect(sized.shares).toBe(15); // the 50% position cap, floor(1000 / 62.82), now binds
});
