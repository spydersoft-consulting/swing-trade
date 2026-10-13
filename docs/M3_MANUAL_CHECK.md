# M3 manual check (real provider, opt-in)

The unit tests use stubs and the fake provider, so nothing in M3 has been run against Alpaca or Tiingo. M3's "Done when" (the 12 active tickers load for real and their MA20/MA50 look right) needs your keys and a Postgres database. Do this once; no automated test depends on it.

## 1. Keys

- Alpaca: a free account (a paper account is enough). Create an API key pair. Check that `feed=sip` works on your plan (step 4).
- Tiingo (optional fallback): a free account and API token.

Keep keys out of the repo. Locally use user-secrets (the API project has `UserSecretsId` `swing-trade-api`):

```bash
cd src/api/src/Spydersoft.SwingTrade.Api
dotnet user-secrets set "MarketData:Providers:0" "alpaca"
dotnet user-secrets set "MarketData:Providers:1" "tiingo"        # optional fallback
dotnet user-secrets set "MarketData:Alpaca:KeyId" "<key id>"
dotnet user-secrets set "MarketData:Alpaca:SecretKey" "<secret>"
dotnet user-secrets set "MarketData:Tiingo:Token" "<token>"      # only if tiingo is listed
```

The same settings as environment variables (this is how the Kubernetes Secret should provide them):

```text
MarketData__Providers__0=alpaca
MarketData__Providers__1=tiingo
MarketData__Alpaca__KeyId=...
MarketData__Alpaca__SecretKey=...
MarketData__Tiingo__Token=...
```

Optional tuning: `MarketData__Alpaca__Feed` (default `sip`; `iex` can give closes that differ from the official close), `MarketData__<Name>__MinDelayMs`, `MarketData__MaxAttempts`.

`appsettings.Development.json` lists `fake` as the only provider. A list in user-secrets or env is merged by index, so set every index you need (and if you list only one provider, index 0 replaces `fake`). The API refuses to start if a listed provider has a missing secret or the list is empty.

## 2. Run

```bash
export ConnectionStrings__swingtrade-db="Host=localhost;Database=swingtrade;Username=postgres;Password=..."
export ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src/api/src/Spydersoft.SwingTrade.Api
```

You need a bearer token with the `swingtrade.admin` role for the POST (see `docs/IDENTITY_SETUP.md`); `swingtrade.reader` is enough for the GETs.

## 3. Trigger a refresh and watch it

```bash
curl -s -X POST https://localhost:PORT/api/v1/refresh -H "Authorization: Bearer $TOKEN"
# 202 {"id":1,"status":"Running",...}   (409 if a run is already active)

curl -s "https://localhost:PORT/api/v1/refresh/runs/1" -H "Authorization: Bearer $TOKEN"
curl -s "https://localhost:PORT/api/v1/refresh/runs?page=1&pageSize=5" -H "Authorization: Bearer $TOKEN"
```

Expected for a healthy run: `status` `Succeeded`, `tickersOk` 12 (the 12 active tickers; SPY is one of them), `tickersFailed` 0, `provider` `alpaca`. A first run backfills 200 calendar days per ticker, about 140 bars each. If a ticker failed, `message` names it and the reason; if Tiingo covered for Alpaca it says "XLK via tiingo".

## 4. Verify the data

In Postgres:

```sql
select "Ticker", max("Date") as last_bar, count(*) as bars from "PriceBars" group by 1 order by 1;
select "Ticker", "AsOfDate", "LastClose", "Ma20", "Ma50", "BarsUsed" from "IndicatorSnapshots" order by 1;
```

Check:

