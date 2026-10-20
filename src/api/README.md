# Swing Trade API

Projects under `src/api/src`: `Rules` (pure library), `MarketData` (price providers), `Contracts` (DTOs), `Data` (EF Core, PostgreSQL, migrations, seeder), `Api` (ASP.NET Core host, services, controllers), plus `*.UnitTests`.

## Database and migrations

The API reads the connection string `ConnectionStrings:swingtrade-db` (Aspire-style name, so it plugs into the AppHost later). At startup it applies pending migrations (`MigrateAsync`) and then runs `DatabaseSeeder`, which seeds only empty tables.

Add a migration (design-time factory in the Data project, no database needed to generate):

```bash
dotnet ef migrations add <Name> --project src/api/src/Spydersoft.SwingTrade.Data --startup-project src/api/src/Spydersoft.SwingTrade.Data --output-dir Migrations
```

Apply manually to a local Postgres instead of waiting for API startup:

```bash
dotnet ef database update --project src/api/src/Spydersoft.SwingTrade.Data --startup-project src/api/src/Spydersoft.SwingTrade.Data --connection "Host=localhost;Database=swingtrade;Username=postgres;Password=..."
```

Run the API locally: `ConnectionStrings__swingtrade-db="Host=...;..." dotnet run --project src/api/src/Spydersoft.SwingTrade.Api` (OpenAPI at `/openapi/v1.json` in Development and Testing; access is by role, see below).

## Market data and refresh

Providers live in `Spydersoft.SwingTrade.MarketData` (Alpaca primary, Tiingo fallback, a deterministic fake). `MarketData:Providers` is an ordered list; keys come from user-secrets or env (`MarketData__Alpaca__KeyId`, `MarketData__Alpaca__SecretKey`, `MarketData__Tiingo__Token`), never from committed config. Development defaults to the fake provider so the app runs without keys; other environments must configure a provider or startup fails.

The ingestion service and the scheduled hosted service are in `Api/Refresh`. The job runs weekdays at 17:30 and 20:30 America/New_York, plus an 08:00 catch-up if the latest bar is missing. `POST /api/v1/refresh` (Admin) starts a run now (202, or 409 if one is active); `GET /api/v1/refresh/runs` (Read) lists history. To try it against the real providers see `docs/M3_MANUAL_CHECK.md`. Design choices are in `docs/DECISIONS.md` (M3 decisions).

## Endpoints and roles

All routes are under `/api/v1` and need a token for audience `swingtrade` with a `role` claim (`docs/IDENTITY_SETUP.md`). Reader: every GET (including `GET /auth/info`, which tells the web UI who the caller is), `POST /sizer`, the CSV export. Trader (includes reader): create, edit, fill, close, cancel and delete trades, add notes, add or edit instruments. Admin (includes trader): `PUT /settings`, `POST /refresh`.

| Route | Purpose |
| --- | --- |
| `GET /watchlist?signal=&sort=` | One row per active ticker with Trend and Signal derived from current Settings, re-entry date, stale flag |
| `GET /instruments/{ticker}/bars?days=120` | Completed closes with MA20 and MA50 series (max 120) |
| `GET /auth/info` | The caller's subject, `swingtrade.*` roles, `canRead`/`canWrite`/`canAdmin`, and the role, policy and audience names. 403 for a user with no role |
| `POST /sizer` | Shares by risk, cap and cash, cost, risk, reward:risk, max entry for the minimum, verdict, warning. Saves nothing |
| `GET /export/trades.csv` | Trade log with computed fields, for tax time |
| `GET /dashboard`, `/settings`, `/trades`, `/notes`, `/instruments`, `/refresh/runs` | As in `docs/BUILD_PLAN.md` |

## MCP endpoint for Claude

