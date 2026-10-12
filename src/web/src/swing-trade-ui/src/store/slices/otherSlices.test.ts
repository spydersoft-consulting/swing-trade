import { AxiosError, AxiosHeaders } from "axios";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { makeStore } from "../store";
import { fetchDashboard } from "./dashboardSlice";
import { createNote, fetchNotes } from "./notesSlice";
import { fetchMcp, fetchSettings, saveSettings } from "./settingsSlice";
import { swingTradeApi } from "../../api/swingTradeApi";
import { dashboard, settings } from "../../test/utils";

vi.mock("../../api/swingTradeApi", () => ({
  swingTradeApi: {
    dashboard: vi.fn(),
    notes: vi.fn(),
    createNote: vi.fn(),
    settings: vi.fn(),
    updateSettings: vi.fn(),
    mcpInfo: vi.fn(),
    mcpCalls: vi.fn(),
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

describe("dashboardSlice", () => {
  it("keeps null metrics as null (no closed trades, no SPY data)", async () => {
    api.dashboard.mockResolvedValue(
      dashboard({
        closedCount: 0,
        winRate: null,
        avgWin: null,
        avgR: null,
        spyReturnPct: null,
        youMinusSpy: null,
        equityCurve: [],
        bySetup: {},
      }),
    );
    const store = makeStore();

    await store.dispatch(fetchDashboard());

    const data = store.getState().dashboard.data!;
    expect(data.winRate).toBeNull();
    expect(data.spyReturnPct).toBeNull();
    expect(data.equityCurve).toEqual([]);
  });

  it("records a failure", async () => {
    api.dashboard.mockRejectedValue(httpError(500));
    const store = makeStore();
    await store.dispatch(fetchDashboard());
    expect(store.getState().dashboard.status).toBe("failed");
  });
});

describe("notesSlice", () => {
  it("loads notes and puts a new note first", async () => {
    api.notes.mockResolvedValue([{ id: 1, date: "2026-10-05", ticker: null, kind: "General", text: "old" }]);
    const store = makeStore();
    await store.dispatch(fetchNotes());

    api.createNote.mockResolvedValue({
      id: 2,
      date: "2026-10-12",
      ticker: null,
      kind: "Pass",
      text: "no clean pullback",
    });
    await store.dispatch(createNote({ date: "2026-10-12", kind: "Pass", text: "no clean pullback" }));

    expect(store.getState().notes.items.map((n) => n.id)).toEqual([2, 1]);
  });

  it("returns the 400 field errors for a bad note", async () => {
    api.createNote.mockRejectedValue(httpError(400, { errors: { Text: ["Text is required."] } }));
    const store = makeStore();

    const result = await store.dispatch(createNote({ date: "2026-10-12", kind: "Pass", text: "" }));

    expect(result.payload).toMatchObject({ fieldErrors: { text: "Text is required." } });
  });
});

describe("settingsSlice", () => {
  it("loads and saves settings", async () => {
    api.settings.mockResolvedValue(settings());
    const store = makeStore();
    await store.dispatch(fetchSettings());
    expect(store.getState().settings.settings?.maxRiskPct).toBe(0.015);

    api.updateSettings.mockResolvedValue(settings({ pullbackBandHigh: 0.03 }));
    await store.dispatch(saveSettings(settings({ pullbackBandHigh: 0.03 })));
    expect(store.getState().settings.settings?.pullbackBandHigh).toBe(0.03);
  });

  it("surfaces a 403 when a non-admin tries to save", async () => {
    api.updateSettings.mockRejectedValue(httpError(403));
    const store = makeStore();

    const result = await store.dispatch(saveSettings(settings()));

    expect(saveSettings.rejected.match(result)).toBe(true);
    expect(result.payload).toMatchObject({ status: 403 });
  });

  it("loads the MCP panel (info and the last 20 calls) together", async () => {
    api.mcpInfo.mockResolvedValue({
      endpointUrl: "https://swing.example/mcp",
      ownerSubject: "owner-1",
      addNoteEnabled: false,
      audience: "swingtrade",
      requiredRole: "swingtrade.mcp",
      addNoteRole: "swingtrade.trader",
    });
    api.mcpCalls.mockResolvedValue([
      {
        id: 1,
        at: "2026-10-12T14:00:00Z",
        tool: "get_watchlist",
        arguments: "{}",
        durationMs: 12,
        caller: "owner-1",
        status: "ok",
      },
    ]);
    const store = makeStore();

    await store.dispatch(fetchMcp());

    expect(api.mcpCalls).toHaveBeenCalledWith(20);
    expect(store.getState().settings.mcpInfo?.endpointUrl).toBe("https://swing.example/mcp");
    expect(store.getState().settings.mcpCalls).toHaveLength(1);
  });

  it("keeps the MCP panel's failure separate from the settings", async () => {
    api.mcpInfo.mockRejectedValue(httpError(403));
    api.mcpCalls.mockRejectedValue(httpError(403));
    const store = makeStore();

    await store.dispatch(fetchMcp());

    expect(store.getState().settings.mcpStatus).toBe("failed");
    expect(store.getState().settings.status).toBe("idle");
  });
});
