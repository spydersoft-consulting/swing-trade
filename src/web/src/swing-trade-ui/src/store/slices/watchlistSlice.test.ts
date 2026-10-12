import { AxiosError, AxiosHeaders } from "axios";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { makeStore } from "../store";
import {
  addInstrument,
  deactivateInstrument,
  fetchLastRun,
  fetchWatchlist,
  runRefresh,
  setRefreshPollInterval,
} from "./watchlistSlice";
import { swingTradeApi } from "../../api/swingTradeApi";
import { row } from "../../test/utils";
import type { RefreshRunDto } from "../../api/generated/types.gen";

vi.mock("../../api/swingTradeApi", () => ({
  swingTradeApi: {
    watchlist: vi.fn(),
    refreshRuns: vi.fn(),
    refreshRun: vi.fn(),
    startRefresh: vi.fn(),
    instruments: vi.fn(),
    createInstrument: vi.fn(),
    updateInstrument: vi.fn(),
  },
}));

const api = vi.mocked(swingTradeApi);

const run = (overrides: Partial<RefreshRunDto> = {}): RefreshRunDto => ({
  id: 7,
  startedAt: "2026-10-12T21:30:00Z",
  finishedAt: null,
  provider: "alpaca",
  status: "Running",
  tickersOk: 0,
  tickersFailed: 0,
  message: null,
  ...overrides,
});

const httpError = (status: number, data?: unknown) =>
  new AxiosError("x", "E", undefined, undefined, {
    status,
    statusText: "",
    data,
    headers: {},
    config: { headers: new AxiosHeaders() },
  });

beforeEach(() => {
  vi.resetAllMocks();
  setRefreshPollInterval(1);
});

