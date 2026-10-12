# Market data provider options

Checked 2026-10-12 against the vendors' own pages where they could be fetched. Items marked **(unverified)** came from secondary sources or were blocked (HTTP 403) and must be confirmed with a free key before the decision is final. Free tiers change; keep every provider behind the interface.

Requirement recap: daily OHLCV for 12-24 US ETFs, once per weekday evening, ~200 calendar days backfill, **split-adjusted, not dividend-adjusted** close, one user, personal use, unattended in Kubernetes, latest close within cents of E*TRADE charts.

## Comparison

| Provider | Free-tier limits | History on free | Split-only vs dividend-adjusted | Auth | Personal storage terms | Unattended reliability | Daily-bar endpoint |
| --- | --- | --- | --- | --- | --- | --- | --- |
| **Alpaca Market Data** | IEX feed only unless subscribed; 200 historical calls/min | Since 2016; SIP/IEX bars older than 15 min are returned on free (forum-confirmed) | Selectable: `adjustment=raw\|split\|dividend\|spin-off\|all` (default `raw`) | Key id + secret headers (needs a free Alpaca account; paper account is enough) | Not verified; data licence is for the account holder (read the Market Data Agreement) | High: documented API, generous limits | `GET https://data.alpaca.markets/v2/stocks/{symbol}/bars?timeframe=1Day&start&end&adjustment=split&feed=iex&limit=10000` (paginate `next_page_token`; multi-symbol `/v2/stocks/bars?symbols=`) |
| **Tiingo** | 50 req/hour, 1,000 req/day, 500 unique symbols/month, 1 GB/month | Decades | Both in every row: `close` (raw), `adjClose` (split+dividend), `splitFactor`, `divCash`. No split-only series; see "Normalization" | API token (header `Authorization: Token x` or `?token=`) | Free tier is internal/personal use only; no display or sharing | High; stable, long-running API | `GET https://api.tiingo.com/tiingo/daily/{ticker}/prices?startDate&endDate&format=json` |
| **Alpha Vantage** | 25 req/day (secondary sources; 5/min) | `compact` = latest 100 points only on free; `outputsize=full` is premium | `TIME_SERIES_DAILY` is free but raw (not split-adjusted); `TIME_SERIES_DAILY_ADJUSTED` is premium | `apikey` query param | PDF terms not readable; assume personal use only | Medium; 25/day too tight for 12-24 tickers plus backfill (200 days > 100 points) | `GET https://www.alphavantage.co/query?function=TIME_SERIES_DAILY&symbol=&outputsize=compact&apikey=` |
| **Stooq CSV** | Daily hit limit; reports of IP blocks for ~1 week after overage (secondary, **unverified**) | Decades | Provider-adjusted for splits; dividends not adjusted (not verified for ETFs) | As of 2026 an apikey obtained via CAPTCHA is reportedly required (secondary, **unverified**) | No clear terms found; informal service | Low: undocumented, CAPTCHA, IP blocking reports | `https://stooq.com/q/d/l/?s=spy.us&i=d&d1=YYYYMMDD&d2=YYYYMMDD&apikey=` |
| **Twelve Data** | Basic: 8 credits/min, 800/day; US equities and ETFs included | Not stated on page; `outputsize` up to 5000 | Selectable: `adjust=splits\|dividends\|all\|none` (default `splits`, which is exactly what we want) | `apikey` param or header `Authorization: apikey x` (header recommended) | Not verified; check ToS before relying on it | High; 1 credit per symbol-call so 24 tickers = 24 credits/night | `GET https://api.twelvedata.com/time_series?symbol=SPY&interval=1day&start_date&end_date&adjust=splits&outputsize=5000` |
| **Massive (formerly Polygon)** | Stocks Basic: 5 calls/min, EOD data only | 2 years | `adjusted=true` (default) adjusts for splits only; `false` = raw. Split-only is the default | API key (query or Bearer) | Not verified | High; 5/min fits sequential 24 tickers in ~5 min | `GET https://api.massive.com/v2/aggs/ticker/{t}/range/1/day/{from}/{to}?adjusted=true&sort=asc&limit=5000` (host name assumed, **unverified**; docs page renamed from Polygon) |
| **Financial Modeling Prep** | 250 calls/day, 500 MB/30 days (secondary) | ~5 years (secondary) | Offers raw/split/split+dividend variants, tier availability **unverified** (official site returned 403) | `apikey` param | Redistribution needs agreement; personal storage unverified | Medium; confirm endpoint on free plan | `/stable/historical-price-eod/...` variants (**unverified**) |
| **Yahoo (unofficial chart endpoint / yfinance)** | None documented | Long | `close` split-adjusted, `adjclose` separate | None (unofficial, cookie/crumb issues) | Yahoo's API terms: no income from APIs, user data deleted in 24h unless stated storable, can rate-limit at will. The public chart endpoint is not a licensed API | Low: breaks without notice, 429s from cloud IPs | `https://query1.finance.yahoo.com/v8/finance/chart/SPY?range=1y&interval=1d` |
| **E\*TRADE API** | Free with account; throttled | n/a | n/a | OAuth 1.0a, tokens die daily | See verdict below | Not viable unattended | **No historical bars endpoint** |

