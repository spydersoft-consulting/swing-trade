import { screen, within } from "@testing-library/react";
import { AxiosError, AxiosHeaders } from "axios";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { Dashboard } from "./Dashboard";
import { swingTradeApi } from "../../api/swingTradeApi";
import { dashboard, renderScreen, settings } from "../../test/utils";

vi.mock("../../api/swingTradeApi", () => ({ swingTradeApi: { dashboard: vi.fn(), settings: vi.fn() } }));
vi.mock("../common/LineChart", () => ({
  LineChart: (props: { labels: string[]; series: { label: string; values: (number | null)[] }[] }) => (
    <div data-testid="line-chart" data-labels={props.labels.join("|")} data-values={props.series[0].values.join("|")}>
      {props.series[0].label}
    </div>
  ),
}));

const api = vi.mocked(swingTradeApi);

beforeEach(() => {
  vi.resetAllMocks();
  api.settings.mockResolvedValue(settings());
});

describe("Dashboard", () => {
  it("shows the six tiles with the API's figures, money to cents and percent to one decimal", async () => {
    api.dashboard.mockResolvedValue(
      dashboard({
        openCount: 2,
        capitalAtWork: 1500.5,
        capitalAtWorkPct: 0.75025,
        riskAtStops: 28.255,
        riskAtStopsPct: 0.0141275,
      }),
    );
    renderScreen(<Dashboard />, { persona: "reader" });

    const account = await screen.findByTestId("kpi-account");
    expect(account).toHaveTextContent("$2,053.55");
    expect(account).toHaveTextContent("+2.7% on $2,000.00");
    expect(screen.getByTestId("kpi-realized")).toHaveTextContent("$53.55");
    expect(screen.getByTestId("kpi-realized")).toHaveTextContent("After tax set-aside: $39.09");
    expect(screen.getByTestId("kpi-tax")).toHaveTextContent("$14.46");
    expect(screen.getByTestId("kpi-tax")).toHaveTextContent("27.0%");
    expect(screen.getByTestId("kpi-tax")).toHaveTextContent("assumption");
    expect(screen.getByTestId("kpi-open")).toHaveTextContent("2 of 3");
    expect(screen.getByTestId("kpi-capital")).toHaveTextContent("$1,500.50");
    expect(screen.getByTestId("kpi-capital")).toHaveTextContent("75.0% of starting capital");
    expect(screen.getByTestId("kpi-risk")).toHaveTextContent("$28.26");
    expect(screen.getByTestId("kpi-risk")).toHaveTextContent("1.4% of starting capital");
  });

  it("flags open positions over the limit in red, from the API's flag", async () => {
    api.dashboard.mockResolvedValue(dashboard({ openCount: 4, openCountOverLimit: true }));
    renderScreen(<Dashboard />);

    const tile = await screen.findByTestId("kpi-open");
    expect(tile).toHaveTextContent("4 of 3");
    expect(tile).toHaveTextContent("Over the position limit");
    expect(within(tile).getByText("4 of 3").className).toContain("text-bad");
  });

  it("shows performance metrics, results by setup and the SPY comparison", async () => {
    api.dashboard.mockResolvedValue(dashboard());
    renderScreen(<Dashboard />);

    const performance = await screen.findByTestId("performance");
    expect(performance).toHaveTextContent("Win rate100.0%");
    expect(performance).toHaveTextContent("Average win$53.55");
    expect(performance).toHaveTextContent("Average R3.80R");
    expect(performance).toHaveTextContent("Plan adherence100.0%");
    expect(performance).toHaveTextContent("Average days held7.0");
    expect(screen.getByTestId("by-setup")).toHaveTextContent("Pullback");
    expect(screen.getByTestId("by-setup")).toHaveTextContent("$53.55");
    expect(screen.getByTestId("spy-return")).toHaveTextContent("+1.2%");
    expect(screen.getByTestId("you-minus-spy")).toHaveTextContent("+1.4%");
  });

  it("handles a profit factor of null (no losing trades) without inventing a number", async () => {
    api.dashboard.mockResolvedValue(dashboard({ profitFactor: null, avgLoss: null }));
    renderScreen(<Dashboard />);

    const factor = await screen.findByTestId("profit-factor");
    expect(factor).toHaveTextContent("n/a");
    expect(factor).toHaveTextContent("No losing trades yet");
    expect(screen.getByTestId("performance")).toHaveTextContent("None yet");
  });

  it("shows a profit factor to two decimals when there are losses", async () => {
    api.dashboard.mockResolvedValue(dashboard({ profitFactor: 1.8333, avgLoss: -20 }));
    renderScreen(<Dashboard />);
    expect(await screen.findByTestId("profit-factor")).toHaveTextContent("1.83");
  });

  it("handles a journal with no closed trades and no SPY data", async () => {
    api.dashboard.mockResolvedValue(
      dashboard({
        realizedPnl: 0,
        accountValue: 2000,
        returnPct: 0,
        taxSetAside: 0,
        afterTaxPnl: 0,
        closedCount: 0,
        winRate: null,
        avgWin: null,
        avgLoss: null,
        profitFactor: null,
        avgR: null,
        planAdherence: null,
        avgDaysHeld: null,
        bySetup: {},
        spyReturnPct: null,
        youMinusSpy: null,
        equityCurve: [],
      }),
    );
    renderScreen(<Dashboard />);

    expect(await screen.findByTestId("kpi-account")).toHaveTextContent("$2,000.00");
    expect(screen.getByText(/No closed trades yet, so there are no performance figures/)).toBeInTheDocument();
    expect(screen.getByText(/No closed trades to group yet/)).toBeInTheDocument();
    expect(screen.getByTestId("spy-missing")).toBeInTheDocument();
    expect(screen.getByText(/The curve needs at least two closed trades/)).toBeInTheDocument();
    expect(screen.queryByTestId("line-chart")).not.toBeInTheDocument();
    expect(screen.queryByText("NaN")).not.toBeInTheDocument();
  });

  it("draws the equity curve from the API's points and dates it", async () => {
    api.dashboard.mockResolvedValue(dashboard());
    renderScreen(<Dashboard />);

    const chart = await screen.findByTestId("line-chart");
    expect(chart).toHaveAttribute("data-labels", "2026-10-05|2026-10-12");
    expect(chart).toHaveAttribute("data-values", "2000|2053.55");
    expect(screen.getByText(/through Oct 12, 2026/)).toBeInTheDocument();
  });

  it("shows the cash note with the reserved amount and the tax caveat", async () => {
    api.dashboard.mockResolvedValue(dashboard({ availableCash: 1050.25, reservedByPlanned: 942.3 }));
    renderScreen(<Dashboard />);
    expect(
      await screen.findByText(/Cash available for new trades: \$1,050.25 \(planned orders reserve \$942.30\)/),
    ).toBeInTheDocument();
  });

  it("shows a retryable error", async () => {
    api.dashboard.mockRejectedValue(
      new AxiosError("x", "E", undefined, undefined, {
        status: 500,
        statusText: "",
        data: {},
        headers: {},
        config: { headers: new AxiosHeaders() },
      }),
    );
    renderScreen(<Dashboard />);
    expect(await screen.findByText("Could not load the dashboard")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Retry" })).toBeInTheDocument();
  });
});
