// Regenerates wiremock/mappings/*.json: deterministic daily bars for the 12 active tickers.
//   node scripts/generate-wiremock.mjs
// The same file prints (and checks) the Trend, Signal and %-vs-20 each series produces under the default settings,
// using the plan's formulas, so the expectations in tests/market-data.ts stay honest.
import { mkdirSync, rmSync, writeFileSync, readdirSync } from "node:fs";
import path from "node:path";

const outDir = path.join(import.meta.dirname, "..", "wiremock", "mappings");
mkdirSync(outDir, { recursive: true });
for (const f of readdirSync(outDir)) if (f.endsWith(".json")) rmSync(path.join(outDir, f));

const LAST_BAR = "2026-10-09"; // a Friday
const BARS = 70;

function weekdaysEnding(last, count) {
  const dates = [];
  const d = new Date(`${last}T00:00:00Z`);
  while (dates.length < count) {
    const dow = d.getUTCDay();
    if (dow !== 0 && dow !== 6) dates.push(d.toISOString().slice(0, 10));
    d.setUTCDate(d.getUTCDate() - 1);
  }
  return dates.reverse();
}

const ramp = (start, step, n) => Array.from({ length: n }, (_, i) => round(start * (1 + step * i)));
const flat = (value, n) => Array.from({ length: n }, () => value);
const round = (x) => Math.round(x * 100) / 100;

// Each series: the closes, oldest first, plus the signal it must produce.
const series = {
  SPY: { closes: ramp(700, 0.0035, BARS), expect: "Watch" }, // benchmark; uptrend, a little above the pullback band
  QQQ: { closes: ramp(700, 0.001, BARS), expect: "PullbackCandidate" }, // the one pullback shape
  IWM: { closes: ramp(200, 0.012, BARS), expect: "ExtendedWait" },
  DIA: { closes: ramp(400, 0.003, BARS), expect: "Watch" }, // +2.8% vs the 20 day: above the band, below extended
  XLK: { closes: ramp(190, 0.003, BARS), expect: "Watch" },
  XLF: { closes: ramp(50, -0.003, BARS), expect: "Avoid" },
  // Rose for 68 bars, then two sharp down days: MA20 > MA50 but Last < MA50 -> Mixed -> Watch.
  XLE: { closes: [...ramp(100, 0.005, 68), 95, 92], expect: "Watch", trend: "Mixed" },
  XLV: { closes: flat(50, BARS), expect: "Watch", trend: "Mixed" }, // every average equal
  XLY: { closes: ramp(180, 0.004, BARS), expect: "Watch" },
  XLI: { closes: ramp(120, 0.002, 49), expect: "InsufficientData" }, // 49 bars: no MA50
  GLD: { closes: ramp(300, 0.004, BARS), expect: "Watch", via: "tiingo" }, // Alpaca does not know it; Tiingo serves it
  XLU: { closes: [], failing: true }, // HTTP 500 from both providers: recorded as failed, others unaffected
};

function evaluate(closes) {
  if (closes.length < 50) return { trend: "InsufficientData", signal: "InsufficientData" };
  const avg = (n) => closes.slice(-n).reduce((a, b) => a + b, 0) / n;
  const last = closes.at(-1);
  const ma20 = avg(20);
  const ma50 = avg(50);
  const pct = last / ma20 - 1;
  const trend = last > ma50 && ma20 > ma50 ? "Uptrend" : last < ma50 && ma20 < ma50 ? "Downtrend" : "Mixed";
  const signal =
    trend === "Uptrend" && pct >= -0.01 && pct <= 0.02
      ? "PullbackCandidate"
      : trend === "Uptrend" && pct > 0.05
        ? "ExtendedWait"
        : trend === "Downtrend"
          ? "Avoid"
          : "Watch";
  return { trend, signal, pct, last, ma20, ma50 };
}

let failed = false;
const summary = [];
for (const [ticker, s] of Object.entries(series)) {
  if (s.failing) continue;
  const e = evaluate(s.closes);
  const ok = e.signal === s.expect && (!s.trend || e.trend === s.trend);
  failed ||= !ok;
  summary.push({
    ticker,
    bars: s.closes.length,
    trend: e.trend,
    signal: e.signal,
    pctVs20: e.pct === undefined ? "" : (e.pct * 100).toFixed(3) + "%",
    ok,
  });
}
console.table(summary);
if (failed) {
  console.error("A series does not produce its expected signal.");
  process.exit(1);
}

function write(name, mapping) {
  writeFileSync(path.join(outDir, name), JSON.stringify(mapping, null, 2) + "\n");
}

const dates = weekdaysEnding(LAST_BAR, BARS);
const alpacaBars = (closes) =>
  closes.map((c, i) => {
    const date = dates[dates.length - closes.length + i];
    return { t: `${date}T04:00:00Z`, o: c, h: c, l: c, c, v: 1000000, n: 1000, vw: c };
  });
const tiingoBars = (closes) =>
  closes.map((c, i) => {
    const date = dates[dates.length - closes.length + i];
    return {
      date: `${date}T00:00:00.000Z`, open: c, high: c, low: c, close: c, volume: 1000000,
      adjOpen: c, adjHigh: c, adjLow: c, adjClose: c, adjVolume: 1000000, divCash: 0.0, splitFactor: 1.0,
    };
  });

for (const [ticker, s] of Object.entries(series)) {
  const alpacaUrl = `/v2/stocks/${ticker}/bars`;
  const tiingoUrl = `/tiingo/daily/${ticker}/prices`;
  if (s.failing) {
    write(`alpaca-${ticker}-500.json`, {
      priority: 1,
      request: { method: "GET", urlPath: alpacaUrl },
      response: { status: 500, jsonBody: { message: "internal error (canned)" } },
    });
    write(`tiingo-${ticker}-500.json`, {
      priority: 1,
      request: { method: "GET", urlPath: tiingoUrl },
      response: { status: 500, jsonBody: { detail: "internal error (canned)" } },
    });
  } else if (s.via === "tiingo") {
    write(`alpaca-${ticker}-unknown.json`, {
      priority: 1,
      request: { method: "GET", urlPath: alpacaUrl },
      response: { status: 404, jsonBody: { message: "asset not found (canned)" } },
    });
    write(`tiingo-${ticker}-bars.json`, {
      priority: 1,
      request: { method: "GET", urlPath: tiingoUrl },
      response: { status: 200, headers: { "Content-Type": "application/json" }, jsonBody: tiingoBars(s.closes) },
    });
  } else {
    write(`alpaca-${ticker}-bars.json`, {
      priority: 1,
      request: { method: "GET", urlPath: alpacaUrl },
      response: {
        status: 200,
        headers: { "Content-Type": "application/json" },
        jsonBody: { bars: alpacaBars(s.closes), symbol: ticker, next_page_token: null },
      },
    });
  }
}

// Tiingo is only the fallback: for every other ticker it does not know the symbol, so the Alpaca bars stand.
write("tiingo-default-unknown.json", {
  priority: 10,
  request: { method: "GET", urlPathPattern: "/tiingo/daily/[A-Z0-9.-]+/prices" },
  response: { status: 404, jsonBody: { detail: "Not found (canned)" } },
});
// Any other Alpaca symbol (a ticker a spec adds) is unknown.
write("alpaca-default-unknown.json", {
  priority: 10,
  request: { method: "GET", urlPathPattern: "/v2/stocks/[A-Z0-9.-]+/bars" },
  response: { status: 404, jsonBody: { message: "asset not found (canned)" } },
});

console.log(`Wrote mappings to ${outDir}; last bar ${LAST_BAR}, ${BARS} bars.`);