## Is the latest close final?

- **Tiingo** (documented): most US EOD prices available around 17:30 ET; exchanges may send corrections until 20:00 ET and Tiingo updates throughout the evening. So a 17:30 pull can be revised; the planned 20:30 retry will pick up the final value. Make the upsert overwrite (it already upserts on Ticker, Date).
- **Alpaca**: SIP daily bars older than 15 minutes are consolidated and should be stable, but the IEX free feed reflects only IEX-venue volume and its OHLC/close can differ from the consolidated close (official close is the primary-exchange closing auction, not IEX last trade). **Do not use IEX for the close**; request `feed=sip` (allowed on free for data older than 15 min) and keep `end` at least 15 minutes in the past. Alpaca does not publish a corrections window; assume the same 20:00 ET settling.
- Others: not documented; treat the first value after 17:30 as provisional and re-pull the last 5 days (already in the plan, step 2).

## Recommendation

**Primary: Alpaca Market Data, `feed=sip`, `adjustment=split`, `timeframe=1Day`.**
- The only option where split-only is an explicit, documented parameter and the consolidated SIP feed is available free for historical data. 200 calls/minute makes 24 sequential tickers (or one multi-symbol call) trivial. Deep history for backfill in a single paginated call.
- Caveats: needs an Alpaca account and key (paper account suffices), and you must verify `feed=sip` actually succeeds on your free account on first run, and that its data licence permits personal DB storage.

**Fallback: Tiingo** (free, personal/internal use explicitly licensed, documented EOD timing and corrections). Because Tiingo gives raw `close` plus `splitFactor` but its `adjClose` includes dividends, normalize to split-only by computing `close * cumulative-split-factor-after-date` from the `splitFactor` column across the requested window, or, simpler for recent ETF data, use raw `close` (any split inside the 200-day window is rare for these ETFs; log a warning and force a full re-pull when `splitFactor != 1` appears). Never store `adjClose` as Close.

**Second fallback if wanted: Twelve Data** with `adjust=splits` (default) is the cleanest split-only semantics after Alpaca; 800 credits/day is ample. Skip Alpha Vantage (25/day, 100-point free window), Stooq (CAPTCHA/IP blocks, undocumented), FMP (unverified tier), Yahoo (terms and fragility). Massive free is viable (5/min, 2-year history, split-only default) but is the least needed.

Acceptance test before locking in: for SPY, QQQ, IWM and one ETF with a recent ex-dividend date, compare the last 5 closes from each provider against the E*TRADE chart; expect an exact match to the cent for SIP/Tiingo raw close.

## Extensible provider pattern

```csharp
public interface IMarketDataProvider
{
    string Name { get; }                       // "alpaca", "tiingo", "fake"
    TimeSpan MinDelayBetweenCalls { get; }     // rate-limit spacing, owned by the provider
    Task<IReadOnlyList<DailyBar>> GetDailyBars(string ticker, DateOnly from, DateOnly to, CancellationToken ct);
}
public sealed record DailyBar(DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close, long Volume);
```

- **Contract for every implementation:** return bars sorted ascending, one per trading date (NY exchange date, not UTC), `Close` = **split-adjusted, dividend-unadjusted**. Each provider owns the translation (Alpaca `adjustment=split`; Twelve Data `adjust=splits`; Massive `adjusted=true`; Tiingo raw `close` times split factor). Never return a provider's dividend-adjusted field. Return an empty list on holidays; throw a typed `ProviderUnavailableException` (HTTP 429/5xx/timeouts) vs `TickerNotFoundException` so the registry knows whether to fall back.
- **Registry:** `MarketData:Providers` is an ordered list (e.g. `["alpaca","tiingo"]`) plus `"fake"` for tests. A `FallbackMarketDataProvider : IMarketDataProvider` wraps the ordered list, tries each per ticker, falls through on `ProviderUnavailableException` or an empty result when a bar is expected, and records which provider supplied each bar (`Bar.Source`). Providers register via DI keyed services; the factory resolves names from config and fails fast at startup on an unknown name or missing secret.
- **Rate-limit delay:** the job awaits `MinDelayBetweenCalls` between calls to the same provider (config override per provider: Alpaca 0.1 s, Tiingo 1 s well under 50/hour only matters for large lists, Twelve Data 8 s, Massive 13 s). Honor `Retry-After` on 429. Retries: 3 with backoff per ticker, per the plan.
- **Sanity check across providers:** on fallback, compare the overlap bar (previous stored close) with the new provider's value for the same date; if they differ by more than a cent, log a warning and flag the run so a basis mismatch is not silent.
- **Secret config keys** (Kubernetes Secret, env vars, never in the repo):
  `MarketData__Alpaca__KeyId`, `MarketData__Alpaca__SecretKey`, `MarketData__Alpaca__Feed` (default `sip`), `MarketData__Tiingo__Token`, `MarketData__TwelveData__ApiKey`, `MarketData__Massive__ApiKey`; non-secret: `MarketData__Providers__0`, `MarketData__Providers__1`, `MarketData__<Name>__MinDelayMs`.
