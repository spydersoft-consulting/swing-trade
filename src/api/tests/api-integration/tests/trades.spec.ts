import { expect, expectFieldErrors, json, test } from "./fixtures";
import type { CreateTradeRequest, TradeDto } from "./types";

const plan = (overrides: Partial<CreateTradeRequest> = {}): CreateTradeRequest => ({
  ticker: "XLK",
  shares: 5,
  entryPrice: 192,
  stopPrice: 187.5,
  targetPrice: 202,
  rationale: "e2e plan",
  ...overrides,
});

test.describe("read", () => {
  test("the seeded XLE trade is Closed with the plan's computed fields", async ({ api }) => {
    const closed = await json<TradeDto[]>(await api.reader.get("/api/v1/trades?status=Closed"));
    const xle = closed.find((t) => t.ticker === "XLE" && t.openDate === "2026-10-05")!;

    expect(xle).toBeDefined();
    expect(xle.position).toBeCloseTo(935.1, 2);
    expect(xle.riskDollars).toBeCloseTo(14.1, 2);
    expect(xle.riskPct).toBeCloseTo(0.00705, 5);
    expect(xle.plannedRewardRisk).toBeCloseTo(3.787, 3);
    expect(xle.pnl).toBeCloseTo(53.55, 2);
    expect(xle.pnlPct).toBeCloseTo(0.05727, 4);
    expect(xle.rMultiple).toBeCloseTo(3.798, 3);
    expect(xle.daysHeld).toBe(7);
    expect(xle.followedPlan).toBe(true);
    expect(xle.washSaleFlag).toBe(false);
    expect(xle.riskOverLimit).toBe(false);
    expect(xle.rewardRiskBelowMin).toBe(false);
  });

  test("GET /trades filters by status and ticker, and GET /trades/{id} returns one", async ({ api, tracked }) => {
    const planned = await tracked.trade(plan());

    const byStatus = await json<TradeDto[]>(await api.reader.get("/api/v1/trades?status=Planned"));
    const byTicker = await json<TradeDto[]>(await api.reader.get("/api/v1/trades?ticker=xlk"));
    const one = await json<TradeDto>(await api.reader.get(`/api/v1/trades/${planned.id}`));

    expect(byStatus.every((t) => t.status === "Planned")).toBe(true);
    expect(byStatus.some((t) => t.id === planned.id)).toBe(true);
    expect(byTicker.every((t) => t.ticker === "XLK")).toBe(true);
    expect(one).toEqual(planned);
  });

  test("an unknown id is 404", async ({ api }) => {
    expect((await api.reader.get("/api/v1/trades/999999")).status()).toBe(404);
  });

  test("a bad status filter is 400", async ({ api }) => {
    expect((await api.reader.get("/api/v1/trades?status=Sideways")).status()).toBe(400);
  });
});

test.describe("create", () => {
  test("POST /trades creates a Planned trade with computed fields and a Location", async ({ api, tracked }) => {
    const response = await api.trader.post("/api/v1/trades", { data: plan({ ticker: "xlk" }) });
    expect(response.status()).toBe(201);
    const trade = (await response.json()) as TradeDto;
    tracked.track(trade.id);

    expect(response.headers()["location"]).toContain(`/api/v1/trades/${trade.id}`);
    expect(trade).toMatchObject({ ticker: "XLK", status: "Planned", setup: "Pullback", shares: 5, openDate: null });
    expect(trade.position).toBeCloseTo(960, 2);
    expect(trade.riskDollars).toBeCloseTo(22.5, 2);
    expect(trade.plannedRewardRisk).toBeCloseTo(2.222, 3);
    expect(trade.pnl).toBeNull();
    expect(trade.rewardRiskBelowMin).toBe(false);
  });

  test("POST /trades creates an Open trade when it has an open date", async ({ tracked }) => {
    const open = await tracked.trade(plan({ status: "Open", openDate: "2026-10-09" }));

    expect(open.status).toBe("Open");
    expect(open.openDate).toBe("2026-10-09");
  });

  test("a trade over the risk limit or under the minimum reward:risk is flagged", async ({ tracked }) => {
    const flagged = await tracked.trade(plan({ shares: 16, entryPrice: 100, stopPrice: 98, targetPrice: 101 }));

    expect(flagged.riskOverLimit).toBe(true); // 16 x 2 = 32 risks 1.6% of 2,000; the limit is 1.5%
    expect(flagged.rewardRiskBelowMin).toBe(true);
  });

  test("validation: 400 with field errors", async ({ api }) => {
    await expectFieldErrors(
      await api.trader.post("/api/v1/trades", { data: plan({ shares: 0, entryPrice: 100, stopPrice: 100 }) }),
      "Shares",
      "EntryPrice",
    );
    await expectFieldErrors(await api.trader.post("/api/v1/trades", { data: plan({ ticker: "NOPE9" }) }), "Ticker");
    await expectFieldErrors(await api.trader.post("/api/v1/trades", { data: plan({ openDate: "2026-10-09" }) }), "OpenDate"); // Planned has no open date
    await expectFieldErrors(await api.trader.post("/api/v1/trades", { data: plan({ status: "Open" }) }), "OpenDate"); // Open needs one
    await expectFieldErrors(await api.trader.post("/api/v1/trades", { data: { ...plan(), status: "Closed" } }), "Status");
    await expectFieldErrors(await api.trader.post("/api/v1/trades", { data: plan({ targetPrice: 0, stopPrice: -1 }) }), "TargetPrice", "StopPrice");
  });
});