describe("watchlistSlice", () => {
  it("loads the rows in the order the API returned them", async () => {
    const rows = [row({ ticker: "QQQ" }), row({ ticker: "SPY", signal: "Watch", signalText: "Watch" })];
    api.watchlist.mockResolvedValue(rows);
    const store = makeStore();

    const pending = store.dispatch(fetchWatchlist());
    expect(store.getState().watchlist.status).toBe("loading");
    await pending;

    expect(store.getState().watchlist.status).toBe("succeeded");
    expect(store.getState().watchlist.rows.map((r) => r.ticker)).toEqual(["QQQ", "SPY"]);
  });

  it("keeps a readable problem when loading fails (403 stays a 403)", async () => {
    api.watchlist.mockRejectedValue(httpError(403));
    const store = makeStore();

    await store.dispatch(fetchWatchlist());

    const state = store.getState().watchlist;
    expect(state.status).toBe("failed");
    expect(state.error?.status).toBe(403);
    expect(state.error?.message).toContain("role");
  });

  it("stores the latest refresh run, or none", async () => {
    api.refreshRuns.mockResolvedValue({
      items: [run({ status: "Partial", tickersOk: 11, tickersFailed: 1 })],
      totalCount: 1,
      page: 1,
      pageSize: 1,
    });
    const store = makeStore();
    await store.dispatch(fetchLastRun());
    expect(store.getState().watchlist.lastRun?.status).toBe("Partial");

    api.refreshRuns.mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 1 });
    await store.dispatch(fetchLastRun());
    expect(store.getState().watchlist.lastRun).toBeNull();
  });

  it("runRefresh starts a run, polls until it leaves Running, then reloads the watchlist and the last run", async () => {
    api.startRefresh.mockResolvedValue(run());
    api.refreshRun
      .mockResolvedValueOnce(run())
      .mockResolvedValueOnce(run({ status: "Succeeded", finishedAt: "2026-10-12T21:31:00Z", tickersOk: 12 }));
    api.watchlist.mockResolvedValue([row()]);
    api.refreshRuns.mockResolvedValue({
      items: [run({ status: "Succeeded", tickersOk: 12 })],
      totalCount: 1,
      page: 1,
      pageSize: 1,
    });
    const store = makeStore();

    const result = await store.dispatch(runRefresh());

    expect(runRefresh.fulfilled.match(result)).toBe(true);
    expect(api.refreshRun).toHaveBeenCalledTimes(2);
    expect(api.refreshRun).toHaveBeenCalledWith(7);
    expect(api.watchlist).toHaveBeenCalledTimes(1);
    expect(store.getState().watchlist.refreshPhase).toBe("succeeded");
    expect(store.getState().watchlist.lastRun?.status).toBe("Succeeded");
    expect(store.getState().watchlist.rows).toHaveLength(1);
  });

  it("treats a Failed run as a failed refresh phase but still reloads", async () => {
    api.startRefresh.mockResolvedValue(run({ status: "Failed", message: "provider down" }));
    api.watchlist.mockResolvedValue([]);
    api.refreshRuns.mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 1 });
    const store = makeStore();

    await store.dispatch(runRefresh());

    expect(store.getState().watchlist.refreshPhase).toBe("failed");
    expect(api.refreshRun).not.toHaveBeenCalled();
  });

  it("reports 409 when a refresh is already running", async () => {
    api.startRefresh.mockRejectedValue(httpError(409, { detail: "A refresh is already running." }));
    const store = makeStore();

    const result = await store.dispatch(runRefresh());

    expect(runRefresh.rejected.match(result)).toBe(true);
    expect(store.getState().watchlist.refreshError?.status).toBe(409);
    expect(store.getState().watchlist.refreshPhase).toBe("failed");
  });

  it("addInstrument posts the request and reloads; a 400 comes back as field errors", async () => {
    api.createInstrument.mockResolvedValue({
      ticker: "XLP",
      name: "Staples",
      category: null,
      active: true,
      isBenchmark: false,
      washSaleGroup: null,
    });
    api.watchlist.mockResolvedValue([row({ ticker: "XLP" })]);
    const store = makeStore();
    const ok = await store.dispatch(addInstrument({ ticker: "XLP", name: "Staples", active: true }));
    expect(addInstrument.fulfilled.match(ok)).toBe(true);
    expect(store.getState().watchlist.rows.map((r) => r.ticker)).toEqual(["XLP"]);

    api.createInstrument.mockRejectedValue(httpError(400, { errors: { Name: ["Name is required."] } }));
    const bad = await store.dispatch(addInstrument({ ticker: "XLB", name: "", active: true }));
    expect(addInstrument.rejected.match(bad)).toBe(true);
    expect(bad.payload).toMatchObject({ status: 400, fieldErrors: { name: "Name is required." } });
  });

  it("deactivateInstrument reads the instrument first so the PUT keeps its other fields", async () => {
    api.instruments.mockResolvedValue([
      {
        ticker: "SPY",
        name: "SPDR S&P 500",
        category: "Broad",
        active: true,
        isBenchmark: true,
        washSaleGroup: "SP500",
      },
    ]);
    api.updateInstrument.mockResolvedValue({
      ticker: "SPY",
      name: "SPDR S&P 500",
      category: "Broad",
      active: false,
      isBenchmark: true,
      washSaleGroup: "SP500",
    });
    api.watchlist.mockResolvedValue([]);
    const store = makeStore();

    await store.dispatch(deactivateInstrument("SPY"));

    expect(api.updateInstrument).toHaveBeenCalledWith("SPY", {
      name: "SPDR S&P 500",
      category: "Broad",
      active: false,
      isBenchmark: true,
      washSaleGroup: "SP500",
    });
  });

  it("deactivateInstrument reports an unknown ticker", async () => {
    api.instruments.mockResolvedValue([]);
    const store = makeStore();

    const result = await store.dispatch(deactivateInstrument("NOPE"));

    expect(deactivateInstrument.rejected.match(result)).toBe(true);
    expect(result.payload).toMatchObject({ status: 404 });
    expect(api.updateInstrument).not.toHaveBeenCalled();
  });
});
