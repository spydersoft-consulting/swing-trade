# Swing Trading Journal: Claude Code Build Brief

Oct 12, 2026 · @Matthew Gerega

## Kickoff prompt

Paste this into Claude Code from the new repo's root, after saving this brief as `docs/BUILD_PLAN.md` and replacing `d:\spydersoft\pitstop`. The whole brief is plain text so it copies cleanly into the repo.

```markdown
You are building a personal swing-trading journal and screener: a C# API, a React frontend, and a nightly job that downloads market prices and applies fixed rules. The app may not call any LLM or AI service at runtime. The one AI touchpoint is a hosted, authenticated MCP endpoint that lets my own Claude read the data. Every signal comes from deterministic code and settings I control.

1. Read docs/BUILD_PLAN.md completely. It is the spec: formulas, data model, API, screens, tests, and milestones.
2. Study my reference app at d:\spydersoft\pitstop before writing code.  Study both the source and the helm-config file. Mirror its solution layout, .NET version, database and migrations, auth, logging, Docker and CI setup, frontend tooling, and test frameworks. Where the plan and the reference app disagree on tooling, follow the reference app and record it in docs/DECISIONS.md.
3. Work in plan mode first. Give me a short plan for Milestone 0 and Milestone 1 only, plus any open decision you cannot settle with its recommended default. Wait for my approval.
4. Build one milestone at a time. Write the rules-engine tests first. The fixtures in the plan were verified by hand and must pass exactly. At the end of each milestone, run all tests, commit, and stop for my review.
5. Never change a formula, threshold, or rule on your own. If the plan is ambiguous, use the documented default, record it in docs/DECISIONS.md, and tell me.
6. Money uses decimal. Dates are DateOnly in US market time. All thresholds live in the Settings table, not in code.
```

## Goals and scope

The app replaces the Excel journal: it stores trades and settings, pulls prices every weekday evening, and applies the same trend and signal rules automatically, so nobody types in a price.

**Context.** One user, a roughly $2,000 cash account at E\*TRADE, long-only swing trades in US-listed ETFs, about one hour of attention a week, paper trading first.

**In scope**

- Trades in four states: Planned (limit order working), Open, Closed, Cancelled.
- Automatic daily prices, 20-day and 50-day simple averages, and a Trend and Signal for every active ticker.
- Position sizer, trade math, dashboard metrics, wash-sale flags, tax set-aside, and a SPY benchmark, all calculated server-side.
- A pass log, so a week with no trade is recorded as a decision.
- A hosted MCP endpoint, so Claude can read the journal and help the owner review it and make judgement calls (see the MCP section).
- Parity with the workbook: every number the sheet produced must match the app.

**Out of scope for v1**

- Any LLM or AI call made by the app at runtime. Claude reaches the app through MCP; the app never calls Claude. The app never suggests a trade beyond what the rules produce.
- Order placement, brokerage credentials, or the E\*TRADE API. Trades are typed in by hand.
- Tax or investment advice. The tax rate is a user setting, and the wash-sale flag is informational.
- Multiple users, shorting, margin, options, or individual stocks.

## Reference app and conventions

Claude Code must read the reference app first and copy its conventions; this brief only fills in what that app does not decide.

Before coding, write `docs/REFERENCE_NOTES.md` covering: solution and project layout, .NET target, data access and migrations, database engine, configuration and secrets, authentication, logging and health checks, API style and error format, OpenAPI generation, frontend build tool, UI library, routing, data fetching, state, test frameworks, Dockerfiles and compose, and CI.

Use these defaults only where the reference app has no answer:

| Area | Default |
| --- | --- |
| Backend | Current .NET LTS, ASP.NET Core Web API, EF Core, xUnit |
| Project split | `Journal.Rules` (pure library, no I/O), `Journal.Data`, `Journal.MarketData`, `Journal.Api` (host and hosted service) |
| Database | Same as the reference app; otherwise PostgreSQL |
| Numbers and dates | `decimal` for all money, price and percent math; `DateOnly` for market dates; UTC timestamps for audit fields |
| Time zone | `America/New_York` for scheduling and for what counts as today |
| Frontend | React, TypeScript, Vite, TanStack Query, a client generated from the OpenAPI document, Recharts for charts |
| Settings | Thresholds are database rows, editable in the UI, not appsettings values |
| Deployment | Docker Compose with api, web and database, as in the reference app |

One rule above all: the API, the nightly job, and the UI call `Journal.Rules` for every calculation. None of them reimplements a formula.

## Domain model

Eight tables hold everything: prices are append-only history, and settings, trades, and notes are what the user edits.