test.describe("update and delete", () => {
  test("PUT /trades/{id} edits a planned trade", async ({ api, tracked }) => {
    const planned = await tracked.trade(plan());

    const updated = await json<TradeDto>(
      await api.trader.put(`/api/v1/trades/${planned.id}`, {
        data: { ticker: "XLK", setup: "Breakout", shares: 4, entryPrice: 193, stopPrice: 188, targetPrice: 205, rationale: "edited" },
      }),
    );

    expect(updated).toMatchObject({ id: planned.id, setup: "Breakout", shares: 4, entryPrice: 193, rationale: "edited", status: "Planned" });
    expect(updated.position).toBeCloseTo(772, 2);
  });

  test("PUT validation and not-found", async ({ api, tracked }) => {
    const planned = await tracked.trade(plan());

    await expectFieldErrors(
      await api.trader.put(`/api/v1/trades/${planned.id}`, { data: { ticker: "XLK", setup: "Pullback", shares: 0, entryPrice: 1, stopPrice: 2, targetPrice: 3 } }),
      "Shares",
      "EntryPrice",
    );
    expect(
      (await api.trader.put("/api/v1/trades/999999", { data: { ticker: "XLK", setup: "Pullback", shares: 1, entryPrice: 2, stopPrice: 1, targetPrice: 4 } })).status(),
    ).toBe(404);
  });

  test("DELETE /trades/{id} removes the trade (204), then 404", async ({ api }) => {
    const created = await json<TradeDto>(await api.trader.post("/api/v1/trades", { data: plan() }), 201);

    expect((await api.trader.delete(`/api/v1/trades/${created.id}`)).status()).toBe(204);
    expect((await api.trader.get(`/api/v1/trades/${created.id}`)).status()).toBe(404);
    expect((await api.trader.delete(`/api/v1/trades/${created.id}`)).status()).toBe(404);
  });
});

test.describe("status transitions", () => {
  test("fill: Planned becomes Open at the fill price and date", async ({ api, tracked }) => {
    const planned = await tracked.trade(plan());

    const filled = await json<TradeDto>(
      await api.trader.post(`/api/v1/trades/${planned.id}/fill`, { data: { fillPrice: 191.5, fillDate: "2026-10-09" } }),
    );

    expect(filled).toMatchObject({ status: "Open", entryPrice: 191.5, openDate: "2026-10-09", shares: 5 });
    expect(filled.position).toBeCloseTo(957.5, 2);
  });

  test("fill validation (price must be above the stop) and illegal transitions are rejected", async ({ api, tracked }) => {
    const planned = await tracked.trade(plan());
    await expectFieldErrors(await api.trader.post(`/api/v1/trades/${planned.id}/fill`, { data: { fillPrice: 187, fillDate: "2026-10-09" } }), "FillPrice");
    await expectFieldErrors(await api.trader.post(`/api/v1/trades/${planned.id}/fill`, { data: { fillPrice: 191 } }), "FillDate");

    await json(await api.trader.post(`/api/v1/trades/${planned.id}/fill`, { data: { fillPrice: 191, fillDate: "2026-10-09" } }));
    expect((await api.trader.post(`/api/v1/trades/${planned.id}/fill`, { data: { fillPrice: 191, fillDate: "2026-10-09" } })).status(), "fill an Open trade").toBe(409);
    expect((await api.trader.post(`/api/v1/trades/${planned.id}/cancel`)).status(), "cancel an Open trade").toBe(409);
  });

  test("close: Open becomes Closed with P&L, R and days held", async ({ api, tracked }) => {
    const open = await tracked.trade(plan({ status: "Open", openDate: "2026-10-05", entryPrice: 62.34, stopPrice: 61.4, targetPrice: 65.9, shares: 15, ticker: "XLE" }));

    const closed = await json<TradeDto>(
      await api.trader.post(`/api/v1/trades/${open.id}/close`, {
        data: { exitPrice: 65.91, exitDate: "2026-10-12", followedPlan: false, lessons: "e2e lesson" },
      }),
    );

    expect(closed).toMatchObject({ status: "Closed", exitPrice: 65.91, exitDate: "2026-10-12", followedPlan: false, lessons: "e2e lesson", daysHeld: 7 });
    expect(closed.pnl).toBeCloseTo(53.55, 2);
    expect(closed.rMultiple).toBeCloseTo(3.798, 3);
  });

  test("close validation and illegal transitions", async ({ api, tracked }) => {
    const planned = await tracked.trade(plan());
    expect((await api.trader.post(`/api/v1/trades/${planned.id}/close`, { data: { exitPrice: 200, exitDate: "2026-10-12" } })).status(), "close a Planned trade").toBe(409);

    const open = await tracked.trade(plan({ status: "Open", openDate: "2026-10-09" }));
    await expectFieldErrors(await api.trader.post(`/api/v1/trades/${open.id}/close`, { data: { exitPrice: 0, exitDate: "2026-10-12" } }), "ExitPrice");
    await expectFieldErrors(await api.trader.post(`/api/v1/trades/${open.id}/close`, { data: { exitPrice: 200, exitDate: "2026-10-01" } }), "ExitDate");

    await json(await api.trader.post(`/api/v1/trades/${open.id}/close`, { data: { exitPrice: 200, exitDate: "2026-10-12" } }));
    expect((await api.trader.post(`/api/v1/trades/${open.id}/close`, { data: { exitPrice: 200, exitDate: "2026-10-12" } })).status(), "close a Closed trade").toBe(409);
  });

  test("cancel: Planned becomes Cancelled, and only a Planned trade can be cancelled", async ({ api, tracked }) => {
    const planned = await tracked.trade(plan());

    const cancelled = await json<TradeDto>(await api.trader.post(`/api/v1/trades/${planned.id}/cancel`));

    expect(cancelled.status).toBe("Cancelled");
    expect((await api.trader.post(`/api/v1/trades/${planned.id}/cancel`)).status()).toBe(409);
    expect((await api.trader.post(`/api/v1/trades/${planned.id}/fill`, { data: { fillPrice: 191, fillDate: "2026-10-09" } })).status()).toBe(409);
  });

  test("transitions on an unknown id are 404", async ({ api }) => {
    expect((await api.trader.post("/api/v1/trades/999999/fill", { data: { fillPrice: 1, fillDate: "2026-10-09" } })).status()).toBe(404);
    expect((await api.trader.post("/api/v1/trades/999999/close", { data: { exitPrice: 1, exitDate: "2026-10-09" } })).status()).toBe(404);
    expect((await api.trader.post("/api/v1/trades/999999/cancel")).status()).toBe(404);
  });
});

