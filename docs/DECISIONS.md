# Decisions

Where `docs/BUILD_PLAN.md` and the reference app (PitStop) disagree on tooling, the reference app wins. Approved by the owner 2026-10-12: project prefix `Spydersoft.SwingTrade`, commit straight to `main` for the first iteration, reuse PitStop's identity provider.

| # | Topic | Plan said | Decision (proposed) | Why |
| --- | --- | --- | --- | --- |
| 1 | Test framework | xUnit | NUnit 4 on Microsoft.Testing.Platform | Reference app; CI template uses `testRunner: mtp` |
| 2 | Frontend data/state | TanStack Query | Redux Toolkit slices + generated hey-api client | Reference app |
| 3 | UI library / charts | Recharts | PrimeReact + Tailwind 4; Chart.js for charts | Reference app ("matching the reference app's UI library") |
| 4 | Local orchestration | Docker Compose | .NET Aspire AppHost locally; Helm in-cluster; no compose file | Reference app has no compose |
| 5 | Auth (web/API) | single user | OidcProxy BFF + JWT bearer at `auth.mattgerega.net`, policies Read/Write; single owner (no per-row OwnerId) | Reference app, but one user |
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
