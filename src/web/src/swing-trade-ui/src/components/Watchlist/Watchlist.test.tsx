import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AxiosError, AxiosHeaders } from "axios";
import { Route, Routes, useLocation } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { Watchlist } from "./Watchlist";
import { swingTradeApi } from "../../api/swingTradeApi";
import { renderScreen, row, type Persona } from "../../test/utils";
import { setRefreshPollInterval } from "../../store/slices/watchlistSlice";
import type { RefreshRunDto } from "../../api/generated/types.gen";

vi.mock("../../api/swingTradeApi", () => ({
  swingTradeApi: {
    watchlist: vi.fn(),
    refreshRuns: vi.fn(),
    refreshRun: vi.fn(),
    startRefresh: vi.fn(),
    bars: vi.fn(),
    instruments: vi.fn(),
    createInstrument: vi.fn(),
    updateInstrument: vi.fn(),
  },
}));
vi.mock("../common/LineChart", () => ({
  LineChart: (props: { ariaLabel: string; series: { label: string }[] }) => (
    <div data-testid="line-chart" aria-label={props.ariaLabel}>
      {props.series.map((s) => s.label).join(",")}
    </div>
  ),
}));

const api = vi.mocked(swingTradeApi);

const rows = [
  row({ ticker: "QQQ", name: "Invesco QQQ" }),
  row({
    ticker: "SPY",
    name: "SPDR S&P 500",
    signal: "Watch",
    signalText: "Watch",
    asOfDate: "2026-10-02",
    stale: true,
    last: 769.64,
    pctVs20: 0.00629,
  }),
  row({
    ticker: "XLV",
    name: "Health Care",
    signal: "Avoid",
    signalText: "Avoid",
    trend: "Downtrend",
    trendText: "Downtrend",
    reentryOkAfter: "2026-11-05",
  }),
];

const finishedRun = (overrides: Partial<RefreshRunDto> = {}): RefreshRunDto => ({
  id: 3,
  startedAt: "2026-10-12T21:30:00Z",
  finishedAt: "2026-10-12T21:31:00Z",
  provider: "alpaca",
  status: "Succeeded",
  tickersOk: 12,
  tickersFailed: 0,
  message: null,
  ...overrides,
});

const Probe = () => {
  const location = useLocation();
  return <div data-testid="location">{`${location.pathname}${location.search}`}</div>;
};

const renderWatchlist = (persona: Persona = "trader") =>
  renderScreen(
    <Routes>
      <Route path="/" element={<Watchlist />} />
      <Route path="/sizer" element={<Probe />} />
    </Routes>,
    { persona },
  );

const tableRow = (ticker: string) =>
  screen.getByRole("button", { name: `Open ${ticker} chart` }).closest("tr") as HTMLElement;

beforeEach(() => {
  vi.resetAllMocks();
  setRefreshPollInterval(1);
  api.watchlist.mockResolvedValue(rows);
  api.refreshRuns.mockResolvedValue({ items: [finishedRun()], totalCount: 1, page: 1, pageSize: 1 });
  api.bars.mockResolvedValue({
    ticker: "QQQ",
    days: 120,
    bars: [
      { date: "2026-10-08", close: 749, ma20: 737, ma50: 725 },
      { date: "2026-10-09", close: 751.27, ma20: 737.54, ma50: 725.27 },
    ],
  });
});

