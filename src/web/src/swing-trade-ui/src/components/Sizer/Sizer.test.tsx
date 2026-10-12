import { act, renderHook, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AxiosError, AxiosHeaders } from "axios";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { Sizer } from "./Sizer";
import { useSizer } from "./useSizer";
import { swingTradeApi } from "../../api/swingTradeApi";
import { renderScreen, sizerResult, trade, type Persona } from "../../test/utils";

vi.mock("../../api/swingTradeApi", () => ({ swingTradeApi: { size: vi.fn(), createTrade: vi.fn() } }));

const api = vi.mocked(swingTradeApi);
const httpError = (status: number, data?: unknown) =>
  new AxiosError("x", "E", undefined, undefined, {
    status,
    statusText: "",
    data,
    headers: {},
    config: { headers: new AxiosHeaders() },
  });

const renderSizer = (persona: Persona = "trader", route = "/") => renderScreen(<Sizer />, { persona, route });

async function enter(user: ReturnType<typeof userEvent.setup>, entry: string, stop: string, target: string) {
  await user.clear(screen.getByLabelText("Entry"));
  await user.type(screen.getByLabelText("Entry"), entry);
  await user.type(screen.getByLabelText("Stop"), stop);
  await user.type(screen.getByLabelText("Target"), target);
}

beforeEach(() => {
  vi.resetAllMocks();
  api.size.mockResolvedValue(sizerResult());
});

describe("Sizer", () => {
  it("asks for the three prices before calculating anything", () => {
    renderSizer();
    expect(screen.getByText(/Enter an entry, a stop and a target/)).toBeInTheDocument();
    expect(api.size).not.toHaveBeenCalled();
  });

  it("shows shares by risk, cap and cash, the verdict and the max entry, all from the API", async () => {
    const user = userEvent.setup();
    renderSizer();

    await enter(user, "62.82", "61.40", "65.90");

    expect(await screen.findByTestId("sizer-verdict", {}, { timeout: 3000 })).toHaveTextContent("OK - log it");
    expect(api.size).toHaveBeenLastCalledWith({ ticker: undefined, entry: 62.82, stop: 61.4, target: 65.9 });
    expect(screen.getByTestId("sizer-shares")).toHaveTextContent("15");
    expect(screen.getByText("Shares by risk").nextSibling).toHaveTextContent("21");
    expect(screen.getByText("Shares by position cap").nextSibling).toHaveTextContent("15");
    expect(screen.getByText("Shares by cash").nextSibling).toHaveTextContent("31");
    expect(screen.getByText("Position cost").nextSibling).toHaveTextContent("$942.30");
    expect(screen.getByText("Risk at the stop").nextSibling).toHaveTextContent("$21.30 (1.1%)");
    expect(screen.getByText("Reward : risk").nextSibling).toHaveTextContent("2.17");
    expect(screen.getByTestId("sizer-max-entry")).toHaveTextContent("$62.90");
  });

  it("shows a SKIP verdict in red, and the open-position warning without changing the verdict", async () => {
    api.size.mockResolvedValue(
      sizerResult({
        verdict: "SKIP - reward:risk below minimum",
        isOk: false,
        shares: 1,
        warning: "3 positions are already open (the limit is 3).",
        openCount: 3,
      }),
    );
    const user = userEvent.setup();
    renderSizer();

    await enter(user, "769.64", "753.50", "773.50");

    const verdict = await screen.findByTestId("sizer-verdict", {}, { timeout: 3000 });
    expect(verdict).toHaveTextContent("SKIP - reward:risk below minimum");
    expect(verdict.className).toContain("text-bad");
    expect(screen.getByTestId("sizer-warning")).toHaveTextContent("already open");
  });

  it("re-sizes when a price changes, without a button", async () => {
    const user = userEvent.setup();
    renderSizer();
    await enter(user, "62.82", "61.40", "65.90");
    await screen.findByTestId("sizer-verdict", {}, { timeout: 3000 });
    api.size.mockResolvedValue(sizerResult({ verdict: "Stop must be below entry", isOk: false, shares: 0 }));

    await user.clear(screen.getByLabelText("Stop"));
    await user.type(screen.getByLabelText("Stop"), "70");

    await waitFor(() => expect(screen.getByTestId("sizer-verdict")).toHaveTextContent("Stop must be below entry"), {
      timeout: 3000,
    });
  });

  it("is prefilled from the watchlist link (ticker and last close)", () => {
    renderSizer("trader", "/sizer?ticker=qqq&entry=751.27");

    expect(screen.getByLabelText("Ticker (optional)")).toHaveValue("QQQ");
    expect(screen.getByLabelText("Entry")).toHaveValue("751.27");
  });

  it("shows the API's message when the sizer rejects the input", async () => {
    api.size.mockRejectedValue(httpError(400, { errors: { Ticker: ["Unknown ticker ZZZ."] } }));
    const user = userEvent.setup();
    renderSizer();
    await user.type(screen.getByLabelText("Ticker (optional)"), "zzz");

    await enter(user, "10", "9", "13");

    expect(await screen.findByTestId("sizer-error", {}, { timeout: 3000 })).toHaveTextContent("Unknown ticker ZZZ.");
  });

  it("creates a planned trade from the result (trader)", async () => {
    api.createTrade.mockResolvedValue(
      trade({ id: 50, ticker: "XLE", status: "Planned", shares: 15, entryPrice: 62.82 }),
    );
    const user = userEvent.setup();
    renderSizer("trader", "/sizer?ticker=XLE&entry=62.82");
    const create = screen.getByRole("button", { name: "Create planned trade" });
    expect(create).toBeDisabled();

    await user.type(screen.getByLabelText("Stop"), "61.40");
    await user.type(screen.getByLabelText("Target"), "65.90");
    await screen.findByTestId("sizer-verdict", {}, { timeout: 3000 });
    await waitFor(() => expect(create).toBeEnabled());
    await user.type(screen.getByLabelText("Rationale (optional)"), "Dip under the 20-day");
    await user.click(create);

    await waitFor(() =>
      expect(api.createTrade).toHaveBeenCalledWith({
        ticker: "XLE",
        status: "Planned",
        setup: "Pullback",
        shares: 15,
        entryPrice: 62.82,
        stopPrice: 61.4,
        targetPrice: 65.9,
        rationale: "Dip under the 20-day",
      }),
    );
    expect(await screen.findByTestId("created-message")).toHaveTextContent("Planned trade saved");
  });

  it("will not create a trade when the verdict is not OK, and says why", async () => {
    api.size.mockResolvedValue(
      sizerResult({ verdict: "SKIP - one share exceeds the position cap", isOk: false, shares: 0 }),
    );
    const user = userEvent.setup();
    renderSizer("trader", "/sizer?ticker=SPY&entry=869");

    await user.type(screen.getByLabelText("Stop"), "850");
    await user.type(screen.getByLabelText("Target"), "900");
    await screen.findByTestId("sizer-verdict", {}, { timeout: 3000 });

    expect(screen.getByRole("button", { name: "Create planned trade" })).toBeDisabled();
    expect(screen.getByText(/verdict is not OK/)).toBeInTheDocument();
  });

  it("shows a 400 from creating the trade against the form", async () => {
    api.createTrade.mockRejectedValue(
      httpError(400, { errors: { Ticker: ["Unknown ticker XLE; add it as an instrument first."] } }),
    );
    const user = userEvent.setup();
    renderSizer("trader", "/sizer?ticker=XLE&entry=62.82");
    await user.type(screen.getByLabelText("Stop"), "61.40");
    await user.type(screen.getByLabelText("Target"), "65.90");
    await screen.findByTestId("sizer-verdict", {}, { timeout: 3000 });
    await waitFor(() => expect(screen.getByRole("button", { name: "Create planned trade" })).toBeEnabled());

    await user.click(screen.getByRole("button", { name: "Create planned trade" }));

    expect(await screen.findByText("Please correct the highlighted fields.")).toBeInTheDocument();
  });

  it("lets a reader calculate but not save", async () => {
    const user = userEvent.setup();
    renderSizer("reader");

    await enter(user, "62.82", "61.40", "65.90");

    expect(await screen.findByTestId("sizer-verdict", {}, { timeout: 3000 })).toHaveTextContent("OK - log it");
    expect(screen.queryByRole("button", { name: "Create planned trade" })).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Rationale (optional)")).not.toBeInTheDocument();
  });
});

