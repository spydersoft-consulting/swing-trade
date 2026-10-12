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
