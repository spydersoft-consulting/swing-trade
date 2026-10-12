# AppHost launch profiles

`src/Spydersoft.SwingTrade.AppHost` (`Properties/launchSettings.json`) is the Aspire AppHost. It has two launch profiles. There is no frontend resource until M6, and no `Remote` profile: the API is the only app and the real identity provider is reached by running the API directly with `Auth:Authority` left at its default.

A container runtime is required (Docker, or podman with `ASPIRE_CONTAINER_RUNTIME=podman`).

Fixed host ports (chosen so this AppHost and PitStop's can run side by side):

| Resource | Port |
| --- | --- |
| API (http) | 8180 |
| Postgres | 8110 |
| mock OIDC | 8210 |
| WireMock (Testing only) | 8310 |
| Aspire dashboard / OTLP / resource service | 8400 (https 8401) / 8402 / 8403 |

## `Local`

Interactive development with no external dependencies and no secrets.

- `DOTNET_ENVIRONMENT=Development`, so `appsettings.Development.json` applies: market data uses the deterministic **fake** provider.
- `UseMockOidc=true` starts the `ghcr.io/soluto/oidc-server-mock` container (`mock-oidc`) and points the API at it (`Auth__Authority=http://localhost:8210`). The five persona users are the same as in Testing (see below), so you can mint a token and call the API from curl or a REST client.
- Postgres keeps a data volume, so state survives restarts.

```sh
dotnet run --project src/Spydersoft.SwingTrade.AppHost --launch-profile Local
```

Mint a token (admin shown) and call the API:

```sh
TOKEN=$(curl -s http://localhost:8210/connect/token \
  -d grant_type=password -d client_id=swingtrade-api-test -d client_secret=swingtrade-api-test-secret \
  -d username=admin -d password='Test123!' -d scope=swingtrade | sed 's/.*"access_token":"\([^"]*\)".*/\1/')
curl -H "Authorization: Bearer $TOKEN" http://localhost:8180/api/v1/dashboard
```

## `Testing`

Used by `src/api/tests/api-integration/playwright.config.ts` (its `webServer` runs this profile); not normally launched by hand.

- `ASPNETCORE_ENVIRONMENT` and `DOTNET_ENVIRONMENT` are `Testing`, so `appsettings.Testing.json` applies.
- Postgres has **no** data volume, so every run starts from the seeded database.
- Mock OIDC is always on. The API still validates real tokens (issuer, signature, audience, lifetime) against it. Testing-only config: `Auth:RequireHttpsMetadata=false` and `Auth:ClockSkewSeconds=0`.
- A WireMock container (`mock-marketdata`, `wiremock/wiremock:3.9.1`) serves canned Alpaca and Tiingo responses from `src/api/tests/api-integration/wiremock/mappings` (bind-mounted read-only). The API's real providers are pointed at it with `MarketData__Alpaca__BaseUrl` and `MarketData__Tiingo__BaseUrl`. Regenerate the mappings with `yarn generate:wiremock` in the test project.
- The scheduled refresh is off (`Refresh:ScheduleEnabled=false`); the suite triggers refreshes with `POST /api/v1/refresh`.
- Child-resource logs and state changes are echoed to the console so a stack that fails to come up can be diagnosed from a pipeline log.

## Mock identity

Defined in `src/Spydersoft.SwingTrade.AppHost/MockOidc.cs`.

| User (password `Test123!`) | `role` claim |
| --- | --- |
| `norole` | none |
| `reader` | `swingtrade.reader` |
| `trader` | `swingtrade.trader` |
| `admin` | `swingtrade.admin` |
| `mcponly` | `swingtrade.mcp` |

Clients (secret `swingtrade-api-test-secret`, resource-owner password grant): `swingtrade-api-test` (normal lifetime) and `swingtrade-api-test-short` (one-second access tokens, used to get a genuinely expired token). Scope `swingtrade` is the API resource the API requires as audience and carries the `role` claim; scope `other-api` is a second resource, used to mint a validly signed token with the wrong audience.

## Postgres password is pinned, not random

Postgres sets its superuser password once, when its data volume is first initialised. `Program.cs` therefore pins a `postgres-password` parameter (default `swingtrade-local-dev-password`, override with `Postgres:Password` in AppHost user secrets) instead of letting Aspire generate a new one each run, which would lock the API out of an existing `Local` volume. Set your own before the first `Local` run:

```sh
dotnet user-secrets set "Postgres:Password" "whatever-you-want" --project src/Spydersoft.SwingTrade.AppHost
```

If you hit a mismatch, remove the Postgres data volume once (`docker volume ls`, `docker volume rm`).

## Leftover containers

If a run is killed rather than stopped cleanly, its containers can outlive it and then block the fixed ports on the next run. List and remove them (`docker ps -a` / `podman ps -a`, then `rm -f`) before starting again.
