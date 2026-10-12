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

All routes are under `/api/v1` and need a token for audience `swingtrade` with a `role` claim (`docs/IDENTITY_SETUP.md`). Reader: every GET, `POST /sizer`, the CSV export. Trader (includes reader): create, edit, fill, close, cancel and delete trades, add notes, add or edit instruments. Admin (includes trader): `PUT /settings`, `POST /refresh`.

| Route | Purpose |
| --- | --- |
| `GET /watchlist?signal=&sort=` | One row per active ticker with Trend and Signal derived from current Settings, re-entry date, stale flag |
| `GET /instruments/{ticker}/bars?days=120` | Completed closes with MA20 and MA50 series (max 120) |
| `POST /sizer` | Shares by risk, cap and cash, cost, risk, reward:risk, max entry for the minimum, verdict, warning. Saves nothing |
| `GET /export/trades.csv` | Trade log with computed fields, for tax time |
| `GET /dashboard`, `/settings`, `/trades`, `/notes`, `/instruments`, `/refresh/runs` | As in `docs/BUILD_PLAN.md` |

## OpenAPI document

The committed document is `src/api/openapi/swing-trade.json`. Regenerate it after changing a route or a DTO (this starts the app up to host build only; no database is needed):

```bash
dotnet build src/api/src/Spydersoft.SwingTrade.Api -p:GenerateOpenApi=true
```

A unit test (`ApiSurfaceTests`) fails when the committed file, the controllers and the plan's API table disagree, and the e2e suite checks the served `/openapi/v1.json` against it. The generated TypeScript client is added with the frontend in M6.

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

What it covers: one spec file per endpoint group (settings, instruments, trades with fill/close/cancel, notes, watchlist, bars, sizer with the plan's fixtures, dashboard against the seeded XLE trade, refresh against WireMock, CSV export, OpenAPI), `thresholds.spec.ts` (a Settings change shows on the next call without a refresh), and `auth.spec.ts` (every endpoint for every persona: 403 without a sufficient role, 401 for no token, a wrong audience, an expired token, a forged signature). `refresh.spec.ts` runs first and loads the canned bars. Specs delete the trades they create and deactivate the instruments they add (neither can be removed through the API; notes cannot be removed at all).

The canned bars come from `scripts/generate-wiremock.mjs` (`yarn generate:wiremock`). If a run is killed, remove the leftover containers before the next run (`docker ps -a` / `podman ps -a`); the ports are fixed. `reuseExistingServer` is on locally, so a stack you started with `dotnet run --project src/Spydersoft.SwingTrade.AppHost --launch-profile Testing` is reused. The suite is also stage `integration_tests` in `.devops/pipeline-ci.yml`.
