# Reference Notes: PitStop

Source: `d:\spydersoft\pitstop\pitstop` (app) and `d:\spydersoft\pitstop\pitstop-helm-config` (deploy config). Swing Trade mirrors these unless `docs/DECISIONS.md` says otherwise.

## Solution and project layout

- Single root solution `Spydersoft.PitStop.slnx` (new `.slnx` format) with `/api/` and `/web/` solution folders plus one Aspire AppHost.
- Monorepo: `src/api`, `src/web`, `src/Spydersoft.PitStop.AppHost`, `charts/pitstop` (app Helm chart), `.devops/pipeline-ci.yml`, `docs/`.
- API projects under `src/api/src/`: `*.Api` (host), `*.Contracts` (DTOs/requests, no deps), `*.Data` (entities, `DbContext`, `Configuration/` per-entity `IEntityTypeConfiguration`, `Migrations/`), `*.DataSeeder` (console app), `*.Api.UnitTests`.
- Web under `src/web/`: `src/Spydersoft.PitStop.Frontend` (ASP.NET BFF host) and `src/pitstop-ui` (React SPA); `tests/web-integration` (Playwright). API integration tests: `src/api/tests/api-integration` (Playwright request context + WireMock).
- Namespace/project prefix `Spydersoft.<App>`. Controllers, services, jobs in folders `Controllers/`, `Services/`, `Jobs/`.
- Repo root files: `Directory.Build.props` (enables NUnit runner), `global.json`, `GitVersion.yml`, `package.json` (yarn workspace + husky), `.editorconfig`, `.prettierrc`, `.lintstagedrc`, `.husky/`, `CONTRIBUTING.md`. `src/Directory.Packages.props` (central package management) and `src/nuget.config`.

## .NET target

- `net10.0`, `Nullable` and `ImplicitUsings` enabled. `global.json`: SDK `10.0.100`, `rollForward: latestMinor`, test runner `Microsoft.Testing.Platform`.
- Central package versions with floating majors (`10.*`) for EF/ASP.NET packages.

## Data access and migrations

- EF Core 10, Npgsql provider, **PostgreSQL**. Registered via Aspire: `builder.AddNpgsqlDbContext<PitStopDbContext>("pitstop-db")`.
- Fluent configuration in per-entity `*Configuration` classes applied in `OnModelCreating`.
- Migrations live in the `Data` project (`Migrations/`, timestamped, e.g. `20260430020448_InitialCreate`). Applied **at API startup**: `await ctx.Database.MigrateAsync()` in `Program.cs`.
- Sonar excludes `**/Migrations/**`.

## Configuration and secrets

- `appsettings.json` holds defaults; `appsettings.Development.json`, `appsettings.Testing.json` per environment. Env vars (`Section__Key`) from Helm ConfigMaps/ExternalSecrets in-cluster. User secrets (`UserSecretsId`) locally.
- Secrets in cluster come via `ExternalSecret` resources (`data-api-secrets`, `web-secrets`, Postgres secrets) owned by the helm-config repo, injected with `envFrom: secretRef`.

## Authentication

- API: JWT bearer against an external IdP (`Auth:Authority` = `https://auth.mattgerega.net`, `Auth:Audience`). `MapInboundClaims = false`. Policies `Read`/`Write` require claim `sub` plus claim `scope` = policy name (`AuthorizationPolicies.cs`). Controllers use `[Authorize(Policy = ...)]`; `GetCurrentUserId()` reads `sub`.
- Web: `Spydersoft.OidcProxy.Net.OpenIdConnect` BFF. Browser never holds tokens; BFF holds the session (Redis-backed when `Redis:ConnectionString` set, else in-memory) and reverse-proxies `/api/v1` to the API with the token. SPA is served from the BFF's `wwwroot`.
- Testing environment: Aspire starts a mock OIDC container (`ghcr.io/soluto/oidc-server-mock`) and the API additionally trusts a symmetric test key so the seeder can mint tokens.
- Multi-tenant by `sub` (OwnerId on rows). Swing Trade is single-user; see DECISIONS.

## Logging, telemetry, health checks

- `Spydersoft.Platform.Hosting` (3.1.0) gives `AddSpydersoftTelemetry` (OTLP logs/metrics/traces, `Telemetry:*` config), `AddSpydersoftSerilog`, `UseSpydersoftRequestLogging`, `AddSpydersoftHealthChecks` / `UseSpydersoftHealthChecks`. Health endpoints `/livez` and `/readyz`, filtered out of traces.