| Table | Fields | Notes |
| --- | --- | --- |
| Settings | StartingCapital, MaxRiskPct, MaxOpenPositions, MinRewardRisk, TaxSetAsideRate, WashSaleWindowDays, MaxPositionPct, PullbackBandLow, PullbackBandHigh, ExtendedThreshold, RiskBasis | One row. Defaults: 2000, 0.015, 3, 2.0, 0.27, 30, 0.50, -0.01, 0.02, 0.05, StartingCapital |
| Instrument | Ticker (unique), Name, Category, Active, IsBenchmark, WashSaleGroup (optional) | SPY is the benchmark and is always refreshed |
| PriceBar | Ticker, Date, Open, High, Low, Close, Volume | Unique on (Ticker, Date). Close is split-adjusted, not dividend-adjusted |
| IndicatorSnapshot | Ticker, AsOfDate, LastClose, Ma20, Ma50, BarsUsed | Unique on (Ticker, AsOfDate). Trend and Signal are not stored; they are derived on read from current Settings, so a threshold change applies at once |
| Trade | Id, Ticker, Status, Setup, OpenDate, Shares, EntryPrice, StopPrice, TargetPrice, ExitPrice, ExitDate, FollowedPlan, Rationale, Lessons, CreatedAt, UpdatedAt | Status: Planned, Open, Closed, Cancelled. Setup: Pullback, Breakout, MeanReversion, Other. A Planned trade uses EntryPrice as its limit price and has no OpenDate until it fills |
| JournalNote | Id, Date, Ticker (optional), Kind, Text | Kind: Pass, Review, General. Logs weeks with no trade |
| RefreshRun | Id, StartedAt, FinishedAt, Provider, Status, TickersOk, TickersFailed, Message | Audit trail and staleness checks |
| McpCallLog | Id, At, Tool, Arguments, DurationMs, Caller, Status | Audit of Claude's reads. Never stores tokens. Keep 90 days |

Validation: Entry above Stop; Shares at least 1; Open and Closed trades need an OpenDate; a Closed trade needs an ExitPrice and an ExitDate on or after the OpenDate.

Derived values (position, risk, P&L, R-multiple, and the rest) are never stored. The rules library computes them on read.

**Seed data**

- Settings defaults from the table above.
- Active watchlist: SPY, QQQ, IWM, DIA, XLK, XLF, XLE, XLV, XLY, XLI, XLU, GLD.
- Inactive candidates to review later: XLP, XLB, XLRE, XLC, SMH, IBB, KRE, ITA, XOP, SPLG, QQQM, GLDM.
- One Closed trade: XLE, Pullback, opened 2026-10-05, 15 shares, entry 62.34, stop 61.40, target 65.90, exit 65.91 on 2026-10-12, FollowedPlan true, rationale 'Uptrend, small dip under the 20-day average; borderline practice trade'.

## Rules engine

All logic lives in one pure library, `Journal.Rules`, using exactly these formulas; the workbook's numbers are the acceptance test.

Conventions: `decimal` throughout, percentages stored as fractions (0.015 means 1.5%), rounding only for display (money to 2 places, percent to 1), never between steps.

### Indicators, trend, and signal

```text
Last    = Close of the most recent COMPLETED bar
MA20    = average Close of the 20 most recent completed bars
MA50    = average Close of the 50 most recent completed bars
PctVs20 = Last / MA20 - 1
Fewer than 50 bars -> Trend and Signal = 'Insufficient data'

Trend:
  Uptrend   if Last > MA50 AND MA20 > MA50
  Downtrend if Last < MA50 AND MA20 < MA50
  Mixed     otherwise (equality counts as Mixed)

Signal, checked in this order:
  1. Trend = Uptrend AND PullbackBandLow <= PctVs20 <= PullbackBandHigh -> 'Pullback candidate'
  2. Trend = Uptrend AND PctVs20 > ExtendedThreshold                    -> 'Extended - wait'
  3. Trend = Downtrend                                                  -> 'Avoid'
  4. anything else                                                      -> 'Watch'
```

The band is inclusive at both ends. An uptrend between the band's top (+2%) and +5% is 'Watch', not a candidate.

### Position sizer

Inputs: Entry, Stop, Target, and optionally AvailableCash.

```text
Capital          = StartingCapital (or live AccountValue when RiskBasis = AccountValue)
RiskBudget       = Capital * MaxRiskPct
RiskPerShare     = Entry - Stop                       (invalid when Stop >= Entry)
SharesByRisk     = floor(RiskBudget / RiskPerShare)
SharesByCap      = floor(Capital * MaxPositionPct / Entry)
SharesByCash     = floor(AvailableCash / Entry)       (only when supplied)
Shares           = min(SharesByRisk, SharesByCap, SharesByCash)
PositionCost     = Shares * Entry
RiskDollars      = Shares * RiskPerShare
RiskPct          = RiskDollars / Capital
RewardRisk       = (Target - Entry) / RiskPerShare
MaxEntryForMinRR = (Target + MinRewardRisk * Stop) / (1 + MinRewardRisk)

AvailableCash = StartingCapital + RealizedPnL - cost of Open trades - cost of Planned trades

Verdict, first match wins:
  Stop >= Entry                    -> 'Stop must be below entry'
  RewardRisk < MinRewardRisk       -> 'SKIP - reward:risk below minimum'
  SharesByRisk < 1                 -> 'SKIP - one share risks more than the budget'
  SharesByCap < 1                  -> 'SKIP - one share exceeds the position cap'
  SharesByCash < 1 (when supplied) -> 'SKIP - not enough cash'
  otherwise                        -> 'OK - log it'

Warning (does not change the verdict): count(Open) >= MaxOpenPositions
```

