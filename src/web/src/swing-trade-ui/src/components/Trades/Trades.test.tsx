import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AxiosError, AxiosHeaders } from "axios";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { Trades } from "./Trades";
import { swingTradeApi } from "../../api/swingTradeApi";
import { renderScreen, row, sizerResult, trade, type Persona } from "../../test/utils";

vi.mock("../../api/swingTradeApi", () => ({
  swingTradeApi: {
    trades: vi.fn(),
    createTrade: vi.fn(),
    updateTrade: vi.fn(),
    fillTrade: vi.fn(),
    closeTrade: vi.fn(),
    cancelTrade: vi.fn(),
    size: vi.fn(),
    watchlist: vi.fn(),
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

const planned = trade({
  id: 10,
  ticker: "XLK",
  status: "Planned",
  openDate: null,
  exitPrice: null,
  exitDate: null,
  pnl: null,
  pnlPct: null,
  rMultiple: null,
  daysHeld: null,
  followedPlan: null,
  entryPrice: 192,
  stopPrice: 187.5,
  targetPrice: 202,
  shares: 5,
  position: 960,
  riskDollars: 22.5,
  riskPct: 0.01125,
  plannedRewardRisk: 2.222222,
});
const open = trade({
  id: 11,
  ticker: "QQQ",
  status: "Open",
  exitPrice: null,
  exitDate: null,
  pnl: null,
  pnlPct: null,
  rMultiple: null,
  daysHeld: null,
  followedPlan: null,
  shares: 1,
  entryPrice: 748.3,
  stopPrice: 740,
  targetPrice: 770,
  position: 748.3,
  riskDollars: 8.3,
  riskPct: 0.00415,
  plannedRewardRisk: 2.614458,
});
const closed = trade({ id: 1 });
const hot = trade({
  id: 12,
  ticker: "IWM",
  status: "Planned",
  openDate: null,
  exitPrice: null,
  exitDate: null,
  pnl: null,
  pnlPct: null,
  rMultiple: null,
  daysHeld: null,
  followedPlan: null,
  riskOverLimit: true,
  rewardRiskBelowMin: true,
  washSaleFlag: true,
  riskPct: 0.05,
  plannedRewardRisk: 0.5,
});

const renderTrades = (persona: Persona = "trader", route = "/?tab=all") => renderScreen(<Trades />, { persona, route });
const rowOf = (ticker: string) =>
  screen.getByText(ticker, { selector: "span.font-semibold" }).closest("tr") as HTMLElement;

beforeEach(() => {
  vi.resetAllMocks();
  api.trades.mockResolvedValue([planned, open, closed, hot]);
  api.watchlist.mockResolvedValue([row({ ticker: "XLK" }), row({ ticker: "QQQ" })]);
});

describe("Trades", () => {
  it("shows tabs with counts and filters rows by status", async () => {
    const user = userEvent.setup();
    renderTrades("trader", "/?tab=planned");

    expect(await screen.findByText("XLK", { selector: "span.font-semibold" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /^Planned 2/ })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: /^Open 1/ })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /^Closed 1/ })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /^All 4/ })).toBeInTheDocument();
    expect(screen.queryByText("QQQ", { selector: "span.font-semibold" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: /^Open/ }));
    expect(screen.getByText("QQQ", { selector: "span.font-semibold" })).toBeInTheDocument();
    expect(screen.queryByText("XLK", { selector: "span.font-semibold" })).not.toBeInTheDocument();
  });

  it("defaults to the Open tab", async () => {
    renderTrades("trader", "/");
    expect(await screen.findByText("QQQ", { selector: "span.font-semibold" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /^Open/ })).toHaveAttribute("aria-selected", "true");
  });

  it("shows the server's computed columns, formatted: money to cents, percent to one decimal", async () => {
    renderTrades();
    await screen.findByText("XLE", { selector: "span.font-semibold" });

    const xle = within(rowOf("XLE"));
    expect(xle.getByText("$935.10")).toBeInTheDocument(); // position
    expect(xle.getByText("$14.10")).toBeInTheDocument(); // risk $
    expect(xle.getByText("0.7%")).toBeInTheDocument(); // risk %
    expect(xle.getByText("3.79")).toBeInTheDocument(); // planned R:R
    expect(xle.getByText("$53.55")).toBeInTheDocument(); // P&L
    expect(xle.getByText("3.80R")).toBeInTheDocument(); // R-multiple
    expect(xle.getByText("7")).toBeInTheDocument(); // days held
    expect(xle.getByText("Oct 5, 2026")).toBeInTheDocument();
  });

  it("highlights rows and badges them from the API's flags, not by recomputing", async () => {
    renderTrades();
    await screen.findByText("IWM", { selector: "span.font-semibold" });

    const hotRow = rowOf("IWM");
    expect(hotRow.className).toContain("row-bad");
    const flags = within(hotRow);
    expect(flags.getByText("Risk over limit")).toBeInTheDocument();
    expect(flags.getByText("R:R below min")).toBeInTheDocument();
    expect(flags.getByText("Wash sale")).toBeInTheDocument();

    const calm = rowOf("XLK");
    expect(calm.className).not.toContain("row-bad");
    expect(calm.className).not.toContain("row-warn");
    expect(within(calm).queryByText("Risk over limit")).not.toBeInTheDocument();
  });

  it("uses the amber row for reward:risk or wash-sale flags without a risk breach", async () => {
    api.trades.mockResolvedValue([
      trade({
        id: 20,
        ticker: "GLD",
        status: "Open",
        rewardRiskBelowMin: true,
        exitPrice: null,
        exitDate: null,
        pnl: null,
        rMultiple: null,
        daysHeld: null,
      }),
    ]);
    renderTrades();
    await screen.findByText("GLD", { selector: "span.font-semibold" });
    expect(rowOf("GLD").className).toContain("row-warn");
  });

  it("offers fill, edit and cancel on a planned row, close and edit on an open row, edit on a closed row", async () => {
    renderTrades();
    await screen.findByText("XLK", { selector: "span.font-semibold" });

    expect(within(rowOf("XLK")).getByRole("button", { name: "Fill XLK" })).toBeInTheDocument();
    expect(within(rowOf("XLK")).getByRole("button", { name: "Edit XLK" })).toBeInTheDocument();
    expect(within(rowOf("XLK")).getByRole("button", { name: "Cancel XLK" })).toBeInTheDocument();
    expect(within(rowOf("QQQ")).getByRole("button", { name: "Close QQQ" })).toBeInTheDocument();
    expect(within(rowOf("QQQ")).getByRole("button", { name: "Edit QQQ" })).toBeInTheDocument();
    expect(within(rowOf("QQQ")).queryByRole("button", { name: "Fill QQQ" })).not.toBeInTheDocument();
    expect(within(rowOf("XLE")).getByRole("button", { name: "Edit XLE" })).toBeInTheDocument();
    expect(within(rowOf("XLE")).queryByRole("button", { name: /Close|Fill|Cancel/ })).not.toBeInTheDocument();
  });

  it("hides New trade and every row action from a reader, but still shows the data", async () => {
    renderTrades("reader");
    await screen.findByText("XLK", { selector: "span.font-semibold" });

    expect(screen.queryByRole("button", { name: "New trade" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^(Fill|Close|Edit|Cancel) / })).not.toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: "Actions" })).not.toBeInTheDocument();
    expect(within(rowOf("XLE")).getByText("$53.55")).toBeInTheDocument();
  });

  it("fills a planned trade with the entered price and date", async () => {
    const user = userEvent.setup();
    api.fillTrade.mockResolvedValue({ ...planned, status: "Open", entryPrice: 192.4, openDate: "2026-10-12" });
    renderTrades();
    await user.click(await screen.findByRole("button", { name: "Fill XLK" }));

    const dialog = await screen.findByRole("dialog");
    const price = within(dialog).getByLabelText("Fill price");
    expect(price).toHaveValue("192");
    await user.clear(price);
    await user.type(price, "192.4");
    await user.click(within(dialog).getByRole("button", { name: "Mark as filled" }));

    await waitFor(() =>
      expect(api.fillTrade).toHaveBeenCalledWith(10, {
        fillPrice: 192.4,
        fillDate: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/),
      }),
    );
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("shows a 409 transition inline and as a toast when filling fails", async () => {
    const user = userEvent.setup();
    api.fillTrade.mockRejectedValue(
      httpError(409, { title: "Conflict", detail: "Only a Planned trade can be filled." }),
    );
    renderTrades();
    await user.click(await screen.findByRole("button", { name: "Fill XLK" }));
    const dialog = await screen.findByRole("dialog");

    await user.click(within(dialog).getByRole("button", { name: "Mark as filled" }));

    expect(await within(dialog).findByText("Only a Planned trade can be filled.")).toBeInTheDocument();
    expect(await screen.findByText("Cannot fill this trade")).toBeInTheDocument();
  });

  it("closes an open trade with exit price, date, plan adherence and lessons", async () => {
    const user = userEvent.setup();
    api.closeTrade.mockResolvedValue({ ...open, status: "Closed", exitPrice: 760, exitDate: "2026-10-12", pnl: 11.7 });
    renderTrades();
    await user.click(await screen.findByRole("button", { name: "Close QQQ" }));
    const dialog = await screen.findByRole("dialog");

    await user.type(within(dialog).getByLabelText("Exit price"), "760");
    await user.type(within(dialog).getByLabelText("Lessons (optional)"), "Held to target");
    await user.click(within(dialog).getByLabelText("I followed my plan"));
    await user.click(within(dialog).getByRole("button", { name: "Close trade" }));

    await waitFor(() =>
      expect(api.closeTrade).toHaveBeenCalledWith(11, {
        exitPrice: 760,
        exitDate: expect.any(String),
        followedPlan: false,
        lessons: "Held to target",
      }),
    );
  });

  it("asks before cancelling a planned trade", async () => {
    const user = userEvent.setup();
    api.cancelTrade.mockResolvedValue({ ...planned, status: "Cancelled" });
    renderTrades();
    await user.click(await screen.findByRole("button", { name: "Cancel XLK" }));

    expect(api.cancelTrade).not.toHaveBeenCalled();
    await user.click(await screen.findByRole("button", { name: "Cancel the trade" }));
    await waitFor(() => expect(api.cancelTrade).toHaveBeenCalledWith(10));
  });

  describe("the new-trade form", () => {
    const openForm = async () => {
      const user = userEvent.setup();
      renderTrades();
      await user.click(await screen.findByRole("button", { name: "New trade" }));
      const dialog = await screen.findByRole("dialog");
      return { user, dialog };
    };

    it("shows the sizer's result live while prices are typed, from POST /sizer", async () => {
      api.size.mockResolvedValue(
        sizerResult({
          ticker: "XLK",
          entry: 192,
          stop: 187.5,
          target: 202,
          shares: 5,
          maxEntryForMinRewardRisk: 192.33,
        }),
      );
      const { user, dialog } = await openForm();

      await user.selectOptions(within(dialog).getByLabelText("Ticker"), "XLK");
      await user.type(within(dialog).getByLabelText("Entry (limit price)"), "192");
      await user.type(within(dialog).getByLabelText("Stop price"), "187.5");
      await user.type(within(dialog).getByLabelText("Target price"), "202");

      expect(await within(dialog).findByTestId("sizer-verdict", {}, { timeout: 3000 })).toHaveTextContent(
        "OK - log it",
      );
      expect(within(dialog).getByTestId("sizer-max-entry")).toHaveTextContent("$192.33");
      await waitFor(() =>
        expect(api.size).toHaveBeenLastCalledWith({ ticker: "XLK", entry: 192, stop: 187.5, target: 202 }),
      );
    });

    it("maps 400 field errors from the API onto the fields", async () => {
      api.size.mockResolvedValue(sizerResult());
      api.createTrade.mockRejectedValue(
        httpError(400, {
          errors: {
            EntryPrice: ["Entry must be above the stop."],
            Shares: ["Shares must be at least 1."],
            Ticker: ["Ticker is required."],
          },
        }),
      );
      const { user, dialog } = await openForm();
      await user.type(within(dialog).getByLabelText("Entry (limit price)"), "100");
      await user.type(within(dialog).getByLabelText("Stop price"), "120");
      await user.type(within(dialog).getByLabelText("Target price"), "150");
      await user.type(within(dialog).getByLabelText("Shares"), "0");

      await user.click(within(dialog).getByRole("button", { name: "Create trade" }));

      expect(await within(dialog).findByText("Entry must be above the stop.")).toBeInTheDocument();
      expect(within(dialog).getByText("Shares must be at least 1.")).toBeInTheDocument();
      expect(within(dialog).getByText("Ticker is required.")).toBeInTheDocument();
      expect(within(dialog).getByLabelText("Entry (limit price)")).toHaveAttribute("aria-invalid", "true");
      expect(within(dialog).getByLabelText("Stop price")).toHaveAttribute("aria-invalid", "false");
    });

    it("catches text that is not a number before sending anything", async () => {
      const { user, dialog } = await openForm();
      await user.type(within(dialog).getByLabelText("Entry (limit price)"), "abc");

      await user.click(within(dialog).getByRole("button", { name: "Create trade" }));

      expect(await within(dialog).findByText(/Enter the entry price as a number/)).toBeInTheDocument();
      expect(api.createTrade).not.toHaveBeenCalled();
    });

    it("offers the sizer's share count without applying it behind the user's back", async () => {
      api.size.mockResolvedValue(sizerResult({ shares: 15 }));
      const { user, dialog } = await openForm();
      await user.type(within(dialog).getByLabelText("Entry (limit price)"), "62.82");
      await user.type(within(dialog).getByLabelText("Stop price"), "61.4");
      await user.type(within(dialog).getByLabelText("Target price"), "65.9");

      const use = await within(dialog).findByRole("button", { name: "Use 15 shares" }, { timeout: 3000 });
      expect(within(dialog).getByLabelText("Shares")).toHaveValue("");
      await user.click(use);
      expect(within(dialog).getByLabelText("Shares")).toHaveValue("15");
    });

    it("sends a planned trade with the typed values", async () => {
      api.size.mockResolvedValue(sizerResult());
      api.createTrade.mockResolvedValue({ ...planned, id: 99 });
      const { user, dialog } = await openForm();
      await user.selectOptions(within(dialog).getByLabelText("Ticker"), "QQQ");
      await user.type(within(dialog).getByLabelText("Entry (limit price)"), "748.3");
      await user.type(within(dialog).getByLabelText("Stop price"), "740");
      await user.type(within(dialog).getByLabelText("Target price"), "770");
      await user.type(within(dialog).getByLabelText("Shares"), "1");

      await user.click(within(dialog).getByRole("button", { name: "Create trade" }));

      await waitFor(() =>
        expect(api.createTrade).toHaveBeenCalledWith({
          ticker: "QQQ",
          status: "Planned",
          setup: "Pullback",
          openDate: null,
          shares: 1,
          entryPrice: 748.3,
          stopPrice: 740,
          targetPrice: 770,
          rationale: null,
        }),
      );
    });
  });

  it("edits a trade in place", async () => {
    const user = userEvent.setup();
    api.updateTrade.mockResolvedValue({ ...planned, shares: 4 });
    renderTrades();
    await user.click(await screen.findByRole("button", { name: "Edit XLK" }));
    const dialog = await screen.findByRole("dialog");

    const shares = within(dialog).getByLabelText("Shares");
    expect(shares).toHaveValue("5");
    await user.clear(shares);
    await user.type(shares, "4");
    await user.click(within(dialog).getByRole("button", { name: "Save changes" }));

    await waitFor(() =>
      expect(api.updateTrade).toHaveBeenCalledWith(
        10,
        expect.objectContaining({ ticker: "XLK", shares: 4, entryPrice: 192, openDate: null }),
      ),
    );
  });

  it("shows empty and error states", async () => {
    api.trades.mockResolvedValue([]);
    const { unmount } = renderTrades("trader", "/?tab=open");
    expect(await screen.findByText(/No open trades/)).toBeInTheDocument();
    unmount();

    api.trades.mockRejectedValue(httpError(500));
    renderTrades();
    expect(await screen.findByText("Could not load trades")).toBeInTheDocument();
  });
});