`/mcp` is a Streamable HTTP MCP server (official C# SDK `ModelContextProtocol.AspNetCore`, stateless; there is no `initialize` handshake or session to keep). It lives in `Api/Mcp` and is a thin layer over the same services the controllers use. Design choices are DECISIONS #71 to #83; the identity-server and Helm side is in `docs/IDENTITY_SETUP.md`.

- **Tools** (read-only): `get_dashboard`, `get_watchlist`, `get_instrument`, `get_bars` (max 120), `list_trades` (max 100), `get_trade`, `size_position`, `get_settings`, `get_refresh_status`, `list_notes`, `get_levels` (swing low and prior high from closes). Each returns `{ asOf, dataStatus, settings, data }`; `data` equals the matching REST response. `add_note` exists only with `Mcp:EnableAddNote=true` and then also needs `swingtrade.trader`. Write tools, each group behind its own flag and each needing `swingtrade.trader`: `add_note`; `record_pass`, `log_planned_trade` (sized by the sizer, refused on a SKIP verdict); `fill_trade`, `close_trade`, `cancel_trade`; `add_instrument`, `set_instrument_active`. None places an order.
- **Resource** `journal://rules`: the rule set as plain text, generated from the Rules library constants and the live Settings.
- **Prompts** `weekly_review` and `trade_postmortem(tradeId)`.
- **Access**: bearer token from `Auth:Authority` with audience `swingtrade`, role `swingtrade.mcp`, and `sub` equal to `Mcp:OwnerSubject`. No token or a bad one is 401 with `WWW-Authenticate: Bearer resource_metadata="..."`; a valid token for anyone else is 403. Tokens are read from the Authorization header only. `/.well-known/oauth-protected-resource` (and `.../mcp`) serves the RFC 9728 metadata. Host allowlist (`Mcp:AllowedHosts`, 421), a rate limit (`Mcp:RequestsPerMinute`, 429), no CORS.
- **Audit**: every tool, resource and prompt call is written to `McpCallLog` (caller, sanitised arguments, duration, status; never a token), kept 90 days. `GET /api/v1/mcp/calls?take=20` (Admin) and `GET /api/v1/mcp/info` (Read) feed the Settings screen.

| Setting | Meaning |
| --- | --- |
| `Mcp:PublicUrl` | Absolute public URL of `/mcp`; the metadata `resource`. https outside Development and Testing |
| `Mcp:OwnerSubject` | The only `sub` allowed. Required outside Development and Testing (startup fails without it) |
| `Mcp:AllowedHosts` | Host names for the MCP routes; default is the host of `Mcp:PublicUrl` |
| `Mcp:EnableAddNote` | Registers `add_note`; default false |
| `Mcp:EnableJournalWrites` | Registers `record_pass` and `log_planned_trade`; default false |
| `Mcp:EnableTradeLifecycle` | Registers `fill_trade`, `close_trade`, `cancel_trade`; default false |
| `Mcp:EnableWatchlistManagement` | Registers `add_instrument`, `set_instrument_active`; default false |
| `Mcp:RequestsPerMinute` | Fixed-window limit for `/mcp` and the metadata routes; default 120 |

Try it against the running `Testing` stack with the MCP Inspector (mint the `mcponly` token as shown in `docs/apphost-launch-profiles.md`; the Testing API trusts that user as the owner):

```bash
npx @modelcontextprotocol/inspector --cli http://localhost:8180/mcp --transport http \
  --header "Authorization: Bearer $MCP_TOKEN" --method tools/list
```

## OpenAPI document

The committed document is `src/api/openapi/swing-trade.json`. Regenerate it after changing a route or a DTO (this starts the app up to host build only; no database is needed):

```bash
dotnet build src/api/src/Spydersoft.SwingTrade.Api -p:GenerateOpenApi=true
```

A unit test (`ApiSurfaceTests`) fails when the committed file, the controllers and the plan's API table disagree, and the e2e suite checks the served `/openapi/v1.json` against it. The TypeScript client for the web UI is generated from it (`yarn api:generate` in `src/web/src/swing-trade-ui`, output committed in `src/api/generated`).

## End-to-end tests (Playwright, through the AppHost)

`src/api/tests/api-integration` drives the real API with Playwright's request context (no browser). Its `webServer` starts the AppHost `Testing` profile: Postgres, the mock OIDC server, WireMock standing in for Alpaca and Tiingo, and the API (`docs/apphost-launch-profiles.md`). Prerequisites: .NET 10 SDK, Node 24 with corepack (Yarn 4), and a container runtime.

```bash
cd src/api/tests/api-integration
corepack enable
yarn install --immutable
# podman instead of Docker: export ASPIRE_CONTAINER_RUNTIME=podman
yarn test                 # boots the stack, runs the suite, tears the stack down
yarn typecheck            # tsc --noEmit
yarn test:report          # open the HTML report
```

What it covers: one spec file per endpoint group (settings, instruments, trades with fill/close/cancel, notes, watchlist, bars, sizer with the plan's fixtures, dashboard against the seeded XLE trade, refresh against WireMock, CSV export, OpenAPI), `thresholds.spec.ts` (a Settings change shows on the next call without a refresh), `auth.spec.ts` (every endpoint for every persona: 403 without a sufficient role, 401 for no token, a wrong audience, an expired token, a forged signature), and `mcp.spec.ts` (`/mcp` over Streamable HTTP as the `mcponly` owner: the exact tool set, every tool equal to its REST counterpart, the rules resource, prompts, 401 with `resource_metadata`, 403 for other personas and for the `mcpother` user, the audit log without tokens). `refresh.spec.ts` runs first and loads the canned bars. Specs delete the trades they create and deactivate the instruments they add (neither can be removed through the API; notes cannot be removed at all).

The canned bars come from `scripts/generate-wiremock.mjs` (`yarn generate:wiremock`). If a run is killed, remove the leftover containers before the next run (`docker ps -a` / `podman ps -a`); the ports are fixed. `reuseExistingServer` is on locally, so a stack you started with `dotnet run --project src/Spydersoft.SwingTrade.AppHost --launch-profile Testing` is reused. The suite is also stage `integration_tests` in `.devops/pipeline-ci.yml`.
