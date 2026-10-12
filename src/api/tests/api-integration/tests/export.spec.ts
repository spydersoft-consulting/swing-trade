import { parseCsv } from "./csv";
import { expect, json, test } from "./fixtures";
import type { TradeDto } from "./types";

const HEADER = [
  "Id", "Ticker", "Status", "Setup", "OpenDate", "Shares", "EntryPrice", "StopPrice", "TargetPrice", "ExitPrice",
  "ExitDate", "Position", "RiskDollars", "RiskPct", "PlannedRewardRisk", "Pnl", "PnlPct", "RMultiple", "DaysHeld",
  "WashSaleFlag", "FollowedPlan", "Rationale", "Lessons",
];

test("GET /export/trades.csv is a CSV attachment with a dated filename", async ({ api }) => {
  const response = await api.reader.get("/api/v1/export/trades.csv");

  expect(response.status()).toBe(200);
  expect(response.headers()["content-type"]).toContain("text/csv");
  expect(response.headers()["content-type"]).toContain("charset=utf-8");
  expect(response.headers()["content-disposition"]).toMatch(/attachment;.*swing-trade-trades-\d{8}\.csv/);
  const text = await response.text();
  expect(text.startsWith("Id,Ticker,Status")).toBe(true);
  expect(text.endsWith("\r\n")).toBe(true);
});

test("the seeded XLE row carries the computed fields", async ({ api }) => {
  const rows = parseCsv(await (await api.reader.get("/api/v1/export/trades.csv")).text());
  const [header, ...data] = rows;
  expect(header).toEqual(HEADER);

  const col = (row: string[], name: string) => row[HEADER.indexOf(name)];
  const xle = data.find((r) => col(r, "Ticker") === "XLE" && col(r, "OpenDate") === "2026-10-05")!;
  expect(xle).toBeDefined();
  expect(col(xle, "Status")).toBe("Closed");
  expect(col(xle, "Shares")).toBe("15");
  expect(col(xle, "EntryPrice")).toBe("62.34");
  expect(col(xle, "StopPrice")).toBe("61.40");
  expect(col(xle, "TargetPrice")).toBe("65.90");
  expect(col(xle, "ExitPrice")).toBe("65.91");
  expect(col(xle, "ExitDate")).toBe("2026-10-12");
  expect(col(xle, "Position")).toBe("935.10");
  expect(col(xle, "RiskDollars")).toBe("14.10");
  expect(col(xle, "PlannedRewardRisk")).toBe("3.787");
  expect(col(xle, "Pnl")).toBe("53.55");
  expect(col(xle, "RMultiple")).toBe("3.798");
  expect(col(xle, "DaysHeld")).toBe("7");
  expect(col(xle, "WashSaleFlag")).toBe("false");
  expect(col(xle, "FollowedPlan")).toBe("true");
  expect(col(xle, "Rationale")).toContain("borderline practice trade");
});

test("the export matches the trade log, row for row", async ({ api, tracked }) => {
  await tracked.trade({ ticker: "XLK", shares: 5, entryPrice: 192, stopPrice: 187.5, targetPrice: 202 });
  await tracked.trade({ ticker: "XLF", status: "Open", openDate: "2026-10-09", shares: 10, entryPrice: 50, stopPrice: 48, targetPrice: 55 });
  const trades = await json<TradeDto[]>(await api.reader.get("/api/v1/trades"));

  const [header, ...data] = parseCsv(await (await api.reader.get("/api/v1/export/trades.csv")).text());
  const idx = (name: string) => header.indexOf(name);

  expect(data.map((r) => Number(r[idx("Id")])).sort((a, b) => a - b)).toEqual(trades.map((t) => t.id).sort((a, b) => a - b));
  for (const trade of trades) {
    const row = data.find((r) => Number(r[idx("Id")]) === trade.id)!;
    expect(row[idx("Ticker")]).toBe(trade.ticker);
    expect(row[idx("Status")]).toBe(trade.status);
    expect(Number(row[idx("Position")])).toBeCloseTo(trade.position, 2);
    expect(Number(row[idx("RiskDollars")])).toBeCloseTo(trade.riskDollars, 2);
    expect(row[idx("WashSaleFlag")]).toBe(String(trade.washSaleFlag));
    if (trade.pnl === null) expect(row[idx("Pnl")]).toBe("");
    else expect(Number(row[idx("Pnl")])).toBeCloseTo(trade.pnl, 2);
  }
});

test("free text with commas, quotes and line breaks is escaped per RFC 4180", async ({ api, tracked }) => {
  const rationale = 'Dip, "buy" it\nsecond line, with comma';
  const trade = await tracked.trade({ ticker: "XLK", shares: 5, entryPrice: 192, stopPrice: 187.5, targetPrice: 202, rationale });

  const text = await (await api.reader.get("/api/v1/export/trades.csv")).text();
  expect(text).toContain('"Dip, ""buy"" it\nsecond line, with comma"');

  const [header, ...data] = parseCsv(text);
  const row = data.find((r) => Number(r[header.indexOf("Id")]) === trade.id)!;
  expect(row).toHaveLength(HEADER.length);
  expect(row[header.indexOf("Rationale")]).toBe(rationale);
});

test("numbers use a decimal point and no thousands separator whatever the server culture", async ({ api, tracked }) => {
  const trade = await tracked.trade({ ticker: "SPY", shares: 2, entryPrice: 1234.5, stopPrice: 1200, targetPrice: 1300 });

  const [header, ...data] = parseCsv(await (await api.reader.get("/api/v1/export/trades.csv")).text());
  const row = data.find((r) => Number(r[header.indexOf("Id")]) === trade.id)!;

  expect(row[header.indexOf("EntryPrice")]).toBe("1234.50");
  expect(row[header.indexOf("Position")]).toBe("2469.00");
  expect(row[header.indexOf("PlannedRewardRisk")]).toBe("1.899");
});