Show MaxEntryForMinRR on every sizer result. It answers 'what is the most I can pay and still meet my minimum reward-to-risk', which is the question that came up repeatedly in practice.

### Trade math

```text
Position    = Shares * Entry
RiskDollars = (Entry - Stop) * Shares
RiskPct     = RiskDollars / Capital
PlannedRR   = (Target - Entry) / (Entry - Stop)
PnL         = (Exit - Entry) * Shares           (Closed only)
PnLPct      = Exit / Entry - 1
RMultiple   = PnL / RiskDollars
DaysHeld    = ExitDate - OpenDate               (calendar days)

Flags: RiskPct > MaxRiskPct; PlannedRR < MinRewardRisk
```

### Wash-sale flag and re-entry date

```text
A trade is flagged when ANOTHER Closed trade exists for the same ticker
(or the same WashSaleGroup) with PnL < 0 and
  OpenDate - WashSaleWindowDays <= its ExitDate <= OpenDate + WashSaleWindowDays

ReentryOkAfter(ticker) = latest ExitDate of a losing Closed trade + WashSaleWindowDays + 1 day
                         (no losing trade -> 'Clear')
```

The flag is informational. It never changes P&L, and tax treatment is for the user and a tax professional to confirm.

### Dashboard

```text
RealizedPnL   = sum PnL of Closed trades
AccountValue  = StartingCapital + RealizedPnL
ReturnPct     = RealizedPnL / StartingCapital
TaxSetAside   = max(0, RealizedPnL) * TaxSetAsideRate
AfterTaxPnL   = RealizedPnL - TaxSetAside
OpenCount     = count of Open trades            (warn above MaxOpenPositions)
CapitalAtWork = sum Position of Open trades, also as % of StartingCapital
RiskAtStops   = sum RiskDollars of Open trades, also as % of StartingCapital
ClosedCount, WinRate = count(PnL > 0) / ClosedCount
AvgWin, AvgLoss, ProfitFactor = sum of wins / -(sum of losses)   (null with no losses)
AvgR = mean RMultiple;  PlanAdherence = count(FollowedPlan) / ClosedCount;  AvgDaysHeld
BySetup: trades, win rate, net PnL for each Setup
Benchmark: SPY Close on the first trade's OpenDate -> latest SPY Close; show YouMinusSPY = ReturnPct - SPY return
```

## Market data and nightly refresh

A hosted background service pulls daily bars for every active ticker after the close, then updates the indicators, so no human enters a price.

**Provider abstraction.** `IMarketDataProvider.GetDailyBars(ticker, from, to)` returns date, open, high, low, close, and volume. Ship one real implementation and one fake for tests, chosen by configuration, with any API key in secrets. Candidates to evaluate in Milestone 0: Alpaca market data, Tiingo, Alpha Vantage, and Stooq CSV. Verify each one's free-tier limits and terms of use before depending on it, and do not scrape a site whose terms forbid it.

**Close basis.** Use split-adjusted closes, not dividend-adjusted. Broker charts and the workbook's averages use the plain close, and the two differ on ex-dividend dates.

**Completed bars only.** A bar counts as completed when its date is before today in New York, or it is after 16:15 New York time. An intraday bar must never enter the averages.

**Schedule.** Weekdays at 17:30 `America/New_York` (cron `30 17 * * 1-5`, evaluated in that time zone, for example with Cronos). The job is idempotent, so holidays need no calendar: the provider simply returns no new bar. A second attempt runs at 20:30, and a catch-up at 08:00 next morning if the latest expected bar is still missing.

**Job steps**

1. Load active instruments plus the benchmark (SPY).
2. For each ticker, request bars from five days before the latest stored date through today, and upsert on (Ticker, Date). A new ticker backfills 200 calendar days.
3. Drop any bar that is not yet completed.
4. Recompute and upsert the IndicatorSnapshot for the latest completed bar.
5. Record a RefreshRun. One ticker's failure is logged and skipped; it never aborts the others. Retry a failing ticker up to three times with backoff.
6. Call providers sequentially with a short delay to stay inside rate limits.

**Staleness.** Every watchlist row shows its AsOfDate. Show a visible stale badge when the AsOfDate is older than the most recent weekday close, so a silent provider failure is never mistaken for a quiet market.

**Manual controls.** A 'Refresh now' action calls `POST /api/refresh`. Changing a threshold needs no refresh, because Trend and Signal are derived on read.

**Later (not v1).** An optional live quote refresh during market hours, shown as a separate 'live % vs 20-day' column, with the averages still computed from completed bars only.

## API surface

The API is a thin layer over the rules library: it validates input, loads data, calls `Journal.Rules`, and returns computed fields alongside stored ones.