- **Completed-bar rule** stays in the job, not the provider: provider returns what it has; the job drops any bar for today before 16:15 ET (and Alpaca queries use `end` at least 15 min old).

## E*TRADE verdict

- **Not viable for the nightly job.** (1) The Market API is quote-oriented (`GET /v1/market/quote/{symbols}`, up to 25 symbols, 50 with override): snapshots with last, bid/ask, open/high/low, previous close and volume. No historical OHLC bar endpoint is documented (the official quote page lists none; secondary searches found none). (2) OAuth 1.0a access tokens expire at the end of the US Eastern calendar day and go inactive after 2 hours without requests; renewal revives only an inactive, not an expired, token, so every day needs a human login (browser verifier code). An unattended 17:30 job cannot do this. (3) Real-time quotes need a signed market data agreement; otherwise data is delayed. (4) Production consumer key approval and redistribution/storage terms could not be fetched (developer portal returned 403); treat as unverified and likely restrictive.
- **Possible role:** none automated. Use the broker chart or statement as a manual cross-check of the stored close for a few tickers (the acceptance test above). A later optional manual "paste/import closes from E*TRADE" or a human-initiated quote pull (previous close field) could cross-check, but adds an OAuth login for little value.

## Risks

- Free tiers change (Alpha Vantage cut from 500 to 25/day; Stooq added a key in 2026). Pin behavior with a provider contract test and a nightly staleness badge (already in the plan).
- Alpaca free plan: IEX default for real-time; SIP historical allowed only older than 15 min. Misconfigured `feed` silently gives IEX-venue closes that may differ from the consolidated close.
- Dividend-adjusted contamination: Tiingo `adjClose`, Alpaca `adjustment=all`/`dividend`. Add a unit test asserting an ex-dividend date close equals the raw close.
- Late corrections (to 20:00 ET) can shift the latest close by a cent or more; the 20:30 and next-morning attempts must overwrite, not skip existing rows.
- Terms: no provider's personal-DB-storage clause was confirmed by me for Alpaca, Twelve Data, Massive, FMP, or Alpha Vantage (pages blocked or unreadable); read each before enabling. Tiingo's free tier explicitly allows internal personal use.
- Splits in history: a split makes stored split-adjusted closes stale. Detect `splitFactor != 1` (Tiingo) or a >30% day-over-day break and re-backfill that ticker.
- Single account/key per provider; rotate via the Kubernetes secret.

## Sources

- Alpaca stock bars reference: https://docs.alpaca.markets/reference/stockbars
- Alpaca market data plans: https://docs.alpaca.markets/docs/about-market-data-api
- Alpaca feeds: https://docs.alpaca.markets/docs/historical-stock-data-1
- Alpaca forum on SIP free access older than 15 min: https://forum.alpaca.markets/t/what-does-realtime-data-in-free-account-mean/13165
- Tiingo pricing: https://www.tiingo.com/about/pricing ; EOD docs: https://www.tiingo.com/documentation/end-of-day
- Alpha Vantage docs: https://www.alphavantage.co/documentation/ ; premium: https://www.alphavantage.co/premium/ ; limits (secondary): https://www.macroption.com/alpha-vantage-api-limits/ ; terms (PDF): https://www.alphavantage.co/terms_of_service/
- Stooq key/CAPTCHA reports (secondary): https://github.com/pydata/pandas-datareader/issues/1012
- Twelve Data pricing: https://twelvedata.com/pricing ; docs: https://twelvedata.com/docs
- Massive pricing: https://massive.com/pricing ; custom bars: https://massive.com/docs/rest/stocks/aggregates/custom-bars
- FMP (secondary): https://site.financialmodelingprep.com/insights/platform/how-to-choose-the-right-financial-modeling-prep-plan-for-your-workflow
- Yahoo API terms: https://legal.yahoo.com/us/en/yahoo/terms/product-atos/apiforydn/index.html
- E*TRADE quote API: https://apisb.etrade.com/docs/api/market/api-quote-v1.html ; token renewal: https://apisb.etrade.com/docs/api/authorization/renew_access_token.html ; getting started: https://developer.etrade.com/getting-started