describe("useSizer", () => {
  it("does not call the API until all three prices are positive numbers", () => {
    const { result } = renderHook(() => useSizer({ entry: "10", stop: "", target: "12" }, 0));
    expect(result.current.ready).toBe(false);
    expect(api.size).not.toHaveBeenCalled();

    const zero = renderHook(() => useSizer({ entry: "10", stop: "0", target: "12" }, 0));
    expect(zero.result.current.ready).toBe(false);
  });

  it("debounces: only the last of several quick changes is sent", async () => {
    const { rerender } = renderHook(({ entry }) => useSizer({ entry, stop: "9", target: "13" }, 30), {
      initialProps: { entry: "10" },
    });
    rerender({ entry: "10.1" });
    rerender({ entry: "10.2" });

    await waitFor(() => expect(api.size).toHaveBeenCalledTimes(1));
    expect(api.size).toHaveBeenCalledWith({ ticker: undefined, entry: 10.2, stop: 9, target: 13 });
  });

  it("ignores a slow response that arrives after a newer request", async () => {
    let resolveFirst: (v: ReturnType<typeof sizerResult>) => void = () => undefined;
    api.size.mockImplementationOnce(() => new Promise((resolve) => (resolveFirst = resolve)));
    api.size.mockResolvedValueOnce(sizerResult({ shares: 7 }));
    const { result, rerender } = renderHook(({ entry }) => useSizer({ entry, stop: "9", target: "13" }, 0), {
      initialProps: { entry: "10" },
    });
    await waitFor(() => expect(api.size).toHaveBeenCalledTimes(1));

    rerender({ entry: "11" });
    await waitFor(() => expect(result.current.result?.shares).toBe(7));
    await act(async () => resolveFirst(sizerResult({ shares: 99 })));

    expect(result.current.result?.shares).toBe(7);
  });
});