| Method and path | Purpose |
| --- | --- |
| GET, PUT `/api/settings` | Read and update the single settings row |
| GET, POST `/api/instruments`, PUT `/api/instruments/{ticker}` | List, add, edit, activate or deactivate tickers |
| GET `/api/watchlist` | One row per active ticker: AsOfDate, Last, Ma20, Ma50, PctVs20, Trend, Signal, ReentryOkAfter, Stale. Supports `signal` filter and sort |
| GET `/api/instruments/{ticker}/bars?days=120` | Closes with MA20 and MA50 series, for the chart |
| GET, POST `/api/trades` | List with `status` filter; create (Planned or Open) |
| GET, PUT, DELETE `/api/trades/{id}` | Read, edit, delete, with computed fields and flags |
| POST `/api/trades/{id}/fill` | Planned to Open, with fill price and date |
| POST `/api/trades/{id}/close` | Open to Closed: exit price, exit date, FollowedPlan, Lessons |
| POST `/api/trades/{id}/cancel` | Planned to Cancelled |
| POST `/api/sizer` | Entry, Stop, Target (+ ticker) to shares, costs, verdict, MaxEntryForMinRR, warnings |
| GET `/api/dashboard` | Every dashboard metric above, plus an equity curve series |
| GET, POST `/api/notes` | Pass log and review notes |
| GET `/api/refresh/runs`, POST `/api/refresh` | Refresh history; trigger a refresh now |
| GET `/api/export/trades.csv` | Trade log export for tax time |

Conventions: follow the reference app's error format (ProblemDetails if it has none), return 400 with field errors on validation failure, publish an OpenAPI document, and keep DTOs separate from entities.

## Authorization and roles

Unlike the reference app, where any authenticated user passes, this application is locked down: a valid token is necessary but not sufficient. Access is granted only by roles that the identity server (`auth.mattgerega.net`, Duende) assigns to a user, and a user without a role gets 403 on everything except health checks.

| Role | Grants |
| --- | --- |
| `swingtrade.reader` | Read-only REST: watchlist, trades, dashboard, notes, bars, settings, refresh history, sizer (a pure calculation), CSV export |
| `swingtrade.trader` | Everything a reader can do, plus create, edit, fill, close, cancel and delete trades, add notes, and add or edit instruments |
| `swingtrade.admin` | Everything a trader can do, plus change Settings (thresholds, tax rate, RiskBasis), trigger `POST /api/refresh`, view McpCallLog, and manage MCP options such as the `add_note` flag |
| `swingtrade.mcp` | May call `/mcp`. Independent of the REST roles, so Claude's connector can be limited to reading without also being a REST user |

Rules:

- Roles are carried in the `role` claim (single string or array), the same claim the identity server uses today (for example `authentication.admin`). Configure `MapInboundClaims = false` and `RoleClaimType = "role"`. The identity server must emit the claim in the access token for this API: define an API resource/scope (for example `swingtrade`) whose `UserClaims` include `role`, and a user-to-role assignment for the owner. Document the exact identity-server changes in `docs/IDENTITY_SETUP.md`; do not edit the identity repo from here.
- Authorization policies are named for what they allow (`Read`, `Write`, `Admin`, `Mcp`) and each requires the audience, a `sub` claim, and the matching role. Roles are hierarchical in code through the policies (admin satisfies trader, trader satisfies reader), not by duplicating assignments. Use `[Authorize(Policy = ...)]` on every controller action; a controller or action with no policy is a build-time test failure (see tests).
- `swingtrade.mcp` is required on `/mcp` in addition to the MCP authentication rules below. The REST roles do not grant MCP access and the MCP role does not grant REST access.
- The role names, policy names and the audience are constants in one place and are shown (read-only) in the Settings screen, so the owner can see what to grant.
- The web BFF (`OidcProxy`) forwards the user's token unchanged; it must not mask a 403. The UI reads the user's roles and hides or disables actions the user cannot perform, but the API is the only enforcement point.
- A user with no `swingtrade.*` role who signs in to the UI sees an 'Access not granted' page that names the roles to request, not an empty app.
- Default stance is deny: new endpoints are inaccessible until a policy is assigned. Log authorization failures (user `sub`, path, required policy) without logging tokens.

## MCP endpoint for Claude

The API also hosts a Model Context Protocol (MCP) server, so the owner can open Claude (claude.ai, the desktop or mobile app, or Claude Code), connect it to the journal, and ask it to review the current data and help with judgement calls: which pullback candidates look best this week, whether a planned trade fits the rules, what the last ten trades say about the process. The app itself still makes no LLM call. Claude is the client, the thinking happens in the owner's own Claude session, and every number comes from `Journal.Rules`. Tools return computed values and Claude must not recompute them.

**Implementation.** Use the official C# SDK, package `ModelContextProtocol.AspNetCore`: `AddMcpServer()`, `WithHttpTransport()` with `Stateless = true` (the SDK docs recommend stateless for most servers), `WithTools<JournalTools>()`, and `MapMcp("/mcp")`. Pin the SDK version and confirm these calls in M0. Serve Streamable HTTP only; legacy SSE is being deprecated. Tool methods are thin wrappers over the same application services the REST controllers call, with no duplicated logic and no second code path to the rules.

**Hosting.** The endpoint must be reachable over HTTPS from the internet, because claude.ai, Desktop, and mobile connect from Anthropic's cloud (outbound traffic comes from `160.79.104.0/21`, which can be allowlisted). Claude Code connects from the owner's machine. This replaces the earlier 'not exposed publicly' default for the `/mcp` path only: the web UI and REST API stay private, and only `/mcp` and its OAuth discovery paths are published. The owner adds the URL as a custom connector in Claude.

