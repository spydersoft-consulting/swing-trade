import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AxiosError, AxiosHeaders } from "axios";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { Settings } from "./Settings";
import { DEFAULT_SETTINGS, FIELDS, toFormValues, toRequest } from "./settingsFields";
import { swingTradeApi } from "../../api/swingTradeApi";
import { renderScreen, settings, type Persona } from "../../test/utils";

vi.mock("../../api/swingTradeApi", () => ({
  swingTradeApi: { settings: vi.fn(), updateSettings: vi.fn(), mcpInfo: vi.fn(), mcpCalls: vi.fn() },
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

const renderSettings = (persona: Persona = "admin") => renderScreen(<Settings />, { persona });
const field = (name: RegExp) => screen.getByLabelText(name);

beforeEach(() => {
  vi.resetAllMocks();
  api.settings.mockResolvedValue(settings());
  api.mcpInfo.mockResolvedValue({
    endpointUrl: "https://swing.example/mcp",
    ownerSubject: "owner-sub-1",
    addNoteEnabled: false,
    audience: "swingtrade",
    requiredRole: "swingtrade.mcp",
    addNoteRole: "swingtrade.trader",
  });
  api.mcpCalls.mockResolvedValue([
    {
      id: 2,
      at: "2026-10-12T15:00:00Z",
      tool: "size_position",
      arguments: '{"ticker":"QQQ"}',
      durationMs: 31,
      caller: "owner-sub-1",
      status: "ok",
    },
    {
      id: 1,
      at: "2026-10-12T14:00:00Z",
      tool: "get_watchlist",
      arguments: "{}",
      durationMs: 12,
      caller: "owner-sub-1",
      status: "error",
    },
  ]);
});

describe("Settings form", () => {
  it("shows every threshold with a one-line explanation, percentages as percent", async () => {
    renderSettings("reader");
    await screen.findByLabelText(/Max risk per trade/);

    expect(field(/Starting capital/)).toHaveValue("2000");
    expect(field(/Max risk per trade/)).toHaveValue("1.5");
    expect(field(/Max open positions/)).toHaveValue("3");
    expect(field(/Minimum reward : risk/)).toHaveValue("2");
    expect(field(/Tax set-aside rate/)).toHaveValue("27");
    expect(field(/Wash-sale window/)).toHaveValue("30");
    expect(field(/Max position size/)).toHaveValue("50");
    expect(field(/Pullback band, low/)).toHaveValue("-1");
    expect(field(/Pullback band, high/)).toHaveValue("2");
    expect(field(/Extended threshold/)).toHaveValue("5");
    expect(field(/Risk basis/)).toHaveValue("StartingCapital");
    for (const f of FIELDS) {
      expect(f.help.length, f.key).toBeGreaterThan(20);
      expect(screen.getByText(f.help)).toBeInTheDocument();
    }
  });

  it("flags the tax rate as an assumption", async () => {
    renderSettings("reader");
    const flag = await screen.findByTestId("assumption-flag");
    expect(flag).toHaveTextContent(/Assumption/);
    expect(flag.closest("div")?.parentElement).toContainElement(field(/Tax set-aside rate/));
  });

  it.each(["reader", "trader"] as const)(
    "is read-only for a %s: fields disabled, no Save or Reset",
    async (persona) => {
      renderSettings(persona);
      await screen.findByLabelText(/Max risk per trade/);

      expect(screen.getByTestId("settings-readonly")).toHaveTextContent("swingtrade.admin");
      for (const f of FIELDS) expect(screen.getByLabelText(new RegExp(f.label))).toBeDisabled();
      expect(field(/Risk basis/)).toBeDisabled();
      expect(screen.queryByRole("button", { name: "Save settings" })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Reset to defaults" })).not.toBeInTheDocument();
    },
  );

  it("is editable for an admin", async () => {
    renderSettings("admin");
    await screen.findByLabelText(/Max risk per trade/);

    expect(screen.queryByTestId("settings-readonly")).not.toBeInTheDocument();
    expect(field(/Max risk per trade/)).toBeEnabled();
    expect(screen.getByRole("button", { name: "Save settings" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reset to defaults" })).toBeInTheDocument();
  });

  it("saves percentages back as fractions, with no floating-point noise", async () => {
    const user = userEvent.setup();
    api.updateSettings.mockResolvedValue(settings({ maxRiskPct: 0.07, pullbackBandHigh: 0.03 }));
    renderSettings("admin");
    await screen.findByLabelText(/Max risk per trade/);

    await user.clear(field(/Max risk per trade/));
    await user.type(field(/Max risk per trade/), "7");
    await user.clear(field(/Pullback band, high/));
    await user.type(field(/Pullback band, high/), "3");
    await user.selectOptions(field(/Risk basis/), "AccountValue");
    await user.click(screen.getByRole("button", { name: "Save settings" }));

    await waitFor(() =>
      expect(api.updateSettings).toHaveBeenCalledWith({
        startingCapital: 2000,
        maxRiskPct: 0.07,
        maxOpenPositions: 3,
        minRewardRisk: 2,
        taxSetAsideRate: 0.27,
        washSaleWindowDays: 30,
        maxPositionPct: 0.5,
        pullbackBandLow: -0.01,
        pullbackBandHigh: 0.03,
        extendedThreshold: 0.05,
        riskBasis: "AccountValue",
      }),
    );
    expect(await screen.findByText("Settings saved")).toBeInTheDocument();
  });

  it("Reset to defaults loads the plan's values into the form without saving until Save is pressed", async () => {
    const user = userEvent.setup();
    api.settings.mockResolvedValue(
      settings({
        maxRiskPct: 0.03,
        maxOpenPositions: 5,
        taxSetAsideRate: 0.2,
        pullbackBandLow: -0.02,
        pullbackBandHigh: 0.04,
        riskBasis: "AccountValue",
      }),
    );
    api.updateSettings.mockResolvedValue(settings());
    renderSettings("admin");
    await screen.findByLabelText(/Max risk per trade/);
    expect(field(/Max risk per trade/)).toHaveValue("3");

    await user.click(screen.getByRole("button", { name: "Reset to defaults" }));

    expect(field(/Max risk per trade/)).toHaveValue("1.5");
    expect(field(/Max open positions/)).toHaveValue("3");
    expect(field(/Tax set-aside rate/)).toHaveValue("27");
    expect(field(/Pullback band, low/)).toHaveValue("-1");
    expect(field(/Pullback band, high/)).toHaveValue("2");
    expect(field(/Risk basis/)).toHaveValue("StartingCapital");
    expect(api.updateSettings).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "Save settings" }));
    await waitFor(() => expect(api.updateSettings).toHaveBeenCalledWith(DEFAULT_SETTINGS));
  });

  it("the defaults match the plan", () => {
    expect(DEFAULT_SETTINGS).toEqual({
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
    });
    expect(toRequest(toFormValues(DEFAULT_SETTINGS)).request).toEqual(DEFAULT_SETTINGS);
  });

  it("rejects text that is not a number before sending", async () => {
    const user = userEvent.setup();
    renderSettings("admin");
    await screen.findByLabelText(/Max risk per trade/);

    await user.clear(field(/Max open positions/));
    await user.type(field(/Max open positions/), "2.5");
    await user.clear(field(/Max risk per trade/));
    await user.type(field(/Max risk per trade/), "abc");
    await user.click(screen.getByRole("button", { name: "Save settings" }));

    expect(await screen.findByText("Enter a whole number.")).toBeInTheDocument();
    expect(screen.getByText(/Enter max risk per trade as a number/)).toBeInTheDocument();
    expect(api.updateSettings).not.toHaveBeenCalled();
  });

  it("maps a 400 from the API onto the field", async () => {
    const user = userEvent.setup();
    api.updateSettings.mockRejectedValue(httpError(400, { errors: { MaxRiskPct: ["Must be between 0 and 1."] } }));
    renderSettings("admin");
    await screen.findByLabelText(/Max risk per trade/);

    await user.click(screen.getByRole("button", { name: "Save settings" }));

    expect(await screen.findByText("Must be between 0 and 1.")).toBeInTheDocument();
    expect(field(/Max risk per trade/)).toHaveAttribute("aria-invalid", "true");
  });

  it("shows the role, policy and audience names read-only", async () => {
    renderSettings("trader");
    const access = await screen.findByTestId("access-info");

    expect(access).toHaveTextContent("swingtrade.trader");
    expect(access).toHaveTextContent("swingtrade");
    expect(access).toHaveTextContent("swingtrade.reader, swingtrade.trader, swingtrade.admin");
    expect(access).toHaveTextContent("Read, Write, Admin, Mcp");
  });

  it("shows a retryable error when settings cannot be loaded", async () => {
    api.settings.mockRejectedValue(httpError(500));
    renderSettings("reader");
    expect(await screen.findByText("Could not load settings")).toBeInTheDocument();
  });
});

describe("MCP panel", () => {
  it("is shown to an admin with the endpoint, owner, add_note flag and the last calls", async () => {
    renderSettings("admin");

    const panel = await screen.findByTestId("mcp-panel");
    await within(panel).findByText("https://swing.example/mcp");
    expect(panel).toHaveTextContent("owner-sub-1");
    expect(panel).toHaveTextContent("Off (read-only)");
    expect(panel).toHaveTextContent("swingtrade.mcp");
    expect(api.mcpCalls).toHaveBeenCalledWith(20);
    expect(within(panel).getByText("size_position")).toBeInTheDocument();
    expect(within(panel).getByText("get_watchlist")).toBeInTheDocument();
    expect(within(panel).getByText("ok")).toBeInTheDocument();
    expect(within(panel).getByText("error")).toBeInTheDocument();
  });

  it("says when add_note is switched on and which role it needs", async () => {
    api.mcpInfo.mockResolvedValue({
      endpointUrl: "https://swing.example/mcp",
      ownerSubject: "owner-sub-1",
      addNoteEnabled: true,
      audience: "swingtrade",
      requiredRole: "swingtrade.mcp",
      addNoteRole: "swingtrade.trader",
    });
    renderSettings("admin");
    expect(await screen.findByText("On, needs swingtrade.trader")).toBeInTheDocument();
  });

  it.each(["reader", "trader"] as const)("is not shown to a %s, and nothing is fetched for it", async (persona) => {
    renderSettings(persona);
    await screen.findByLabelText(/Max risk per trade/);

    expect(screen.queryByTestId("mcp-panel")).not.toBeInTheDocument();
    expect(api.mcpInfo).not.toHaveBeenCalled();
    expect(api.mcpCalls).not.toHaveBeenCalled();
  });

  it("shows an empty call log", async () => {
    api.mcpCalls.mockResolvedValue([]);
    renderSettings("admin");
    expect(await screen.findByText("No calls recorded yet.")).toBeInTheDocument();
  });

  it("shows an error when the panel cannot load", async () => {
    api.mcpInfo.mockRejectedValue(httpError(500));
    renderSettings("admin");
    expect(await screen.findByText("Could not load the MCP panel")).toBeInTheDocument();
  });
});