1. `last_bar` is the latest completed session for every ticker (a bar for today only appears after 16:15 New York, never an intraday one).
2. `BarsUsed` is 50 and `Ma20`, `Ma50` are filled in.
3. Optionally compare `LastClose`, `Ma20`, `Ma50` for a few tickers with any charting tool using plain (not dividend-adjusted) closes. You decided not to cross-check against broker charts (DECISIONS #36), so this is optional. If Alpaca rejects `feed=sip` on your plan the run fails with an "unavailable" reason in `message`; that is the thing to look for.
4. Run the POST again: `tickersOk` is 12 and the `PriceBars` row count does not grow (idempotent).

## 5. Schedule

The hosted service runs at 17:30 and 20:30 New York on weekdays and at 08:00 (Tuesday to Saturday) if the latest bar is still missing. The log line "Next Primary refresh at ..." shows the next time. It does not run at startup, so do step 3 once after a first deploy.

## 6. Quick path with the Local profile and the verification script

Use this instead of steps 2 to 4 when you run the AppHost `Local` profile (API on http://localhost:8180, mock IdP on 8210; see `docs/apphost-launch-profiles.md`).

1. Put the keys in the API project's user-secrets (step 1). Local reads them because the API project's `UserSecretsId` is `swing-trade-api`. Replace the fake provider by setting index 0 (`MarketData:Providers:0` = `alpaca`), and index 1 (`tiingo`) only if you want the fallback. Never commit keys.
2. Start Local: `dotnet run --project src/Spydersoft.SwingTrade.AppHost --launch-profile Local`. If the API exits at startup, a listed provider lacks a secret.
3. Run the script (it starts nothing; it mints an admin token, triggers `POST /api/v1/refresh`, polls the run, then prints a table):
   - PowerShell: `./scripts/verify-real-data.ps1` (options: `-NoRefresh`, `-StaleDays 4`, `-TimeoutSec 300`)
   - bash (needs curl and jq): `scripts/verify-real-data.sh` (`--no-refresh`; env `API`, `IDP`, `STALE_DAYS`)
   It prints the RefreshRun status, provider and failed-ticker message, then per ticker: as-of date, last close, MA20, MA50, trend, signal, bar count, and flags `BARS<50`, `OLD ASOF`, `STALE`, `NO MA50`, `NO DATA`. Exit code 1 if the run did not succeed, any ticker is flagged, or there are not 12 tickers. (Bar count is read from the bars endpoint, capped at 120.)
4. Compare with a broker chart (daily candles, plain closes, no dividend adjustment) for at least SPY and two other tickers:
   - last close and as-of date must equal the broker's last completed session close;
   - MA20 and MA50 (simple, on close) should agree within a few cents. A larger gap usually means the broker chart includes today's partial bar, uses adjusted closes, or uses a different MA type.
5. Run the script again with `-NoRefresh`/`--no-refresh` after a second POST: values unchanged and `PriceBars` row count unchanged (idempotent).

### If `feed=sip` is rejected

The run ends `Failed` or `Partial` and `message` names the tickers with an "unavailable" reason (HTTP 403/422 style subscription error from Alpaca, or 429/5xx). With only Alpaca listed, every ticker fails and the table shows no data or `OLD ASOF`. Options, in order:

1. Fallback to Tiingo: create a token, `dotnet user-secrets set "MarketData:Providers:1" "tiingo"` and `"MarketData:Tiingo:Token" "<token>"`, restart, rerun. A covered ticker shows in the message as "XLK via tiingo". Tiingo raw close is used, so values should match the broker to the cent.
2. IEX for a smoke test only: `dotnet user-secrets set "MarketData:Alpaca:Feed" "iex"`. IEX closes can differ from the official close, so do not sign off MA values on IEX; it only proves the wiring and bar counts.
3. Record which provider you verified in the table below.

### Results template

Run: date ____ provider ____ feed ____ RefreshRun id ____ status ____ tickersOk ____ / tickersFailed ____

| Ticker | As-of | Last (API) | Last (broker) | MA20 (API) | MA20 (broker) | MA50 (API) | MA50 (broker) | Bars | OK? |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPY | | | | | | | | | |
| (other 11 tickers) | | | | | | | | | |

Done when: 12 tickers, none flagged, status `Succeeded`, broker comparison within a few cents for the sampled tickers, second run idempotent.
