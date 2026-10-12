# Swing Trade API

Projects under `src/api/src`: `Rules` (pure library), `Contracts` (DTOs), `Data` (EF Core, PostgreSQL, migrations, seeder), `Api` (ASP.NET Core host, services, controllers), plus `*.UnitTests`.

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