**Authentication.** The journal holds personal financial data, so an open URL is not acceptable. Requirements:

- Accept only requests carrying a valid bearer token. An unauthenticated request gets `401` with `WWW-Authenticate: Bearer resource_metadata="…"`; Claude ignores that header on a `200`.
- Preferred: OAuth 2.0 with an external identity provider as the authorization server, with the app acting only as the resource server. Serve protected resource metadata (RFC 9728) whose `resource` equals the MCP URL exactly and whose `authorization_servers` lists the issuer first. The provider must support PKCE S256 and either Dynamic Client Registration or Client ID Metadata Documents, and must accept redirect `https://claude.ai/api/mcp/auth_callback` plus a port-agnostic loopback redirect for Claude Code.
- Authorize by role and identity: require the `swingtrade.mcp` role (see Authorization and roles), and additionally allow only the owner's account (a configured subject or email). Reject every other valid token, even one that carries the role.
- Fallback if OAuth is too heavy: a long random static bearer token in the `Authorization` header. Claude's request-header authentication is in beta for a limited set of organizations, so confirm in M0 that the owner's account can use it. Never accept a token in the query string.
- Do not run the endpoint unauthenticated, even behind an unguessable URL.

**Limits and shape of results.** On claude.ai and Desktop a tool result is capped at about 150,000 characters and a call times out at 240 seconds; Claude Code defaults to a 25,000-token output cap. Keep results compact JSON, default to the latest values, cap history (bars at 120 per ticker, trades at 100 per call), and answer in well under a second.

**Safety.** Every result carries `asOf` (last completed close), `dataStatus` (fresh or stale, per the staleness rule), and the Settings snapshot used, so Claude can say how current the data is. Tools are read-only in v1, apart from the two optional note tools below. Add request rate limiting, a Host allowlist, and no CORS. Log each call (tool name, arguments, duration, caller) to `McpCallLog`, never the token. Brokerage credentials stay out of scope.

### MCP tools, resource, and prompts

| Tool | Arguments | Returns |
| --- | --- | --- |
| `get_dashboard` | none | Realized P&L, win rate, average R, expectancy, drawdown, account value, cash, reserved cash, tax set-aside, SPY benchmark, plus `asOf` and `dataStatus` |
| `get_watchlist` | `activeOnly` (default true) | Per ticker: last close, MA20, MA50, %vs20, Trend, Signal, staleness. Same values as the Watchlist screen |
| `get_instrument` | `ticker` | One watchlist row, its open or planned trade if any, wash-sale status, and the last 5 closes |
| `get_bars` | `ticker`, `days` (max 120) | Completed daily closes, oldest first |
| `list_trades` | `status`, `ticker`, `from`, `to`, `limit` (max 100) | Trades with computed fields (P&L, percent, R, tax set-aside, wash-sale flag) |
| `get_trade` | `id` | One trade in full, with notes and entry-time Trend and Signal if recorded |
| `size_position` | `ticker` or `entry`, `stop`, `target` | The sizer output: shares, cost, risk, reward and risk, MaxEntryForMinRR, and a SKIP reason when RewardRisk is below the minimum. A pure calculation; it saves nothing |
| `get_settings` | none | Capital, risk percent, position cap, minimum RewardRisk, MA lengths, signal band, tax rate |
| `get_refresh_status` | none | Last RefreshRun, per-ticker staleness, next scheduled run |
| `list_notes` | `from`, `to`, `kind (Pass, Review, General)` | Weekly journal entries and pass log |
| `add_note` (optional, off by default) | `date`, `kind`, `text`, `tradeId?` | Appends a note. The only write tool, enabled by a config flag |

**Resource.** `journal://rules` returns the full rule set in plain text: trend and signal definitions, sizer formulas, wash-sale rule, and the current Settings values. Claude reads it before giving an opinion, so its judgement is anchored to the real rules. Keep it generated from the same constants the rules library uses, never a hand-written copy.

**Prompts** (user-invoked templates):

- `weekly_review`: read the dashboard, the watchlist, open and planned trades, and last week's notes; list Pullback candidates; for each, run `size_position` with the user's entry, stop, and target; flag stale data and wash-sale conflicts; end by asking whether to record a pass.
- `trade_postmortem` (`tradeId`): compare a closed trade with its plan and the rules, in plain language, and suggest one thing to keep or change.

Tool descriptions must say that Claude is giving a review against the owner's own rules, that signals are informational and not advice, and that it should quote the returned numbers rather than calculate its own.

## Frontend screens

Six screens, built in this order, with the Watchlist first because it replaces the most manual work. Desktop first, usable on a phone, and matching the reference app's UI library and theme.

