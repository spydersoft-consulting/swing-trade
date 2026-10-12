// The API has no note delete endpoint (the plan lists GET and POST only), so these specs cannot remove what they add.
// Every note carries a unique marker and every assertion filters on it, so leftovers never affect another spec.
import { expect, expectFieldErrors, json, test } from "./fixtures";
import type { NoteDto } from "./types";

const marker = () => `e2e-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;

test("POST /notes as trader records a pass and GET /notes lists it", async ({ api }) => {
  const text = marker();

  const created = await json<NoteDto>(
    await api.trader.post("/api/v1/notes", { data: { date: "2026-10-09", kind: "Pass", text, ticker: "spy" } }),
    201,
  );
  const listed = await json<NoteDto[]>(await api.reader.get("/api/v1/notes?kind=Pass&from=2026-10-09&to=2026-10-09"));

  expect(created).toMatchObject({ date: "2026-10-09", kind: "Pass", text, ticker: "SPY" });
  expect(created.id).toBeGreaterThan(0);
  expect(listed.find((n) => n.id === created.id)).toEqual(created);
});

test("GET /notes filters by kind, date range and ticker, newest first", async ({ api }) => {
  const text = marker();
  const review = await json<NoteDto>(await api.trader.post("/api/v1/notes", { data: { date: "2026-10-02", kind: "Review", text: `${text}-r`, ticker: "QQQ" } }), 201);
  const general = await json<NoteDto>(await api.trader.post("/api/v1/notes", { data: { date: "2026-10-08", kind: "General", text: `${text}-g` } }), 201);

  const reviews = await json<NoteDto[]>(await api.reader.get("/api/v1/notes?kind=Review"));
  const ranged = await json<NoteDto[]>(await api.reader.get("/api/v1/notes?from=2026-10-07&to=2026-10-09"));
  const byTicker = await json<NoteDto[]>(await api.reader.get("/api/v1/notes?ticker=qqq"));
  const all = await json<NoteDto[]>(await api.reader.get("/api/v1/notes"));

  expect(reviews.every((n) => n.kind === "Review")).toBe(true);
  expect(reviews.some((n) => n.id === review.id)).toBe(true);
  expect(ranged.some((n) => n.id === general.id)).toBe(true);
  expect(ranged.some((n) => n.id === review.id)).toBe(false);
  expect(byTicker.every((n) => n.ticker === "QQQ")).toBe(true);
  expect(byTicker.some((n) => n.id === review.id)).toBe(true);
  const dates = all.map((n) => n.date);
  expect(dates).toEqual([...dates].sort().reverse());
});

test("POST /notes validation: 400 with field errors", async ({ api }) => {
  await expectFieldErrors(await api.trader.post("/api/v1/notes", { data: { date: "2026-10-09", kind: "Pass", text: "  " } }), "Text");
  await expectFieldErrors(await api.trader.post("/api/v1/notes", { data: { kind: "Pass", text: "no date" } }), "Date");
  expect((await api.trader.post("/api/v1/notes", { data: { date: "2026-10-09", kind: "Nonsense", text: "x" } })).status()).toBe(400);
});

test("a reader may read notes but not add them", async ({ api }) => {
  expect((await api.reader.get("/api/v1/notes")).status()).toBe(200);
  expect((await api.reader.post("/api/v1/notes", { data: { date: "2026-10-09", kind: "Pass", text: marker() } })).status()).toBe(403);
});
