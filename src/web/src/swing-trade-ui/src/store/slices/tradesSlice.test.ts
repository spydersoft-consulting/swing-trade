import { AxiosError, AxiosHeaders } from "axios";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { makeStore } from "../store";
import { cancelTrade, closeTrade, createTrade, fetchTrades, fillTrade, updateTrade } from "./tradesSlice";
import { swingTradeApi } from "../../api/swingTradeApi";
import { trade } from "../../test/utils";

vi.mock("../../api/swingTradeApi", () => ({
  swingTradeApi: {
    trades: vi.fn(),
    createTrade: vi.fn(),
    updateTrade: vi.fn(),
    fillTrade: vi.fn(),
    closeTrade: vi.fn(),
    cancelTrade: vi.fn(),
  },
}));

const api = vi.mocked(swingTradeApi);
const httpError = (status: number, data?: unknown) =>
  new AxiosError("x", "E", undefined, undefined, {
    status,
    statusText: "",
    data,
    headers: {},
    config: { headers: new AxiosHeaders() },
  });

beforeEach(() => vi.resetAllMocks());

describe("tradesSlice", () => {
  it("loads trades with the server's computed fields untouched", async () => {
    api.trades.mockResolvedValue([trade()]);
    const store = makeStore();

    await store.dispatch(fetchTrades());

    const [t] = store.getState().trades.items;
    expect(store.getState().trades.status).toBe("succeeded");
    expect(t.pnl).toBe(53.55);
    expect(t.rMultiple).toBe(3.797872);
    expect(t.riskPct).toBe(0.00705);
  });

  it("records a failed load with its problem", async () => {
    api.trades.mockRejectedValue(httpError(500));
    const store = makeStore();

    await store.dispatch(fetchTrades());

    expect(store.getState().trades.status).toBe("failed");
    expect(store.getState().trades.error?.status).toBe(500);
  });

  it("adds a created trade to the front and replaces a changed trade in place", async () => {
    api.trades.mockResolvedValue([trade({ id: 1 }), trade({ id: 2, status: "Planned" })]);
    const store = makeStore();
    await store.dispatch(fetchTrades());

    api.createTrade.mockResolvedValue(trade({ id: 3, status: "Planned", ticker: "QQQ" }));
    await store.dispatch(
      createTrade({ ticker: "QQQ", status: "Planned", shares: 1, entryPrice: 10, stopPrice: 9, targetPrice: 13 }),
    );
    expect(store.getState().trades.items.map((t) => t.id)).toEqual([3, 1, 2]);

    api.fillTrade.mockResolvedValue(trade({ id: 2, status: "Open", entryPrice: 62.9 }));
    await store.dispatch(fillTrade({ id: 2, body: { fillPrice: 62.9, fillDate: "2026-10-12" } }));
    expect(store.getState().trades.items.find((t) => t.id === 2)).toMatchObject({ status: "Open", entryPrice: 62.9 });

    api.closeTrade.mockResolvedValue(trade({ id: 2, status: "Closed", pnl: -4 }));
    await store.dispatch(closeTrade({ id: 2, body: { exitPrice: 60, exitDate: "2026-10-13" } }));
    expect(store.getState().trades.items.find((t) => t.id === 2)?.pnl).toBe(-4);

    api.cancelTrade.mockResolvedValue(trade({ id: 3, status: "Cancelled" }));
    await store.dispatch(cancelTrade(3));
    expect(store.getState().trades.items.find((t) => t.id === 3)?.status).toBe("Cancelled");

    api.updateTrade.mockResolvedValue(trade({ id: 1, rationale: "edited" }));
    await store.dispatch(
      updateTrade({
        id: 1,
        body: { ticker: "XLE", shares: 15, entryPrice: 62.34, stopPrice: 61.4, targetPrice: 65.9 },
      }),
    );
    expect(store.getState().trades.items.find((t) => t.id === 1)?.rationale).toBe("edited");
    expect(store.getState().trades.items).toHaveLength(3);
  });

  it("surfaces a 409 from an illegal transition and leaves the trade as it was", async () => {
    api.trades.mockResolvedValue([trade({ id: 2, status: "Open" })]);
    const store = makeStore();
    await store.dispatch(fetchTrades());
    api.fillTrade.mockRejectedValue(
      httpError(409, { title: "Conflict", detail: "Only a Planned trade can be filled." }),
    );

    const result = await store.dispatch(fillTrade({ id: 2, body: { fillPrice: 1, fillDate: "2026-10-12" } }));

    expect(fillTrade.rejected.match(result)).toBe(true);
    expect(result.payload).toMatchObject({ status: 409, message: "Only a Planned trade can be filled." });
    expect(store.getState().trades.items[0].status).toBe("Open");
  });

  it("returns 400 field errors keyed by the form's field names", async () => {
    api.createTrade.mockRejectedValue(
      httpError(400, {
        errors: { EntryPrice: ["Entry must be above the stop."], Shares: ["Shares must be at least 1."] },
      }),
    );
    const store = makeStore();

    const result = await store.dispatch(
      createTrade({ ticker: "XLE", shares: 0, entryPrice: 1, stopPrice: 2, targetPrice: 3 }),
    );

    expect(result.payload).toMatchObject({
      status: 400,
      fieldErrors: { entryPrice: "Entry must be above the stop.", shares: "Shares must be at least 1." },
    });
  });
});