test.describe("wash sale", () => {
  test("a re-entry within 30 days of a losing exit is flagged; one outside is not; the loser itself is not", async ({ api, tracked }) => {
    const loser = await tracked.trade(plan({ ticker: "XLF", status: "Open", openDate: "2026-09-20", entryPrice: 50, stopPrice: 48, targetPrice: 55, shares: 10 }));
    await json(await api.trader.post(`/api/v1/trades/${loser.id}/close`, { data: { exitPrice: 48.5, exitDate: "2026-10-05" } }));

    const within = await tracked.trade(plan({ ticker: "XLF", status: "Open", openDate: "2026-11-04", entryPrice: 50, stopPrice: 48, targetPrice: 55, shares: 10 }));
    const outside = await tracked.trade(plan({ ticker: "XLF", status: "Open", openDate: "2026-11-05", entryPrice: 50, stopPrice: 48, targetPrice: 55, shares: 10 }));
    const otherTicker = await tracked.trade(plan({ ticker: "XLK", status: "Open", openDate: "2026-10-09" }));

    expect((await json<TradeDto>(await api.reader.get(`/api/v1/trades/${within.id}`))).washSaleFlag).toBe(true);
    expect((await json<TradeDto>(await api.reader.get(`/api/v1/trades/${outside.id}`))).washSaleFlag).toBe(false);
    expect((await json<TradeDto>(await api.reader.get(`/api/v1/trades/${otherTicker.id}`))).washSaleFlag).toBe(false);
    expect((await json<TradeDto>(await api.reader.get(`/api/v1/trades/${loser.id}`))).washSaleFlag).toBe(false);

    // The watchlist shows when XLF may be re-entered: the latest losing exit + 30 days + 1.
    const rows = await json<{ ticker: string; reentryOkAfter: string | null }[]>(await api.reader.get("/api/v1/watchlist"));
    expect(rows.find((r) => r.ticker === "XLF")!.reentryOkAfter).toBe("2026-11-05");
  });
});

test("the role matrix for trade writes: a reader may read but not write", async ({ api, tracked }) => {
  const planned = await tracked.trade(plan());

  expect((await api.reader.get(`/api/v1/trades/${planned.id}`)).status()).toBe(200);
  expect((await api.reader.post("/api/v1/trades", { data: plan() })).status()).toBe(403);
  expect((await api.reader.put(`/api/v1/trades/${planned.id}`, { data: plan() })).status()).toBe(403);
  expect((await api.reader.delete(`/api/v1/trades/${planned.id}`)).status()).toBe(403);
  expect((await api.reader.post(`/api/v1/trades/${planned.id}/cancel`)).status()).toBe(403);
  expect((await api.admin.get(`/api/v1/trades/${planned.id}`)).status(), "the trade survived").toBe(200);
});