## API style and error format

- MVC controllers (`[ApiController]`), route prefix `api/v1/...`, a `PitStopControllerBase` holding the `DbContext` and user-id helper. Logic mostly in controllers, with some scoped services (`FillUpService`, `LocationService`). `ActionResult<T>`, `CancellationToken`, `NotFound()` for missing/foreign rows. Default ProblemDetails from `[ApiController]`.
- List endpoints take a `*ListQuery` and return `{Items, TotalCount, Page, PageSize}`, page size clamped to 100.
- Contracts project separates DTOs from entities (matches the plan's DTO rule).

## OpenAPI

- `AddOpenApi()` / `MapOpenApi()` (built-in, Development only) serving `/openapi/v1.json`. SPA client generated with `@hey-api/openapi-ts` (`yarn api:spec`, `yarn api:generate`) into `src/api/generated`, with an axios client.

## Frontend

- Vite 8, React 19, TypeScript 6, **Yarn 4** (corepack), Node 24.
- UI: **PrimeReact** + Tailwind 4, FontAwesome and PrimeIcons. Charts: **Chart.js** (no Recharts).
- Routing: `react-router-dom` 7 (`AppRouter.tsx`). State/data: **Redux Toolkit** slices (`store/slices/*`) with per-resource `api/*Api.ts` wrappers over the generated client; no TanStack Query.
- Layout helpers: `KpiTile`, `PageHeader`, `NavigationBar`. Runtime config via the BFF's dynamic `/config.js`.
- Lint/format: ESLint 10, Prettier; husky `pre-commit` (dotnet format, eslint/prettier via lint-staged) and `pre-push` (full unit tests).

## Tests

- Backend unit tests: **NUnit 4** + NUnit3TestAdapter, run on Microsoft.Testing.Platform (`EnableNUnitRunner`), EF Core InMemory and SQLite. Hand-rolled fakes in `Support/` (`FakeTimeProvider`, `StubHttpMessageHandler`, ...). Test project name `*.UnitTests`; pipeline globs `**/*.UnitTests/*.csproj`.
- Frontend: Vitest 4 + Testing Library + jsdom, coverage via v8, JUnit and cobertura output.
- E2E: Playwright suites (API request context, and browser) booted through Aspire `webServer`; WireMock for external HTTP.

## Local dev, Docker, deploy

- **.NET Aspire AppHost** (13.3.5) runs Postgres (pinned port and password, data volume), API, BFF, Vite app (`AddViteApp(...).WithYarn()`), optional mock OIDC, and the seeder (explicit start). Launch profiles `Local`/`Remote`/`Testing`.
- Dockerfiles are minimal runtime-only: `COPY . /app` of `dotnet publish` output (CI publishes first), base `aspnet:10.0` (api) / `aspnet:10.0-noble-chiseled` (web), port 8080, non-root `$APP_UID`. **No docker-compose**; the plan's "Docker Compose" maps to Aspire locally and Helm in-cluster.
- Web project builds the SPA via MSBuild `Exec` targets and copies `dist` into `wwwroot`.
- Helm: app chart `charts/pitstop` uses the bjw-s `common` library (controllers `data-api`, `web`, probes `/livez` `/readyz`, Gateway API `HTTPRoute`), published as an OCI chart. Deploy config repo `pitstop-helm-config` uses helmfile (`environments/{test,production}`, `images.yaml` tag bumps via `scripts/process-cicd.ps1`, ConfigMaps in `values.yaml.gotmpl`, ExternalSecrets, Postgres under `apps/data-api`).
- Swing Trade's sibling `swing-trade-helm-config` repo already exists (empty, `.git` only).

## CI

- Azure DevOps `.devops/pipeline-ci.yml` using shared templates from `spydersoft-consulting/azure-devops-templates` (`pipelines/build-multi-container/v1.yml`, `stages/integration-tests/v1.yml`). GitVersion 6 (GitHubFlow, `main` = `-beta`, tags = release). SonarCloud for C# and UI. Images pushed to `ghcr.io/spydersoft-consulting/...`. PR builds do not publish; `main` publishes betas and bumps helm-config `test`; tags promote.
- Playwright integration stage gates image publish.