1. **Watchlist.** A sortable table, defaulting to 'Pullback candidate' rows first. Columns: ticker, name, as-of date, last, 20-day, 50-day, % vs 20-day, trend, signal, re-entry date. Filter chips by signal, a stale badge, and add or deactivate tickers. Clicking a row opens a drawer with a 120-day chart of close, MA20 and MA50, and a 'Plan a trade' button that opens the sizer prefilled with the last close.
2. **Trades.** Tabs for Planned, Open, Closed, and All. Computed columns: position, risk dollars, risk %, planned reward-to-risk, P&L, R-multiple, days held. Row actions: fill, close, cancel, edit. The new-trade form shows the sizer's result live. Highlight rows the way the workbook did: risk % above the limit, reward-to-risk below the minimum, a wash-sale badge.
3. **Sizer.** A standalone calculator showing shares by risk, by cap, and by cash, the verdict, and the maximum entry for the minimum reward-to-risk. A 'Create planned trade' button saves the result.
4. **Dashboard.** Tiles for account value, realized P&L, tax set-aside, open positions against the limit, capital at work, and risk at stops. Below them: performance metrics, results by setup, the SPY comparison, and an equity curve.
5. **Journal and weekly routine.** The pass log and review notes, plus the one-hour routine as a checklist with a time box per step: review open trades (10 min), read the watchlist signals (15), inspect candidates on the chart for swing low and prior high (15), size the trade (10), log it and place orders (10).
6. **Settings.** (Admin only for edits; readers and traders see it read-only.) Every threshold with a one-line explanation, the tax-rate assumption flagged as an assumption, and a reset-to-defaults action. An MCP panel shows the endpoint URL, the allowed owner identity, the on/off flag for note tools, and the last 20 McpCallLog entries.

Formatting: money to cents, percentages to one decimal, red for 'Avoid' and warnings, green for 'Pullback candidate', neutral for everything else. Never show a number without its as-of date where staleness could matter.

## Test plan and fixtures

Every number below was computed from the workbook's rules and the real trades in this project, and `Journal.Rules` must reproduce them exactly: money to the cent, ratios to three decimals. All use the default settings (capital 2,000, 1.5% risk, 50% cap, 2.0 minimum reward-to-risk, 27% tax).

### Position sizer

| Case | Entry | Stop | Target | Shares | Cost | Risk $ | Reward:risk | Max entry for 2x | Verdict |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| XLE plan | 62.82 | 61.40 | 65.90 | 15 | 942.30 | 21.30 | 2.169 | 62.90 | OK |
| SPY, one share | 769.64 | 753.50 | 773.50 | 1 | 769.64 | 16.14 | 0.239 | 760.17 | SKIP - reward:risk below minimum |
| XLK buy the dip | 192.00 | 187.50 | 202.00 | 5 | 960.00 | 22.50 | 2.222 | 192.33 | OK |
| XLK at limit | 196.30 | 193.50 | 202.00 | 5 | 981.50 | 14.00 | 2.036 | 196.33 | OK |
| XLK just too high | 196.60 | 193.50 | 202.00 | 5 | 983.00 | 15.50 | 1.742 | 196.33 | SKIP - reward:risk below minimum |
| Cap sets the size | 62.34 | 61.40 | 65.90 | 16 | 997.44 | 15.04 | 3.787 | 62.90 | OK |

Also test: Stop equal to or above Entry returns 'Stop must be below entry'; the 'Cap sets the size' case with AvailableCash 500 returns 8 shares (cash is the limit); the open-position warning appears when count(Open) is 3 or more.

### Trend and signal

| Case | Last | MA20 | MA50 | Trend | % vs 20-day | Signal |
| --- | --- | --- | --- | --- | --- | --- |
| SPY 2026-10-02 | 769.64 | 764.83 | 763.70 | Uptrend | +0.629% | Pullback candidate |
| QQQ 2026-10-02 | 749.58 | 727.78 | 716.82 | Uptrend | +2.995% | Watch |
| XLE 2026-10-02 | 62.82 | 63.47 | 62.12 | Uptrend | -1.024% | Watch |
| XLV 2026-10-02 | 166.18 | 168.38 | 168.53 | Downtrend | -1.307% | Avoid |
| XLK 2026-10-09 | 198.78 | 194.52 | 189.33 | Uptrend | +2.190% | Watch |
| QQQ 2026-10-09 | 751.27 | 737.54 | 725.27 | Uptrend | +1.862% | Pullback candidate |
| SPY 2026-10-09 | 778.57 | 767.98 | 767.99 | Mixed | +1.379% | Watch |
| Extended | 200.00 | 190.00 | 185.00 | Uptrend | +5.263% | Extended - wait |
| Band bottom | 99.00 | 100.00 | 90.00 | Uptrend | -1.000% | Pullback candidate |
| Band top | 102.00 | 100.00 | 90.00 | Uptrend | +2.000% | Pullback candidate |
| Just above band | 102.01 | 100.00 | 90.00 | Uptrend | +2.010% | Watch |
| Extended boundary | 105.00 | 100.00 | 90.00 | Uptrend | +5.000% | Watch |
| Just extended | 105.01 | 100.00 | 90.00 | Uptrend | +5.010% | Extended - wait |
| All equal | 100.00 | 100.00 | 100.00 | Mixed | 0.000% | Watch |

