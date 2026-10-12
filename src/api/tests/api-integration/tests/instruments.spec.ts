import { ACTIVE_TICKERS } from "./market-data";
import { expect, expectFieldErrors, json, test, uniqueTicker } from "./fixtures";
import type { InstrumentDto } from "./types";

test("GET /instruments lists the seeded watchlist and candidates", async ({ api }) => {
  const all = await json<InstrumentDto[]>(await api.reader.get("/api/v1/instruments"));
  const active = await json<InstrumentDto[]>(await api.reader.get("/api/v1/instruments?active=true"));
  const inactive = await json<InstrumentDto[]>(await api.reader.get("/api/v1/instruments?active=false"));

  expect(all.length).toBeGreaterThanOrEqual(24);
  expect(active.map((i) => i.ticker).sort()).toEqual([...ACTIVE_TICKERS].sort());
  expect(inactive.map((i) => i.ticker)).toEqual(expect.arrayContaining(["XLP", "XLB", "XLRE", "XLC", "SMH", "IBB", "KRE", "ITA", "XOP", "SPLG", "QQQM", "GLDM"]));
  expect(active.filter((i) => i.isBenchmark).map((i) => i.ticker)).toEqual(["SPY"]);
  expect(all.map((i) => i.ticker)).toEqual([...all.map((i) => i.ticker)].sort((a, b) => (a < b ? -1 : 1)));
});

test("POST /instruments as trader creates an instrument (ticker normalised to upper case)", async ({ api, tracked }) => {
  const ticker = uniqueTicker("n");

  const created = await tracked.instrument(ticker.toLowerCase(), "Normalised");

  expect(created.ticker).toBe(ticker);
  expect(created.name).toBe("Normalised");
  expect(created.active).toBe(true);
  const listed = await json<InstrumentDto[]>(await api.reader.get("/api/v1/instruments"));
  expect(listed.some((i) => i.ticker === ticker)).toBe(true);
});

test("POST /instruments rejects a duplicate with 409 and bad input with 400 field errors", async ({ api, tracked }) => {
  const ticker = uniqueTicker("d");
  await tracked.instrument(ticker);

  const duplicate = await api.trader.post("/api/v1/instruments", { data: { ticker, name: "Again" } });
  expect(duplicate.status()).toBe(409);

  await expectFieldErrors(await api.trader.post("/api/v1/instruments", { data: { ticker: "bad ticker!", name: "" } }), "Ticker", "Name");
});

test("PUT /instruments/{ticker} edits and deactivates; an unknown ticker is 404", async ({ api, tracked }) => {
  const ticker = uniqueTicker("u");
  await tracked.instrument(ticker);

  const updated = await json<InstrumentDto>(
    await api.trader.put(`/api/v1/instruments/${ticker}`, {
      data: { name: "Renamed", category: "Test", active: false, washSaleGroup: "TESTGRP" },
    }),
  );

  expect(updated).toMatchObject({ ticker, name: "Renamed", category: "Test", active: false, washSaleGroup: "TESTGRP" });
  const active = await json<InstrumentDto[]>(await api.reader.get("/api/v1/instruments?active=true"));
  expect(active.some((i) => i.ticker === ticker)).toBe(false);

  expect((await api.trader.put("/api/v1/instruments/NOPE9", { data: { name: "x" } })).status()).toBe(404);
  await expectFieldErrors(await api.trader.put(`/api/v1/instruments/${ticker}`, { data: { name: " " } }), "Name");
});

test("a reader cannot add or edit instruments", async ({ api }) => {
  expect((await api.reader.post("/api/v1/instruments", { data: { ticker: uniqueTicker(), name: "Nope" } })).status()).toBe(403);
  expect((await api.reader.put("/api/v1/instruments/SPY", { data: { name: "Nope" } })).status()).toBe(403);
});

test("a new active instrument appears in the watchlist as Insufficient data and is stale until it has bars", async ({ api, tracked }) => {
  const ticker = uniqueTicker("w");
  await tracked.instrument(ticker);

  const rows = await json<{ ticker: string; signal: string; asOfDate: string | null; stale: boolean }[]>(
    await api.reader.get("/api/v1/watchlist"),
  );

  expect(rows.find((r) => r.ticker === ticker)).toMatchObject({ signal: "InsufficientData", asOfDate: null, stale: true });
});
