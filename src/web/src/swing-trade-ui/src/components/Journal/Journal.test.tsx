import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AxiosError, AxiosHeaders } from "axios";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { Journal } from "./Journal";
import { ROUTINE_STEPS, loadRoutine, saveRoutine, totalMinutes } from "./routine";
import { swingTradeApi } from "../../api/swingTradeApi";
import { renderScreen, type Persona } from "../../test/utils";
import { todayInNewYork, weekStart } from "../../utils/dates";
import type { NoteDto } from "../../api/generated/types.gen";

vi.mock("../../api/swingTradeApi", () => ({ swingTradeApi: { notes: vi.fn(), createNote: vi.fn() } }));

const api = vi.mocked(swingTradeApi);
const notes: NoteDto[] = [
  { id: 1, date: "2026-10-05", ticker: null, kind: "Pass", text: "No clean pullback." },
  { id: 2, date: "2026-10-09", ticker: "XLK", kind: "Review", text: "XLK ran away from the band." },
  { id: 3, date: "2026-10-07", ticker: null, kind: "General", text: "Rates meeting Wednesday." },
];

const renderJournal = (persona: Persona = "trader") => renderScreen(<Journal />, { persona });
const week = () => weekStart(todayInNewYork());

beforeEach(() => {
  vi.resetAllMocks();
  localStorage.clear();
  api.notes.mockResolvedValue(notes);
});