describe("Watchlist", () => {
  it("shows the API's rows in the API's order with the documented columns", async () => {
    renderWatchlist();

    await screen.findByRole("button", { name: "Open QQQ chart" });
    for (const header of [
      "Ticker",
      "Name",
      "As of",
      "Last",
      "20-day",
      "50-day",
      "% vs 20-day",
      "Trend",
      "Signal",
      "Re-entry date",
    ]) {
      expect(
        screen.getByRole("columnheader", { name: new RegExp(`^${header.replace("%", "\\%")}`) }),
      ).toBeInTheDocument();
    }
    const tickers = screen.getAllByRole("button", { name: /^Open .* chart$/ }).map((b) => b.textContent);
    expect(tickers).toEqual(["QQQ", "SPY", "XLV"]);

    const qqq = within(tableRow("QQQ"));
    expect(qqq.getByText("Oct 9, 2026")).toBeInTheDocument();
    expect(qqq.getByText("$751.27")).toBeInTheDocument();
    expect(qqq.getByText("$737.54")).toBeInTheDocument();
    expect(qqq.getByText("$725.27")).toBeInTheDocument();
    expect(qqq.getByText("+1.9%")).toBeInTheDocument();
    expect(qqq.getByText("Pullback candidate")).toBeInTheDocument();
    expect(qqq.getByText("Clear")).toBeInTheDocument();
    expect(within(tableRow("XLV")).getByText(/after Nov 5, 2026/)).toBeInTheDocument();
  });

  it("shows a stale badge, and a banner, only for the stale rows, next to their as-of date", async () => {
    renderWatchlist();
    await screen.findByRole("button", { name: "Open QQQ chart" });

    expect(within(tableRow("SPY")).getByTestId("stale-badge")).toBeInTheDocument();
    expect(within(tableRow("SPY")).getByText("Oct 2, 2026")).toBeInTheDocument();
    expect(within(tableRow("QQQ")).queryByTestId("stale-badge")).not.toBeInTheDocument();
    expect(screen.getAllByTestId("stale-badge")).toHaveLength(1);
    expect(screen.getByTestId("stale-banner")).toHaveTextContent("1 ticker has prices older");
  });

  it("colours pullback candidates green, Avoid red and everything else neutral", async () => {
    renderWatchlist();
    await screen.findByRole("button", { name: "Open QQQ chart" });

    const pill = (ticker: string) => within(tableRow(ticker)).getByTestId("signal");
    expect(pill("QQQ").className).toContain("text-good");
    expect(pill("XLV").className).toContain("text-bad");
    expect(pill("SPY").className).not.toContain("text-good");
    expect(pill("SPY").className).not.toContain("text-bad");
  });

  it("filters by signal with chips and shows counts", async () => {
    const user = userEvent.setup();
    renderWatchlist();
    await screen.findByRole("button", { name: "Open QQQ chart" });

    expect(screen.getByRole("button", { name: /^All 3/ })).toHaveAttribute("aria-pressed", "true");
    await user.click(screen.getByRole("button", { name: /^Avoid 1/ }));

    expect(screen.getByRole("button", { name: /^Avoid/ })).toHaveAttribute("aria-pressed", "true");
    expect(screen.queryByRole("button", { name: "Open QQQ chart" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Open XLV chart" })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /^Extended - wait 0/ }));
    expect(screen.getByText(/No tickers with the signal/)).toBeInTheDocument();
  });

  it("hides every write action from a reader", async () => {
    renderWatchlist("reader");
    await screen.findByRole("button", { name: "Open QQQ chart" });

    expect(screen.queryByRole("button", { name: "Add ticker" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Refresh now" })).not.toBeInTheDocument();
  });

  it("offers Add ticker to a trader but not Refresh now, and both to an admin", async () => {
    const { unmount } = renderWatchlist("trader");
    await screen.findByRole("button", { name: "Open QQQ chart" });
    expect(screen.getByRole("button", { name: "Add ticker" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Refresh now" })).not.toBeInTheDocument();
    unmount();

    renderWatchlist("admin");
    await screen.findByRole("button", { name: "Open QQQ chart" });
    expect(screen.getByRole("button", { name: "Add ticker" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Refresh now" })).toBeInTheDocument();
  });

  it("shows the last refresh run's status", async () => {
    api.refreshRuns.mockResolvedValue({
      items: [finishedRun({ status: "Partial", tickersOk: 11, tickersFailed: 1, message: "XLU via tiingo failed" })],
      totalCount: 1,
      page: 1,
      pageSize: 1,
    });
    renderWatchlist();

    await waitFor(() => expect(screen.getByTestId("refresh-status")).toHaveTextContent("Partial"));
    expect(screen.getByTestId("refresh-status")).toHaveTextContent("11 ok, 1 failed");
    expect(screen.getByTestId("refresh-status")).toHaveTextContent("XLU via tiingo failed");
  });

  it("Refresh now (admin) starts a run, polls it, and reloads the rows", async () => {
    const user = userEvent.setup();
    api.startRefresh.mockResolvedValue(finishedRun({ status: "Running", finishedAt: null, tickersOk: 0 }));
    api.refreshRun.mockResolvedValue(finishedRun());
    renderWatchlist("admin");
    await screen.findByRole("button", { name: "Open QQQ chart" });
    expect(api.watchlist).toHaveBeenCalledTimes(1);

    await user.click(screen.getByRole("button", { name: "Refresh now" }));

    await waitFor(() => expect(api.refreshRun).toHaveBeenCalledWith(3));
    await waitFor(() => expect(api.watchlist).toHaveBeenCalledTimes(2));
    expect(await screen.findByText("Refresh finished")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Refresh now" })).toBeEnabled();
  });

  it("explains a refresh that is already running (409)", async () => {
    const user = userEvent.setup();
    api.startRefresh.mockRejectedValue(
      new AxiosError("x", "E", undefined, undefined, {
        status: 409,
        statusText: "",
        data: { detail: "running" },
        headers: {},
        config: { headers: new AxiosHeaders() },
      }),
    );
    renderWatchlist("admin");
    await screen.findByRole("button", { name: "Open QQQ chart" });

    await user.click(screen.getByRole("button", { name: "Refresh now" }));

    expect(await screen.findByText("A refresh is already running")).toBeInTheDocument();
  });

  it("opens a drawer with the 120-day chart (close, 20-day, 50-day) when a row is chosen", async () => {
    const user = userEvent.setup();
    renderWatchlist("reader");
    await user.click(await screen.findByRole("button", { name: "Open QQQ chart" }));

    const drawer = await screen.findByTestId("instrument-drawer");
    expect(api.bars).toHaveBeenCalledWith("QQQ", 120);
    const chart = await within(drawer).findByTestId("line-chart");
    expect(chart).toHaveTextContent("Close,20-day,50-day");
    expect(within(drawer).getByTestId("chart-asof")).toHaveTextContent("2 completed sessions, through Oct 9, 2026");
    expect(within(drawer).getByText("$751.27")).toBeInTheDocument();
    expect(within(drawer).queryByRole("button", { name: "Deactivate ticker" })).not.toBeInTheDocument();
  });

  it("Plan a trade opens the sizer prefilled with the last close", async () => {
    const user = userEvent.setup();
    renderWatchlist("reader");
    await user.click(await screen.findByRole("button", { name: "Open QQQ chart" }));

    await user.click(await screen.findByRole("button", { name: "Plan a trade" }));

    expect(await screen.findByTestId("location")).toHaveTextContent("/sizer?ticker=QQQ&entry=751.27");
  });

  it("lets a trader deactivate a ticker from the drawer after confirming", async () => {
    const user = userEvent.setup();
    api.instruments.mockResolvedValue([
      { ticker: "QQQ", name: "Invesco QQQ", category: null, active: true, isBenchmark: false, washSaleGroup: null },
    ]);
    api.updateInstrument.mockResolvedValue({
      ticker: "QQQ",
      name: "Invesco QQQ",
      category: null,
      active: false,
      isBenchmark: false,
      washSaleGroup: null,
    });
    renderWatchlist("trader");
    await user.click(await screen.findByRole("button", { name: "Open QQQ chart" }));

    await user.click(await screen.findByRole("button", { name: "Deactivate ticker" }));
    await user.click(await screen.findByRole("button", { name: "Deactivate" }));

    await waitFor(() =>
      expect(api.updateInstrument).toHaveBeenCalledWith("QQQ", expect.objectContaining({ active: false })),
    );
  });

  it("maps a 400 from Add ticker onto its field", async () => {
    const user = userEvent.setup();
    api.createInstrument.mockRejectedValue(
      new AxiosError("x", "E", undefined, undefined, {
        status: 400,
        statusText: "",
        data: { errors: { Name: ["Name is required."] } },
        headers: {},
        config: { headers: new AxiosHeaders() },
      }),
    );
    renderWatchlist("trader");
    await screen.findByRole("button", { name: "Open QQQ chart" });

    await user.click(screen.getByRole("button", { name: "Add ticker" }));
    const dialog = await screen.findByRole("dialog");
    await user.type(within(dialog).getByLabelText("Ticker"), "xlp");
    await user.click(within(dialog).getByRole("button", { name: "Add ticker" }));

    expect(await within(dialog).findByText("Name is required.")).toBeInTheDocument();
    expect(api.createInstrument).toHaveBeenCalledWith(expect.objectContaining({ ticker: "XLP" }));
    expect(within(dialog).getByLabelText("Name")).toHaveAttribute("aria-invalid", "true");
  });

  it("explains an empty watchlist differently to a trader and to a reader", async () => {
    api.watchlist.mockResolvedValue([]);
    const { unmount } = renderWatchlist("reader");
    expect(await screen.findByText(/Ask a trader to add one/)).toBeInTheDocument();
    unmount();
    renderWatchlist("trader");
    expect(await screen.findByText(/Add a ticker to start/)).toBeInTheDocument();
  });

  it("shows a retryable error when the watchlist cannot be loaded, and a permission message for 403", async () => {
    const user = userEvent.setup();
    api.watchlist.mockRejectedValueOnce(
      new AxiosError("x", "E", undefined, undefined, {
        status: 500,
        statusText: "",
        data: {},
        headers: {},
        config: { headers: new AxiosHeaders() },
      }),
    );
    renderWatchlist();

    expect(await screen.findByText("Could not load the watchlist")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Retry" }));
    expect(await screen.findByRole("button", { name: "Open QQQ chart" })).toBeInTheDocument();
  });

  it("shows the 403 as a permission problem without a retry button", async () => {
    api.watchlist.mockRejectedValue(
      new AxiosError("x", "E", undefined, undefined, {
        status: 403,
        statusText: "",
        data: {},
        headers: {},
        config: { headers: new AxiosHeaders() },
      }),
    );
    renderWatchlist();

    expect(await screen.findByText("Not permitted")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Retry" })).not.toBeInTheDocument();
  });
});