The market-data figures above were read from public price tables, so they are fixtures for the rules, not a price feed.

### Moving averages

- Closes 1 through 50, oldest to newest: Last 50, MA20 40.5, MA50 25.5, % vs 20-day +23.457%, Uptrend, 'Extended - wait'.
- 49 bars returns 'Insufficient data' for Trend and Signal.
- A bar dated today before 16:15 New York time is excluded from the averages.

### Closed trade and dashboard

XLE: 15 shares, entry 62.34, stop 61.40, target 65.90, exit 65.91, opened 2026-10-05, closed 2026-10-12.

| Metric | Expected |
| --- | --- |
| Position | 935.10 |
| Risk dollars and risk % | 14.10 and 0.705% |
| Planned reward:risk | 3.787 |
| P&L, P&L % | 53.55 and +5.727% |
| R-multiple | 3.798 |
| Days held | 7 |
| Account value (this trade only) | 2,053.55 |
| Return on starting capital | 2.678% |
| Tax set-aside | 14.46 |
| After-tax P&L | 39.09 |
| Win rate, average R | 100% and 3.80 |
| Profit factor, average loss | null (no losses) |
| Capital at work, open count | 0 and 0 |

### Wash sale

Given a Closed XLE trade with a loss, exited 2026-10-05:

- A new XLE trade opened 2026-11-04 is flagged (exactly 30 days); opened 2026-11-05 is not.
- Opened 2026-09-05 is flagged (the loss exit is 30 days after); opened 2026-09-04 is not.
- ReentryOkAfter for XLE is 2026-11-05. A winning trade creates no flag, and a trade never flags itself.

### End-to-end API tests

Unit tests are not enough: the API also gets an end-to-end suite that runs against the real running stack, mirroring PitStop's `src/api/tests/api-integration`.

- **Location and tooling:** `src/api/tests/api-integration`, Playwright's request context (no browser), TypeScript, Yarn 4, with WireMock for external HTTP. Add the suite to `.devops/pipeline-ci.yml` through the shared `stages/integration-tests/v1.yml` template so image publish is gated on it, as PitStop does.
- **Stack under test:** the Aspire AppHost `Testing` profile starts Postgres, the API, a mock OIDC server (as PitStop does) and a WireMock container standing in for Alpaca and Tiingo, with canned daily-bar mappings. Playwright's `webServer` boots it. No test calls a real market-data provider.
- **Auth in tests:** the mock OIDC server issues tokens with controllable `role` and `aud` claims. A helper mints tokens for each persona: no role, reader, trader, admin, mcp-only, wrong audience, expired. The seeder pattern from PitStop (a test key trusted only in the `Testing` environment) is acceptable if the mock OIDC route is too heavy, but the real JWT validation path must still run.
- **Data:** each spec starts from the seeded database (the XLE trade, default settings, 12 active instruments) and cleans up what it creates, so specs can run in any order.
- **Coverage:** one spec file per endpoint group in the API table. Each covers the happy path, validation errors (400 with field errors), not-found, illegal status transitions, and the role matrix (every endpoint is hit by every persona and the expected 200, 403 or 401 is asserted). Specific scenarios: the seeded dashboard matches the closed-trade fixture; the sizer fixtures return the documented shares and verdicts; changing a threshold changes `GET /api/v1/watchlist` signals on the next call with no refresh; `POST /api/v1/refresh` against WireMock loads bars, computes indicators, and records a RefreshRun, and a failing ticker is recorded without stopping the others; re-running refresh is idempotent; CSV export matches the trade log; the stale badge flag appears when the latest bar is old.
- **MCP:** the same suite drives `/mcp` over Streamable HTTP with the `swingtrade.mcp` persona: `tools/list` returns exactly the documented tools, each tool's result equals the matching REST response, a missing token returns 401 with `resource_metadata`, a non-owner or non-mcp token is rejected, and the token never appears in McpCallLog.
- **When it grows:** the suite is built up per milestone, not at the end. Each milestone that adds endpoints adds its specs, and the pipeline runs them from M4 onward.

### Job and API

- Upserting the same bars twice leaves one row per (Ticker, Date).
- A provider failure on one ticker is recorded and the others still complete.
- A threshold change alters `GET /api/watchlist` signals on the next call, with no refresh.
- With the seed data, `GET /api/dashboard` matches the closed-trade table above.
- MCP: `tools/list` returns exactly the tools in the MCP table, and `add_note` is absent unless its flag is on.

  MCP: each tool's result equals the matching REST response for the same seed data (dashboard, watchlist, size\_position fixtures).

  MCP: no token returns 401 with a `resource_metadata` pointer; a valid token for a non-owner identity is rejected; a valid owner token without `swingtrade.mcp` is rejected with 403; the token never appears in McpCallLog.

  Authorization: a valid token with no `swingtrade.*` role gets 403 on every REST endpoint; a reader gets 403 on every write endpoint; a trader gets 403 on settings, refresh and McpCallLog; an admin can do all of it; a token with the wrong audience gets 401. A reflection test fails the build if any controller action lacks an `[Authorize]` policy (other than the health checks).

  MCP: oversized requests are capped (120 bars, 100 trades). Verify end to end with the MCP Inspector, then with a custom connector in Claude.

