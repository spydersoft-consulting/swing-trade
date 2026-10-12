# Decisions

Where `docs/BUILD_PLAN.md` and the reference app (PitStop) disagree on tooling, the reference app wins. Approved by the owner 2026-10-12: project prefix `Spydersoft.SwingTrade`, commit straight to `main` for the first iteration, reuse PitStop's identity provider.

| # | Topic | Plan said | Decision (proposed) | Why |
| --- | --- | --- | --- | --- |
| 1 | Test framework | xUnit | NUnit 4 on Microsoft.Testing.Platform | Reference app; CI template uses `testRunner: mtp` |
| 2 | Frontend data/state | TanStack Query | Redux Toolkit slices + generated hey-api client | Reference app |
| 3 | UI library / charts | Recharts | PrimeReact + Tailwind 4; Chart.js for charts | Reference app ("matching the reference app's UI library") |
| 4 | Local orchestration | Docker Compose | .NET Aspire AppHost locally; Helm in-cluster; no compose file | Reference app has no compose |
| 5 | Auth (web/API) | single user | OidcProxy BFF + JWT bearer at `auth.mattgerega.net`, policies Read/Write/Admin/Mcp, now role-based (see M2 decision 17); single owner (no per-row OwnerId) | Reference app, but one user |
| 6 | Package manager / Node | n/a | Yarn 4, Node 24 | Reference app |
| 7 | Solution format / layout | `Journal.*` projects | `Spydersoft.SwingTrade.*` names; `.slnx`; `Rules`, `Data`, `MarketData`, `Contracts`, `Api`, `UnitTests`, `AppHost`, `Frontend`, `swing-trade-ui` | Reference app naming; plan's project split preserved |
| 8 | API route prefix | `/api/...` | `/api/v1/...` | Reference app convention; MCP stays at `/mcp` |
| 9 | Controllers vs services | thin API over services | Controllers and MCP tools both call shared application services, which call the Rules library | Plan rule: one code path to the rules |
| 10 | Database | same as reference | PostgreSQL via Aspire Npgsql, migrations applied at startup | Reference app |

## M1 decisions

| # | Topic | Decision | Why |
| --- | --- | --- | --- |
| 11 | "After 16:15" | Strictly greater than 16:15 New York; a bar at exactly 16:15 is not yet complete | Literal reading of the plan |
| 12 | Fewer than 20/50 bars | `Ma20`/`Ma50` are null until enough bars exist; Trend and Signal are `Insufficient data` below 50 | Plan rule |
| 13 | Stop >= Entry in `TradeMath` | Planned reward:risk returns 0 and R-multiple null rather than dividing by zero; validation rejects such trades before storage | Defensive; the sizer reports the verdict |
| 14 | Metrics with no formula | Expectancy and drawdown (listed for the MCP `get_dashboard` tool) are not implemented; they need a definition from the owner | Rule: never invent a formula |
| 15 | Test project naming | `Spydersoft.SwingTrade.Rules.UnitTests`, so the pipeline glob `**/*.UnitTests/*.csproj` finds it | Reference app |
| 16 | `America/New_York` lookup | Uses the IANA id via `TimeZoneInfo`. Linux images need tzdata (the chiseled aspnet image has none), so the API image must add it | Found while writing `BarCompletion` |

## Findings from M0 research

- **Identity provider** (`d:\spydersoft\identity`, Duende IdentityServer 8.0.4): no Dynamic Client Registration and no Client ID Metadata Document support is configured. Plan: register a PKCE client for Claude by hand (redirect `https://claude.ai/api/mcp/auth_callback` plus a loopback for Claude Code) and enter its client id in the Claude custom connector's advanced settings. Confirm in M5 that the connector accepts a pre-registered client; otherwise use the static-bearer-token fallback.
- Duende's DCR is a licensed add-on, so avoid depending on it.

## Open items needing the owner

- Market-data provider: not yet evaluated against current terms and free-tier limits (needs a live web check). Candidates: Alpaca, Tiingo, Alpha Vantage, Stooq. Needed before M3.
- Public hosting address for `/mcp` (a separate HTTPRoute on the API; the REST API stays behind the BFF). Needed before M5.
- SonarCloud project key and helm-config layout for `swing-trade-helm-config`. Needed before M7.

## M2 authorization decision (2026-10-12)

