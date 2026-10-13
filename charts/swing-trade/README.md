# swing-trade chart

Combined OCI chart for the Swing Trade application's own controllers: `api` and `web`. Published from this repo (`ghcr.io/spydersoft-consulting/charts/swing-trade`), versioned alongside the container images. It mirrors `charts/pitstop` (bjw-s `common` library 4.4.0).

This chart does **not** own or create any Kubernetes `Secret`. It only references secrets **by name**, through `envFrom.secretRef`, supplied by the caller. Backing infrastructure (Postgres) and the `ExternalSecret` resources that create the secrets are owned by `swing-trade-helm-config` (`apps/data`).

## Controllers, services and routes

| Controller | Image | Service | Port | Probes |
| --- | --- | --- | --- | --- |
| `api` | `ghcr.io/spydersoft-consulting/swing-trade-api` | `<release>-api` | 80 to 8080 | `/livez` liveness and startup, `/readyz` readiness |
| `web` | `ghcr.io/spydersoft-consulting/swing-trade-web` | `<release>-web` | 80 to 8080 | `/livez`, `/readyz` |

`api` runs a single replica on purpose: the refresh lock and schedule are in-process (DECISIONS #48), so a second replica would run every scheduled refresh twice.

`/readyz` on the API includes the database (unreachable is Unhealthy, HTTP 503) and price freshness (stale or empty is Degraded, still 200). `/livez` never touches the database.

Two `HTTPRoute`s, both on the **same hostname** (DECISIONS #68):

| Route | Gateway | Paths | Backend |
| --- | --- | --- | --- |
| `web` | private (`main` in `envoy-gateway-system`, as PitStop) | `/` | the BFF, which serves the SPA and forwards `/api/v1` with the user's token |
| `api-mcp` | **public** (placeholder `TODO-owner-public-gateway`) | `/mcp`, `/.well-known/oauth-protected-resource` | the `api` Service directly |

Nothing else is reachable from the internet. The BFF does not proxy `/mcp` or `/.well-known`. Hostnames and the real public gateway are per-environment and supplied by the caller. The `api-mcp` route ships pointing at a gateway that does not exist, so until the owner replaces it the route is not accepted and nothing is exposed.

Optional `mcpAllowlist.enabled=true` adds an Envoy Gateway `SecurityPolicy` on the `api-mcp` route that allows only the listed CIDRs (default `160.79.104.0/21`, Anthropic's outbound range). It needs the Envoy Gateway CRDs and your own address for Claude Code; the API's own checks apply either way.

## Values

- `controllers.api.containers.main.image.tag`, `controllers.web.containers.main.image.tag`: image tags.
- `controllers.*.containers.main.envFrom`: supplied entirely by the caller (every real caller adds its own `configMapRef`s).
- `route.web.hostnames`, `route.api-mcp.hostnames`: per-environment hostnames; not defaulted.
- `route.api-mcp.parentRefs`: the public gateway. TODO(owner).
- `controllers.web.containers.main.env` **is** defaulted: `OidcProxySettings__ReverseProxy__Clusters__swingTradeApi__Destinations__destination1__Address` is `http://{{ include "swing-trade.fullname" . }}-api/`, computed with the chart's own helper (both controllers always live in one release). Do not hardcode a service name. The helper equals the release name when the release name contains `swing-trade` (the config repo uses `swing-trade-app`), which is also what bjw-s uses for the Service names.
- `mcpAllowlist.*`: see above.

## Secrets contract

The caller must create a secret named **`api-secrets`** containing:

| Key | Meaning |
| --- | --- |
| `ConnectionStrings__swingtrade-db` | Npgsql connection string for Postgres |
| `MarketData__Alpaca__KeyId`, `MarketData__Alpaca__SecretKey` | Alpaca market-data credentials (primary provider) |
| `MarketData__Tiingo__Token` | Tiingo token (fallback provider) |
| `Mcp__OwnerSubject` | the owner's identity-server `sub`; the API refuses to start without it outside Development |

The caller must create a secret named **`web-secrets`** containing:

| Key | Meaning |
| --- | --- |
| `OidcProxySettings__Oidc__Authority` | issuer URL, exactly as in the identity server's discovery document |
| `OidcProxySettings__Oidc__ClientId`, `OidcProxySettings__Oidc__ClientSecret` | the BFF's confidential client |
| `Redis__ConnectionString` | Redis for BFF sessions; without it sessions are in memory and die with the pod |

Non-secret configuration (the caller supplies it as ConfigMaps): `Auth__Authority`, `Mcp__PublicUrl` (absolute https, ending in `/mcp`), `Mcp__EnableAddNote`, `MarketData__Providers__0`/`__1` (`alpaca`, `tiingo`), `Telemetry__*`. Neither secret name is hardcoded here: both come from the caller's `envFrom.secretRef.name`.

## Rendering locally

```sh
helm lint charts/swing-trade
helm template swing-trade-app charts/swing-trade -f my-values.yaml
```

`charts/common-4.4.0.tgz` is committed (as in PitStop), so no repository needs to be added.