describe("weekly routine", () => {
  it("lists the five steps with their time boxes, totalling an hour", async () => {
    renderJournal();
    await screen.findAllByTestId("note");
    const routine = screen.getByRole("region", { name: "Weekly routine" });

    const expected: [string, string][] = [
      ["Review open trades", "10 min"],
      ["Read the watchlist signals", "15 min"],
      ["Inspect candidates on the chart for swing low and prior high", "15 min"],
      ["Size the trade", "10 min"],
      ["Log it and place orders", "10 min"],
    ];
    for (const [label, minutes] of expected) {
      const item = within(routine).getByText(label).closest("li") as HTMLElement;
      expect(item).toHaveTextContent(minutes);
    }
    expect(ROUTINE_STEPS.map((s) => s.minutes)).toEqual([10, 15, 15, 10, 10]);
    expect(totalMinutes).toBe(60);
    expect(screen.getByTestId("routine-progress")).toHaveTextContent("0 of 60 minutes done");
  });

  it("links each step to the screen where it is done", async () => {
    renderJournal();
    await screen.findAllByTestId("note");
    const hrefs = Object.fromEntries(ROUTINE_STEPS.map((s) => [s.id, s.to]));
    expect(hrefs).toMatchObject({
      "review-open": "/trades?tab=open",
      "read-signals": "/watchlist",
      "inspect-candidates": "/watchlist",
      "size-trade": "/sizer",
      "log-orders": "/trades?tab=planned",
    });
    expect(screen.getByRole("link", { name: "Sizer" })).toHaveAttribute("href", "/sizer");
    expect(screen.getByRole("link", { name: "Open trades" })).toHaveAttribute("href", "/trades?tab=open");
    expect(screen.getByRole("link", { name: "Planned trades" })).toHaveAttribute("href", "/trades?tab=planned");
  });

  it("ticks steps, totals the minutes, and stores the state per week in localStorage", async () => {
    const user = userEvent.setup();
    renderJournal();

    await user.click(screen.getByRole("checkbox", { name: "Review open trades" }));
    await user.click(screen.getByRole("checkbox", { name: "Size the trade" }));

    expect(screen.getByTestId("routine-progress")).toHaveTextContent("20 of 60 minutes done");
    expect(JSON.parse(localStorage.getItem(`swingtrade.routine.${week()}`) ?? "[]")).toEqual([
      "review-open",
      "size-trade",
    ]);

    await user.click(screen.getByRole("checkbox", { name: "Review open trades" }));
    expect(screen.getByTestId("routine-progress")).toHaveTextContent("10 of 60 minutes done");
  });

  it("restores this week's ticks on the next visit, and a new week starts empty", async () => {
    saveRoutine(week(), ["read-signals"]);
    saveRoutine("2026-01-05", ["review-open", "size-trade"]);

    renderJournal();
    await screen.findAllByTestId("note");

    expect(screen.getByRole("checkbox", { name: "Read the watchlist signals" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Review open trades" })).not.toBeChecked();
    expect(screen.getByTestId("routine-progress")).toHaveTextContent("15 of 60 minutes done");
  });

  it("resets the week", async () => {
    const user = userEvent.setup();
    saveRoutine(week(), ["read-signals", "size-trade"]);
    renderJournal();

    await user.click(screen.getByRole("button", { name: "Reset week" }));

    expect(screen.getByTestId("routine-progress")).toHaveTextContent("0 of 60 minutes done");
    expect(loadRoutine(week())).toEqual([]);
  });

  it("survives unavailable or corrupt storage", () => {
    localStorage.setItem(`swingtrade.routine.${week()}`, "{not json");
    expect(loadRoutine(week())).toEqual([]);
    localStorage.setItem(`swingtrade.routine.${week()}`, JSON.stringify({ not: "an array" }));
    expect(loadRoutine(week())).toEqual([]);

    const spy = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("blocked");
    });
    expect(() => saveRoutine(week(), ["x"])).not.toThrow();
    spy.mockRestore();
  });
});

describe("notes and pass log", () => {
  it("lists the newest notes first with their kind and ticker", async () => {
    renderJournal();

    const items = await screen.findAllByTestId("note");
    expect(items).toHaveLength(3);
    expect(items[0]).toHaveTextContent("Oct 9, 2026");
    expect(items[0]).toHaveTextContent("Review");
    expect(items[0]).toHaveTextContent("XLK");
    expect(items[2]).toHaveTextContent("Oct 5, 2026");
  });

  it("filters by kind", async () => {
    const user = userEvent.setup();
    renderJournal();
    await screen.findAllByTestId("note");

    await user.click(
      within(screen.getByRole("group", { name: "Filter by kind" })).getByRole("button", { name: "Pass" }),
    );

    const items = screen.getAllByTestId("note");
    expect(items).toHaveLength(1);
    expect(items[0]).toHaveTextContent("No clean pullback.");
  });

  it("records a pass for a trader, as a Pass note dated today", async () => {
    const user = userEvent.setup();
    api.createNote.mockResolvedValue({
      id: 9,
      date: todayInNewYork(),
      ticker: null,
      kind: "Pass",
      text: "Nothing in the band.",
    });
    renderJournal("trader");
    await screen.findAllByTestId("note");

    await user.click(screen.getByRole("button", { name: "Record a pass" }));
    const dialog = await screen.findByRole("dialog");
    await user.type(within(dialog).getByLabelText("Why no trade?"), "Nothing in the band.");
    await user.click(within(dialog).getByRole("button", { name: "Record pass" }));

    await waitFor(() =>
      expect(api.createNote).toHaveBeenCalledWith({
        date: todayInNewYork(),
        kind: "Pass",
        ticker: null,
        text: "Nothing in the band.",
      }),
    );
    expect(await screen.findByText("Nothing in the band.")).toBeInTheDocument();
  });

  it("adds a review note with a ticker", async () => {
    const user = userEvent.setup();
    api.createNote.mockResolvedValue({
      id: 10,
      date: todayInNewYork(),
      ticker: "QQQ",
      kind: "Review",
      text: "Good entry.",
    });
    renderJournal("admin");
    await screen.findAllByTestId("note");

    await user.click(screen.getByRole("button", { name: "Add note" }));
    const dialog = await screen.findByRole("dialog");
    await user.selectOptions(within(dialog).getByLabelText("Kind"), "Review");
    await user.type(within(dialog).getByLabelText("Ticker (optional)"), "qqq");
    await user.type(within(dialog).getByLabelText("Note"), "Good entry.");
    await user.click(within(dialog).getByRole("button", { name: "Save note" }));

    await waitFor(() =>
      expect(api.createNote).toHaveBeenCalledWith({
        date: todayInNewYork(),
        kind: "Review",
        ticker: "QQQ",
        text: "Good entry.",
      }),
    );
  });

  it("maps a 400 onto the note's fields", async () => {
    const user = userEvent.setup();
    api.createNote.mockRejectedValue(
      new AxiosError("x", "E", undefined, undefined, {
        status: 400,
        statusText: "",
        data: { errors: { Text: ["Text is required."] } },
        headers: {},
        config: { headers: new AxiosHeaders() },
      }),
    );
    renderJournal();
    await screen.findAllByTestId("note");
    await user.click(screen.getByRole("button", { name: "Record a pass" }));
    const dialog = await screen.findByRole("dialog");

    await user.click(within(dialog).getByRole("button", { name: "Record pass" }));

    expect(await within(dialog).findByText("Text is required.")).toBeInTheDocument();
    expect(within(dialog).getByLabelText("Why no trade?")).toHaveAttribute("aria-invalid", "true");
  });

  it("hides Record a pass and Add note from a reader, who can still read the log", async () => {
    renderJournal("reader");

    expect(await screen.findAllByTestId("note")).toHaveLength(3);
    expect(screen.queryByRole("button", { name: "Record a pass" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Add note" })).not.toBeInTheDocument();
  });

  it("shows an empty state that invites a pass to a trader", async () => {
    api.notes.mockResolvedValue([]);
    renderJournal("trader");
    expect(await screen.findByText(/A week with no trade is a decision/)).toBeInTheDocument();
  });
});