| # | Topic | Decision | Why |
| --- | --- | --- | --- |
| 17 | Authorization model | Replaces the reference app's scope policies (and "any authenticated user") with role-based policies `Read`, `Write`, `Admin`, `Mcp`. Each requires authenticated user, audience `swingtrade`, a `sub` claim, and a `role` claim: `swingtrade.reader` / `.trader` / `.admin` (admin satisfies trader satisfies reader) and `swingtrade.mcp` (independent). `MapInboundClaims=false`, `RoleClaimType="role"`; role may be one claim or an array. Names live in `SwingTradeAuth`; `Auth:Audience` config removed, only `Auth:Authority` remains. GET = Read; trade create/edit/fill/close/cancel/delete, notes POST, instruments POST/PUT = Write; PUT settings = Admin. No role gives 403. Tests: role matrix, audience/sub/anonymous denial, and a reflection test failing on any controller action without a known `[Authorize(Policy)]`. Identity-server changes are in `docs/IDENTITY_SETUP.md` | Per the new "Authorization and roles" section of the plan; deny by default |
| 18 | Auth failure logging | Not yet implemented (logging `sub`, path, required policy on 403); to add with the Settings roles screen and `POST /api/refresh` (Admin) | Out of scope of the M2 policy change |

## M2 decisions

| # | Topic | Decision | Why |
| --- | --- | --- | --- |
| 17 | Seeding location | `DatabaseSeeder` lives in the Data project and runs at API startup after `MigrateAsync`; no separate DataSeeder console app | Owner instruction for M2 |
| 18 | Seed idempotence | Settings seed when the Settings table is empty; instruments and the sample XLE trade seed together only when Instruments is empty, so deleting every trade does not bring the sample back | "Seed only when tables are empty" without resurrecting deleted data |
| 19 | Contracts depends on Rules | Contracts references the Rules library for the `TradeStatus`, `TradeSetup` and `RiskBasis` enums; `NoteKind` lives in Contracts. Enums serialise as strings in JSON and the database | One definition of each enum; Rules stays pure |
| 20 | Keys for price tables | `PriceBar` and `IndicatorSnapshot` use composite keys (Ticker, Date) and (Ticker, AsOfDate), which are the plan's unique constraints; no FK to Instrument | Simplest way to get append-only upserts; M3 may revisit |
| 21 | Settings row | Fixed Id 1, no autoincrement. If the row is missing the API falls back to the plan defaults on read and creates it on first PUT | Single-row table |
| 22 | Trade creation | POST accepts only Planned or Open (per plan). A Closed trade is reached through fill and close. Ticker must exist in Instruments and is upper-cased | Plan API table; data integrity |
| 23 | Field rules by status | Planned must have no OpenDate; ExitPrice and ExitDate are accepted only on Closed. Cancelled needs nothing. Violations are 400 field errors. All price fields must be greater than zero | Extends the plan's validation list consistently with "a Planned trade has no OpenDate until it fills" |
| 24 | Fill | The fill price replaces EntryPrice (the limit) and must be above the stop; shares are unchanged | Plan says fill takes a price and date; entry is the traded price |
| 25 | Close | Updates FollowedPlan and Lessons only when supplied; ExitDate must not precede OpenDate | Plan validation |
| 26 | Invalid transitions | Return 409 ProblemDetails (not found is 404, field errors are 400 ValidationProblemDetails) | Brief allowed 409 or 400; 409 keeps state errors apart from input errors |
| 27 | Delete | DELETE is allowed in any status | Plan lists DELETE with no restriction |
| 28 | Wash-sale flag on trades | Computed only for Open and Closed trades (they have an OpenDate). Planned and Cancelled trades are never flagged; the library is called with all Closed trades, grouped by `WashSaleGroup` when set | The rule is defined on OpenDate; the plan default is ticker only, and the optional group field is passed through if the owner fills it in |
| 29 | Risk capital for risk % | Trade risk % uses `PositionSizer.CapitalFor`: StartingCapital, or the dashboard AccountValue when RiskBasis is AccountValue | Plan sizer definition of Capital |
| 30 | Benchmark return | SPY (the instrument flagged IsBenchmark) close on or before the first Open/Closed trade OpenDate, to the latest SPY bar; null when no bars exist. Only one instrument can be the benchmark; setting a new one clears the old | Plan says "close on the first trade's OpenDate"; non-trading dates need the prior close |
| 31 | Auth policies | `swingtrade:read` and `swingtrade:write` scope claims, same shape as PitStop. The Testing-environment symmetric key branch is not copied yet | No integration tests or AppHost in M2 |
| 32 | Dashboard extras | `expectancy` and `drawdown` are still not returned (see #14); the dashboard DTO carries everything the library computes, including the equity curve | No formula in the plan |
| 33 | Tests | Service and controller tests use the EF Core InMemory provider (no Sqlite); uniqueness is enforced by the service and the database index | InMemory does not enforce unique indexes, so the service check is what is tested |