## Milestones

Eight milestones, each ending in passing tests, a commit, and a stop for review; the rules library comes before any storage or screen.

1. **M0, reconnaissance (no code).** Read the reference app and write `docs/REFERENCE_NOTES.md`. Check the market-data candidates against their terms and free-tier limits. Propose the M1 plan. Done when the notes and the plan are approved.
2. **M1, rules library.** `Journal.Rules` with indicators, trend, signal, sizer, trade math, wash-sale, and dashboard calculators, plus unit tests for every fixture in the test plan. Done when all fixtures pass exactly and the library has no I/O or database references.
3. **M2, persistence and CRUD.** Entities, migrations, seed data, and the settings, instruments, trades, and notes endpoints, with validation and computed fields from the rules library, and role-based authorization on every endpoint (see Authorization and roles). Done when the seeded XLE trade produces the dashboard fixture through the API and the role tests pass.
4. **M3, market data and nightly job.** Provider interface, one real implementation, the fake, the ingestion service, the scheduled hosted service, backfill, the completed-bar rule, RefreshRun, and `POST /api/refresh`. Done when the 12 active tickers load for real and their MA20 and MA50 match a broker chart to within a few cents.
5. **M4, remaining endpoints.** Watchlist, sizer, dashboard, bars, export, and a generated OpenAPI client. Also add the Aspire AppHost (`Testing` profile with mock OIDC and WireMock) and the Playwright `api-integration` suite described in End-to-end API tests, with specs for everything built so far (M2 and M3 endpoints included) and the pipeline stage. Done when every endpoint in the API table works, has an end-to-end spec that includes the role matrix, and the suite passes locally through the AppHost.
6. **M5, MCP endpoint.** Add the SDK, the tools, the `journal://rules` resource, the two prompts, token validation, protected resource metadata, and McpCallLog. Enforce the `swingtrade.mcp` role and owner identity. Confirm with the owner how the Claude client is registered in the identity server (the static-token fallback has no role claim, so it must be checked against the configured owner token instead) and the hosting address first. Add the MCP specs to the end-to-end suite. Done when the end-to-end MCP specs pass, the MCP Inspector lists and calls every tool against seeded data, and Claude connected as a custom connector can answer 'what are my pullback candidates and how many shares for each?' using only tool output.
7. **M6, React app.** The six screens in order: Watchlist, Trades, Sizer, Dashboard, Journal and routine, Settings. Done when a full week's routine can be run in the UI without opening the spreadsheet.
8. **M7, hardening.** Docker Compose, CI, README, backup note, health checks, the stale-data badge, and `docs/DECISIONS.md` complete. Done when a fresh clone starts with one command.

Stretch, after v1 and only on request: alerts when a ticker newly becomes a 'Pullback candidate' or a stop or target is touched at the close, a live intraday quote column, an E\*TRADE transaction CSV import, and a backtester that replays the same rules over stored bars.

## Open decisions

Twelve choices are still open; each has a recommended default so Claude Code can proceed, and anything it settles on its own goes in `docs/DECISIONS.md`.

| Decision | Recommended default | Why |
| --- | --- | --- |
| Market data provider | Pick in M0 after checking free-tier limits and terms; keep it behind the interface | Free tiers and terms change, and the rules should not care |
| Database | Same as the reference app; otherwise PostgreSQL | Fewest surprises for a self-hosted setup |
| Auth and hosting | Same identity server and BFF as the reference app, but access requires explicit `swingtrade.*` roles granted in the identity server (reader, trader, admin, mcp); a bare authenticated user is denied. The web UI and REST API stay private; only /mcp is public | The data is personal finance; no brokerage credentials are stored. Claude's cloud apps need a public HTTPS endpoint |
| Risk basis | StartingCapital | Matches the workbook; switch to AccountValue in Settings if preferred |
| Signal timing | Last completed close | Reproducible and matches the workbook; live quote is a later column |
| Wash-sale scope | Ticker only, with the optional WashSaleGroup field unused | Matches the workbook; whether SPY, SPLG and VOO are 'substantially identical' is a question for a tax professional |
| Cash model | Planned orders reserve cash | Prevents sizing two trades against the same dollars |
| Extra watchlist tickers | Seed XLP, XLB, XLRE, XLC, SMH, IBB, KRE, ITA, XOP, SPLG, QQQM, GLDM as inactive | Activate after checking share price (under about 250) and volume |
| Notifications | Off in v1 | One hour a week does not need alerts yet |
| Tax set-aside rate | 27% | An assumption: roughly 22% federal plus Pennsylvania's 3.07%, rounded up. Confirm your own bracket |
| MCP exposure and auth | Public HTTPS /mcp, OAuth 2.0 with an external identity provider, owner-only; static bearer token as fallback | Claude's cloud apps cannot reach a private network. Confirm in M0 that the provider supports DCR or CIMD and that header auth is available to your account |
| MCP write tools | Read-only in v1; add\_note behind a flag, off by default | Orders and trades stay hand-entered; a notes-only write is low risk and can wait |
