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

Run the API locally: `ConnectionStrings__swingtrade-db="Host=...;..." dotnet run --project src/api/src/Spydersoft.SwingTrade.Api` (OpenAPI at `/openapi/v1.json` in Development; JWT scopes `swingtrade:read` and `swingtrade:write`).

## Market data and refresh

Providers live in `Spydersoft.SwingTrade.MarketData` (Alpaca primary, Tiingo fallback, a deterministic fake). `MarketData:Providers` is an ordered list; keys come from user-secrets or env (`MarketData__Alpaca__KeyId`, `MarketData__Alpaca__SecretKey`, `MarketData__Tiingo__Token`), never from committed config. Development defaults to the fake provider so the app runs without keys; other environments must configure a provider or startup fails.

The ingestion service and the scheduled hosted service are in `Api/Refresh`. The job runs weekdays at 17:30 and 20:30 America/New_York, plus an 08:00 catch-up if the latest bar is missing. `POST /api/v1/refresh` (Admin) starts a run now (202, or 409 if one is active); `GET /api/v1/refresh/runs` (Read) lists history. To try it against the real providers see `docs/M3_MANUAL_CHECK.md`. Design choices are in `docs/DECISIONS.md` (M3 decisions).
