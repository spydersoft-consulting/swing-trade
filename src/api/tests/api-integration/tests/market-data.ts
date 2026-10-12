// What the canned WireMock bars (wiremock/mappings, from scripts/generate-wiremock.mjs) produce under the default
// settings. The generator checks the same table against the plan's formulas before it writes the mappings.
import type { SignalName, TrendName } from "./types";

export const ACTIVE_TICKERS = ["SPY", "QQQ", "IWM", "DIA", "XLK", "XLF", "XLE", "XLV", "XLY", "XLI", "XLU", "GLD"] as const;

/** Fails with HTTP 500 from both providers: recorded in the run, every other ticker still loads. */
export const FAILING_TICKER = "XLU";

/** Alpaca answers 404 for it, so the run falls back to Tiingo. */
export const TIINGO_TICKER = "GLD";

/** The one Pullback-candidate shape. */
export const PULLBACK_TICKER = "QQQ";

export const LAST_CANNED_BAR = "2026-10-09";

export const EXPECTED: Record<string, { trend: TrendName; signal: SignalName; bars: number }> = {
  SPY: { trend: "Uptrend", signal: "Watch", bars: 70 },
  QQQ: { trend: "Uptrend", signal: "PullbackCandidate", bars: 70 },
  IWM: { trend: "Uptrend", signal: "ExtendedWait", bars: 70 },
  DIA: { trend: "Uptrend", signal: "Watch", bars: 70 },
  XLK: { trend: "Uptrend", signal: "Watch", bars: 70 },
  XLF: { trend: "Downtrend", signal: "Avoid", bars: 70 },
  XLE: { trend: "Mixed", signal: "Watch", bars: 70 },
  XLV: { trend: "Mixed", signal: "Watch", bars: 70 },
  XLY: { trend: "Uptrend", signal: "Watch", bars: 70 },
  XLI: { trend: "InsufficientData", signal: "InsufficientData", bars: 49 },
  GLD: { trend: "Uptrend", signal: "Watch", bars: 70 },
};

/**
 * The most recent weekday whose close is complete: after 16:15 America/New_York on the day itself, else an earlier
 * day. The same rule as MarketCalendar.LatestExpectedBar, written independently here for the stale-flag check.
 */
export function latestExpectedBar(now = new Date()): string {
  const parts = new Intl.DateTimeFormat("en-CA", {
    timeZone: "America/New_York",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hourCycle: "h23",
  }).formatToParts(now);
  const get = (t: string) => Number(parts.find((p) => p.type === t)!.value);
  const minutes = get("hour") * 60 + get("minute");
  const day = new Date(Date.UTC(get("year"), get("month") - 1, get("day")));
  if (!(minutes > 16 * 60 + 15)) day.setUTCDate(day.getUTCDate() - 1);
  while (day.getUTCDay() === 0 || day.getUTCDay() === 6) day.setUTCDate(day.getUTCDate() - 1);
  return day.toISOString().slice(0, 10);
}
