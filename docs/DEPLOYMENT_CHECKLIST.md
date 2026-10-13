# Deployment checklist (owner runbook)

One ordered runbook for the first deploy of Swing Trade. Nothing here has been run; the build machine had no access to the identity server, Vault, Azure DevOps, Argo CD or DNS. Detail and rationale live in `docs/IDENTITY_SETUP.md`, `docs/OPERATIONS.md` and `docs/DECISIONS.md` ("Open items for the owner"). Do the steps in order; each ends with a PASS check.

Conventions: `<env>` is `test` or `production`. Hosts: test `swingtrade.mattgerega.net`, production `swingtrade.mattgerega.com`. Identity authority: `https://auth.mattgerega.net` for test, and `https://auth.mattgerega.com` for production because `swing-trade-helm-config` builds it as `https://auth.<environment_hostname>` (see decision D1). Do test completely first, then repeat the environment-specific steps for production.

## Decisions needed from you (recommended defaults)

| ID | Decision | Recommended default | Step |
| --- | --- | --- | --- |
| D1 | Which identity server issues production tokens. `Auth__Authority` is `https://auth.<environment_hostname>`, so production expects `https://auth.mattgerega.com`. | Confirm that issuer exists and serves the same Duende config; if PitStop production uses a different authority, change `Auth__Authority` in `swing-trade-helm-config/apps/swing-trade-app/config/values.yaml.gotmpl` line 9 to match PitStop's. | 1, 2 |
| D2 | Hostnames (DECISIONS owner item 13) | Keep `swingtrade.mattgerega.net` (test) and `swingtrade.mattgerega.com` (production). | 7 |
| D3 | Share PitStop's Redis (`secrets-k8/redis/nonproduction`, `/production`) for BFF sessions (owner item 7) | Share it. Sessions are keyed by cookie name `swingtrade.session`; no data of value is stored. Switch only if you want isolation. | 2 |
| D4 | Public gateway for `/mcp` (owner item 5) | Reuse the existing internet-facing Envoy Gateway if you have one for other public apps. If there is none, do not deploy test publicly yet: use the web UI only and leave `api-mcp` on its placeholder (nothing is exposed). | 7 |
| D5 | Source allowlist `mcpAllowlist` (owner item 6) | Off for the first connection test (so a failure is not an IP problem), then turn on with `160.79.104.0/21` plus your `/32` once the connector works. | 9 |
| D6 | Claude Code loopback redirect | Fixed port `http://localhost:8765/callback` and `--callback-port 8765`. This avoids writing an `IRedirectUriValidator` in the identity repo. | 1 |
| D7 | Register the clients and resources in the identity repo seeder, or only in the admin UI | Admin UI only (the DataSeeder is run by the identity AppHost for local dev; no chart runs it). Add to the seeder too if you want them in source control. | 1 |
| D8 | Production on a tag (release) only | Yes: push `main` for beta to test, tag `1.0.0` for production (decision in `pipeline-deploy.yml`). | 6, 8 |
| D9 | Expectancy and drawdown formulas, tax bracket (owner items 17, 18) | Not blocking. Leave out and keep 27% until you review Settings. | 10 |

## Placeholder inventory (every TODO(owner) in both repos)

| # | File and line | What is needed | Step |
| --- | --- | --- | --- |
| P1 | `swing-trade/.devops/pipeline-ci.yml:5` (header) | Confirm Sonar keys, registry connection, GitHub endpoint | 4, 5 |
| P2 | `swing-trade/.devops/pipeline-ci.yml:59` (`sonarProjectKey: spydersoft-consulting_swing-trade`) | SonarCloud project must exist | 4 |
| P3 | `swing-trade/.devops/pipeline-ci.yml:82` (`containerRegistryName: github-spydersoft-docker`) | Azure DevOps Docker registry service connection | 3 |
| P4 | `swing-trade/src/web/react-sonar-project.properties:1` (`sonar.projectKey=spydersoft-consulting_swing-trade-web-ui`) | SonarCloud UI project must exist | 4 |
| P5 | `swing-trade/charts/swing-trade/values.yaml:152-154` (`TODO-owner-public-gateway`) | Chart default; overridden per environment (P8). Leave as is. | 7 |
| P6 | `swing-trade/charts/swing-trade/values.yaml:177` (`TODO(owner): your own public address, /32`) | Chart comment for the allowlist | 9 |
| P7 | `swing-trade/charts/swing-trade/README.md:23, 34` and `docs/OPERATIONS.md:17`, `docs/IDENTITY_SETUP.md:124` | Documentation references to the gateway; no edit required, update the wording after step 7 | 7 |
| P8 | `swing-trade-helm-config/apps/swing-trade-app/config/values.yaml.gotmpl:84-86` (`name: TODO-owner-public-gateway`) | Real public Gateway name and namespace | 7 |
| P9 | `swing-trade-helm-config/apps/swing-trade-app/config/values.yaml.gotmpl:92` (`mcpAllowlist.enabled: false`) | Enable and add your `/32` after the first successful connect | 9 |
| P10 | `swing-trade-helm-config/apps/data/charts/swing-trade-data/templates/web-es.yaml:22` | BFF Duende client id and secret in Vault `auth` | 1, 2 |
| P11 | `swing-trade-helm-config/apps/data/charts/swing-trade-data/templates/web-es.yaml:35` | Confirm shared Redis (D3) | 2 |
| P12 | `swing-trade-helm-config/.devops/deploy.yml:1` | Argo CD apps and `apps/swing-trade` directories; register `pipeline-deploy.yml` | 3, 6 |
| P13 | `swing-trade-helm-config/README.md:42` (Owner TODOs section) | Summary of P8, P10, P12 | n/a |

After the steps below, delete each marker you resolved (`grep -rn "TODO.owner" .` in both repos must come back empty except the allowlist comment you chose to leave).

## 1. Identity server (repo `d:\spydersoft\identity`, Duende)

Do this on the identity instance for each environment (see D1). Roles and users are created in the admin UI (`Spydersoft.Identity.Admin.Frontend`) or the admin API (`/api/v1/...`, scopes `identity:admin:write`). Clients are not managed by any chart: `DataSeeder` is only run by the AppHost (local), so a client added in source reaches a deployed server only through the admin UI/API or by running the seeder against that database (D7).

### 1.1 Scope and API resource

Admin UI: Scopes > Add `swingtrade` (display name "Swing Trade API"). API Resources > Add `swingtrade`, display name "Swing Trade API", scopes `swingtrade`, user claims `sub` and `role`, `ShowInDiscoveryDocument` true.

Admin API equivalent (create, then add children using the returned `id`):

```http
POST /api/v1/scopes                       { "name": "swingtrade", "displayName": "Swing Trade API", "enabled": true }
POST /api/v1/apiresources                 { "name": "swingtrade", "displayName": "Swing Trade API", "enabled": true }
POST /api/v1/apiresources/{id}/scopes     { "scope": "swingtrade" }
POST /api/v1/apiresources/{id}/claims     { "type": "sub" }
POST /api/v1/apiresources/{id}/claims     { "type": "role" }
```

Seeder equivalent (optional, D7). Add to `Spydersoft.Identity.DataSeeder/Seeding/Resources.cs`:

```csharp
// in ApiScopes
new ApiScope("swingtrade", "Swing Trade API") { Description = "Swing Trade REST API and MCP endpoint" },

// in ApiResources
new ApiResource("swingtrade", "Swing Trade API")
{
    Scopes = { "swingtrade" },
    ShowInDiscoveryDocument = true,
    UserClaims = { JwtClaimTypes.Subject, JwtClaimTypes.Role },
},
```

Note the existing seeded resource uses `ShowInDiscoveryDocument = false`; that is for the admin API. `swingtrade` may stay `false` too, but keep it `true` so the `resource` check in step 9 can be explained from the discovery document.

### 1.2 Roles

Roles are Identity roles (`ApplicationRole`), the same mechanism as `authentication.admin`. Admin UI > Roles, or:

```http
POST /api/v1/roles { "name": "swingtrade.reader" }
POST /api/v1/roles { "name": "swingtrade.trader" }
POST /api/v1/roles { "name": "swingtrade.admin" }
POST /api/v1/roles { "name": "swingtrade.mcp" }
```

(or add `"swingtrade.reader", "swingtrade.trader", "swingtrade.admin", "swingtrade.mcp"` to `Identity.RoleNames` in `Seeding/Identity.cs`.)

Assign to your user: `swingtrade.reader`, `swingtrade.trader`, `swingtrade.admin` now. `swingtrade.mcp` can be assigned now too; it is only used by `/mcp`.

Check the profile service emits the Identity roles as `role` for the `swingtrade` API resource. PitStop does the same for its audience, so it should already; verify in step 1.6.

### 1.3 BFF client (client 1 of 3)

Matches PitStop's BFF: confidential, authorization code, PKCE off (set PKCE to match what OidcProxy.Net sends; if sign-in fails with `invalid_request ... code_challenge`, turn `RequirePkce` on). The callback path is `/oauth2/callback` (the same path PitStop's mock client uses; `docs/apphost-launch-profiles.md`).

Admin API (one POST, then children):

```http
POST /api/v1/clients
{
  "clientId": "swingtrade-web",
  "clientName": "Swing Trade (web BFF)",
  "enabled": true,
  "requireClientSecret": true,
  "requireConsent": false,
  "requirePkce": false,
  "allowOfflineAccess": true,
  "allowAccessTokensViaBrowser": false,
  "accessTokenLifetime": 3600
}
POST /api/v1/clients/{id}/granttypes              { "grantType": "authorization_code" }
POST /api/v1/clients/{id}/redirecturis            { "redirectUri": "https://swingtrade.mattgerega.net/oauth2/callback" }
POST /api/v1/clients/{id}/postlogoutredirecturis  { "postLogoutRedirectUri": "https://swingtrade.mattgerega.net/" }
POST /api/v1/clients/{id}/scopes                  { "scope": "openid" }   // repeat: profile, email, offline_access, swingtrade
```

Add the secret in the admin UI (it hashes it) and generate the value first:

```sh
openssl rand -base64 36        # this is client_secret; store it in Vault in step 2, never in git
```

Seeder equivalent (optional, D7), in `Seeding/Clients.cs` inside `All(...)`; the secret would need a parameter like `adminFrontendClientSecret`, so prefer the admin UI for the secret:

```csharp
new Client
{
    ClientId = "swingtrade-web",
    ClientName = "Swing Trade (web BFF)",
    AllowedGrantTypes = { GrantType.AuthorizationCode },
    RequirePkce = false,
    RequireConsent = false,
    ClientSecrets = { Hashed(swingTradeWebSecret.Sha256()) },
    RedirectUris = { "https://swingtrade.mattgerega.net/oauth2/callback", "https://swingtrade.mattgerega.com/oauth2/callback" },
    PostLogoutRedirectUris = { "https://swingtrade.mattgerega.net/", "https://swingtrade.mattgerega.com/" },
    AllowedScopes = { "openid", "profile", "email", "swingtrade" },
    AllowOfflineAccess = true,
},
```

Production: use a separate client (or add the `.com` URIs) and a separate secret per environment; the Vault paths are per environment.

### 1.4 Claude connector client (client 2 of 3, confidential)

Exact definition is in `docs/IDENTITY_SETUP.md` (client `swingtrade-claude-connector`). Admin API form:

```http
POST /api/v1/clients
{
  "clientId": "swingtrade-claude-connector",
  "clientName": "Claude custom connector (Swing Trade MCP)",
  "enabled": true,
  "requireClientSecret": true,
  "requireConsent": false,
  "requirePkce": true,
  "allowPlainTextPkce": false,
  "allowOfflineAccess": true,
  "accessTokenLifetime": 3600
}
POST /api/v1/clients/{id}/granttypes   { "grantType": "authorization_code" }
POST /api/v1/clients/{id}/redirecturis { "redirectUri": "https://claude.ai/api/mcp/auth_callback" }
POST /api/v1/clients/{id}/scopes       { "scope": "swingtrade" }
POST /api/v1/clients/{id}/scopes       { "scope": "offline_access" }
```

Generate its secret (`openssl rand -base64 36`), add it in the admin UI, and keep the plain value in your password manager only (you type it into Claude in step 9).

### 1.5 Claude Code client (client 3 of 3, public)

```http
POST /api/v1/clients
{
  "clientId": "swingtrade-claude-code",
  "clientName": "Claude Code (Swing Trade MCP)",
  "enabled": true,
  "requireClientSecret": false,
  "requireConsent": false,
  "requirePkce": true,
  "allowPlainTextPkce": false,
  "allowOfflineAccess": true,
  "accessTokenLifetime": 3600
}
POST /api/v1/clients/{id}/granttypes   { "grantType": "authorization_code" }
POST /api/v1/clients/{id}/redirecturis { "redirectUri": "http://localhost:8765/callback" }   // D6
POST /api/v1/clients/{id}/scopes       { "scope": "swingtrade" }
POST /api/v1/clients/{id}/scopes       { "scope": "offline_access" }
```

If you do not take D6, use `http://127.0.0.1` and `http://localhost` and add the port-ignoring `IRedirectUriValidator` described in `docs/IDENTITY_SETUP.md`.

### 1.6 PASS check

Get a token for the BFF client or the Claude Code client by following step 9 (the manual flow), decode the access token (`jwt decode`, or `echo $T | cut -d. -f2 | base64 -d`) and confirm: `iss` equals `Auth__Authority` exactly, `aud` contains `swingtrade`, `sub` present, `role` contains `swingtrade.*` values. FAIL signals: no `role` (the resource `UserClaims` or profile service is wrong), no `aud` (the scope is not on the `ApiResource`).

Record your `sub` for step 2 (the `mcp` entry).

## 2. Vault (`secrets-k8/swing-trade/<env>/...`)

PitStop reads `secrets-k8/pitstop/<env>/postgres|auth|data-api-client|google-places` and `secrets-k8/redis/<nonproduction|production>`; swing-trade follows the same tree. Mount is `secrets-k8` (KV v2, the `vault-backend` ClusterSecretStore). Property names are exactly what the ExternalSecrets reference. Run for `test`, then `production` (separate values for each).

```sh
ENV=test            # then production
vault kv put secrets-k8/swing-trade/$ENV/postgres \
  rootUser=postgres rootPassword="$(openssl rand -base64 24)" \
  user=swingtrade  password="$(openssl rand -base64 24)"

vault kv put secrets-k8/swing-trade/$ENV/market-data \
  alpaca_key_id='<from Alpaca>' alpaca_secret_key='<from Alpaca>' tiingo_token='<from Tiingo>'

vault kv put secrets-k8/swing-trade/$ENV/mcp owner_subject='<your sub from step 1.6>'

vault kv put secrets-k8/swing-trade/$ENV/auth \
  authority='https://auth.mattgerega.net' \
  client_id='swingtrade-web' client_secret='<secret from step 1.3 for this environment>'
```

Notes: the `auth.authority` value is used by the BFF (`OidcProxySettings__Oidc__Authority`) and must equal the issuer string in the discovery document with no trailing slash; the API's `Auth__Authority` comes from `values.yaml.gotmpl:9`. Redis (D3): nothing to write if sharing; verify the entry exists and has `host` and `password`:

```sh
vault kv get secrets-k8/redis/nonproduction     # test (properties: host, password)
vault kv get secrets-k8/redis/production
```

If you refuse D3, create `secrets-k8/redis/swing-trade-<env>` and change the two `key: secrets-k8/redis/{{ .Values.redis.environment }}` entries in `web-es.yaml` (the `redis_host` and `redis_password` refs) to that key.

Does the Vault policy / ExternalSecrets role cover `secrets-k8/swing-trade/*`? PitStop's does for `secrets-k8/pitstop/*`; if the policy lists paths explicitly, add the new prefix to the same policy (the policy file is in the platform/ops repos, not changed here).

PASS: `vault kv get secrets-k8/swing-trade/test/postgres` returns the 4 keys, and likewise `market-data` (3), `mcp` (1), `auth` (3). Later (step 7) the ExternalSecrets show `SecretSynced`.

## 3. Azure DevOps and GitHub

1. Repos: push both repos to GitHub under `spydersoft-consulting`: `swing-trade` and `swing-trade-helm-config` (nothing was pushed; owner item 15).
   ```sh
   cd swing-trade && git remote add origin https://github.com/spydersoft-consulting/swing-trade.git && git push -u origin main
   cd ../swing-trade-helm-config && git remote add origin https://github.com/spydersoft-consulting/swing-trade-helm-config.git && git push -u origin main
   ```
2. Service connections (both exist for PitStop; confirm they are usable by the new pipelines, tick "Grant access permission to all pipelines" or authorize on first run):
   - GitHub endpoint `spydersoft-gh` (used by `resources.repositories` in both pipelines).
   - Docker registry connection `github-spydersoft-docker` (ghcr.io; P3).
   - Feed credential `SpydersoftGithub` (NuGet GitHub Packages feed, `externalFeedCredentials`).
   - SonarCloud connection `sonarcloud-spydersoft`.
3. Register the pipelines (Pipelines > New pipeline > GitHub > existing YAML):
   - `swing-trade`: `/.devops/pipeline-ci.yml`, name `swing-trade-ci`.
   - `swing-trade-helm-config`: `/.devops/pipeline-deploy.yml`, name `swing-trade-deploy`.
4. The deploy pipeline commits to GitHub repos `ops-nonprod-cluster` and `ops-prod-cluster` through `spydersoft-gh`; the identity behind that endpoint must have write access (it does for PitStop).
5. Variable groups: if `pipeline-ci.yml` templates for PitStop use a variable group or secret (for example for the helm-config commit token), link the same group to the swing-trade pipeline. Compare with PitStop's pipeline settings page; nothing in this repo names a variable group.

PASS: both pipelines appear in Azure DevOps and the "Resources" authorization prompt does not block on the first run.

## 4. SonarCloud

Organization `spydersoft-consulting`, connection `sonarcloud-spydersoft`. Create two projects (Add project > manually) with these exact keys (P2, P4):

| Key | Name |
| --- | --- |
| `spydersoft-consulting_swing-trade` | swing-trade |
| `spydersoft-consulting_swing-trade-web-ui` | swing-trade-web-ui |

Set each to "Previous code" or "New code" as for PitStop and choose "With Azure DevOps Pipelines" or "Other CI" (the pipeline runs the analysis, not auto-analysis; turn automatic analysis off). Generate no new token if `sonarcloud-spydersoft` already has one.

Decision: if your PitStop keys use a different organization key prefix, change `sonarProjectKey` (`pipeline-ci.yml:60`) and `sonar.projectKey` (`react-sonar-project.properties:2`) to match before the first run.

PASS: both projects exist and are empty; the first analysis (step 5) populates them.

## 5. First CI run

1. From the app repo, push `main` (already done in step 3.1). This triggers `swing-trade-ci` as a beta build.
2. Watch for: Build, unit tests, `integration_tests` (both Playwright suites), `docker_publish`, `chart_publish`, `updateHelmConfig`.
3. Expected outputs:
   - Images `ghcr.io/spydersoft-consulting/swing-trade-api:<ver>` and `swing-trade-web:<ver>`.
   - Chart `oci://ghcr.io/spydersoft-consulting/charts/swing-trade` version `<ver>` (beta, `X.Y.Z-beta.n`).
   - A commit in `swing-trade-helm-config` changing `environments/test/images.yaml` from `0.0.0` to the build number (the pipeline pushes it; `process-cicd.ps1`).
4. Make GHCR packages readable by the cluster pull secret (new packages are private; PitStop's images are accessible, so grant the same access: GitHub > Packages > each of the three > Package settings > Manage access, or link them to the repo).

```sh
git -C ../swing-trade-helm-config pull && cat ../swing-trade-helm-config/environments/test/images.yaml
```

PASS: pipeline green; `images.yaml` no longer contains `0.0.0`; `helm pull oci://ghcr.io/spydersoft-consulting/charts/swing-trade --version <ver>` succeeds after `helm registry login ghcr.io`. FAIL modes: the Sonar step fails with "project not found" (step 4), the registry login fails (P3), or the Playwright stage cannot start containers on the agent (should not: ubuntu-latest has Docker).

## 6. Argo CD applications (nonprod and prod clusters)

Repos not available on this machine (`ops-nonprod-cluster`, `ops-prod-cluster`), so the manifests below are the standard Argo CD form and must be aligned with how PitStop's application is declared in those repos (copy PitStop's Application file and replace names). The deploy pipeline renders `helmfile` output and commits it to `apps/swing-trade/` in each repo (`argoBaseDir: apps/swing-trade` in `swing-trade-helm-config/.devops/deploy.yml`, P12).

1. In `ops-nonprod-cluster` find the directory where the PitStop Application lives (search: `grep -rl pitstop .` in that repo). Create the same for swing-trade:

```yaml
# <apps-dir>/swing-trade.yaml  (ops-nonprod-cluster)
apiVersion: argoproj.io/v1alpha1
kind: Application
metadata:
  name: swing-trade
  namespace: argocd
spec:
  project: default
  source:
    repoURL: https://github.com/spydersoft-consulting/ops-nonprod-cluster.git
    targetRevision: HEAD
    path: apps/swing-trade
    directory:
      recurse: true
  destination:
    server: https://kubernetes.default.svc
    namespace: swing-trade-test
  syncPolicy:
    automated: { prune: true, selfHeal: true }
    syncOptions:
      - CreateNamespace=true
```

2. Same in `ops-prod-cluster` with `repoURL: .../ops-prod-cluster.git` and `namespace: swing-trade-production`.
3. The generated manifests are committed under `apps/swing-trade/` by the pipeline; create the directory with a placeholder if Argo complains about a missing path:

```sh
mkdir -p apps/swing-trade && touch apps/swing-trade/.gitkeep && git add apps/swing-trade && git commit -m "Add swing-trade app directory" && git push
```

4. Namespaces `swing-trade-test` and `swing-trade-production` are created by the manifests (the generated files include them if PitStop's do; otherwise keep `CreateNamespace=true`).
5. Run the deploy pipeline (it triggers on the `environments/test/*` change from step 5): Pipelines > `swing-trade-deploy` > Run if it did not trigger.

PASS: Argo shows `swing-trade` Synced and Healthy for test. Check:

```sh
kubectl -n swing-trade-test get pods                 # swing-trade-data-postgres, swing-trade-app-api, swing-trade-app-web all Running 1/1
kubectl -n swing-trade-test get externalsecret       # api-es, web-es, postgres-secrets-es: STATUS SecretSynced, READY True
kubectl -n swing-trade-test get secret api-secrets web-secrets postgres-secrets
kubectl -n swing-trade-test logs deploy/swing-trade-app-api | head -40   # migrations applied, no startup exception
```

FAIL signals: `ErrImagePull` (GHCR access, step 5.4); ExternalSecret `SecretSyncedError` (Vault path/property or policy, step 2); API `CrashLoopBackOff` with "Mcp:PublicUrl" or "Mcp:OwnerSubject" (the `mcp` Vault entry empty); `Unhealthy` with Postgres auth error (Postgres init script ran with old values: see OPERATIONS key rotation).

## 7. Gateway and DNS

1. The `web` route uses the private gateway `main` in `envoy-gateway-system` (chart default, same as PitStop; no change). Hostname `swingtrade.<environment_hostname>`.
2. DNS: add `swingtrade.mattgerega.net` the same way `pitstop.mattgerega.net` is published (same target as the private gateway's address; check the record for pitstop and copy it, a CNAME or A to the gateway/load balancer), for the internal view. For the public MCP route the name must also resolve from the internet to the public gateway address: if the private and public gateways share one name, that conflicts, so split-horizon DNS (internal answer = private gateway, public answer = public gateway) is required. TLS: the certificate for `swingtrade.<host>` must be present on both gateways' listeners (cert-manager or a wildcard; PitStop's listener probably uses a wildcard for `*.mattgerega.net`/`.com`).
3. Public gateway (D4, P8). Find the name:

```sh
kubectl get gateway -A
```

Pick the internet-facing one and set, in `swing-trade-helm-config/apps/swing-trade-app/config/values.yaml.gotmpl` (lines 84-89):

```yaml
    parentRefs:
      - name: <public-gateway-name>
        namespace: <its-namespace>
        group: gateway.networking.k8s.io
        kind: Gateway
```

If test and production use different public gateways, wrap with the existing environment test used elsewhere in the file: `{{ if ne .Environment.Name "production" }} ... {{ else }} ... {{ end }}`. Remove the TODO comment lines. Commit; the next deploy-pipeline run renders it. The route listener must allow routes from the `swing-trade-<env>` namespace (`allowedRoutes` on the Gateway listener: `namespaces.from: All` or a selector) and must have a listener for the hostname with a TLS certificate.

PASS: after sync:

```sh
kubectl -n swing-trade-test get httproute            # web and api-mcp
kubectl -n swing-trade-test get httproute swing-trade-app-api-mcp -o jsonpath='{.status.parents[*].conditions[*].type}{"\n"}'   # Accepted ResolvedRefs, both True
nslookup swingtrade.mattgerega.net
curl -sI https://swingtrade.mattgerega.net/livez     # from inside the network: HTTP/2 200 (BFF)
```

Negative check from the public internet (a phone on mobile data): `curl -si https://swingtrade.mattgerega.net/api/v1/trades` and `curl -si https://swingtrade.mattgerega.net/` must NOT return the app or API (404 or no route); `curl -si https://swingtrade.mattgerega.net/.well-known/oauth-protected-resource` must return 200 JSON.

## 8. First deploy checks (test)

Run these in order.

1. Pods and readiness (PASS: all Running):
   ```sh
   kubectl -n swing-trade-test get pods
   kubectl -n swing-trade-test exec deploy/swing-trade-app-api -- wget -qO- http://localhost:8080/readyz
   ```
   Expect `Degraded` ("No price data yet") with database `Healthy`. `Unhealthy`/503 means the database is down or the connection string is wrong: check `postgres-secrets` and `api-secrets`.
2. Sign in. Open `https://swingtrade.mattgerega.net/`. You should be redirected to `https://auth.mattgerega.net`, sign in, and land on the Watchlist. PASS: the header shows your name, Settings shows the roles `swingtrade.admin` etc. FAIL: `invalid_redirect_uri` (step 1.3 redirect exact match), `invalid_scope` (scope not allowed on the BFF client), "Access not granted" (no `swingtrade.*` role on the token).
3. Load prices once (the API never refreshes at start in a deployment). In the UI, Watchlist > Refresh now (admin). Equivalent API check in the browser console of the signed-in site:
   ```js
   await fetch('/api/v1/refresh', { method: 'POST' }).then(r => r.status)   // 202
   await fetch('/api/v1/refresh/runs').then(r => r.json())                  // newest item: status Succeeded
   ```
   PASS: the run is `Succeeded` (12 tickers), the Watchlist shows as-of dates and no stale badge (on a trading evening after 16:15 New York). `Failed` with 401/403 means the Alpaca key or Tiingo token is wrong (step 2).
4. `/readyz` after the refresh (PASS: `Healthy`, HTTP 200):
   ```sh
   kubectl -n swing-trade-test exec deploy/swing-trade-app-api -- wget -qO- http://localhost:8080/readyz
   kubectl -n swing-trade-test exec deploy/swing-trade-app-web -- wget -qO- http://localhost:8080/readyz
   ```
   The web `/readyz` includes a Redis ping; failure there is the shared Redis entry (D3, step 2).
5. Public MCP metadata and challenge (from the internet, PASS values given):
   ```sh
   curl -si https://swingtrade.mattgerega.net/.well-known/oauth-protected-resource
   #  HTTP 200; "resource": "https://swingtrade.mattgerega.net/mcp"; "authorization_servers": ["https://auth.mattgerega.net"]
   curl -si -X POST https://swingtrade.mattgerega.net/mcp -H 'content-type: application/json' -d '{}'
   #  HTTP 401 with: WWW-Authenticate: Bearer resource_metadata="https://swingtrade.mattgerega.net/.well-known/oauth-protected-resource"
   ```
   FAIL: 404 (the route is not accepted: step 7); 421 (Host mismatch: `Mcp__PublicUrl` host differs from the Host header the gateway forwards; set `Mcp__AllowedHosts__0`).
6. Review Settings (admin): starting capital, risk, tax rate. They are database rows.

## 9. Claude connector, Claude Code, and the `aud` check for `resource`

This is the main unverified item (DECISIONS #76, owner item 3, 16). Do the `resource` check before involving Claude.

### 9.1 Manual `resource` / `aud` check (PASS before continuing)

Uses the Claude Code client (public; redirect from step 1.5). Run in a bash shell; use a real browser for the one login step.

```sh
AUTH=https://auth.mattgerega.net
RES=https://swingtrade.mattgerega.net/mcp
CID=swingtrade-claude-code
REDIR=http://localhost:8765/callback

curl -s $AUTH/.well-known/openid-configuration | jq '{issuer, authorization_endpoint, token_endpoint, code_challenge_methods_supported, scopes_supported}'

VERIFIER=$(openssl rand -base64 48 | tr -d '=+/' | cut -c1-64)
CHALLENGE=$(printf %s "$VERIFIER" | openssl dgst -sha256 -binary | openssl base64 -A | tr '+/' '-_' | tr -d '=')
echo "$AUTH/connect/authorize?client_id=$CID&response_type=code&scope=swingtrade%20offline_access&redirect_uri=$(printf %s $REDIR | jq -sRr @uri)&code_challenge=$CHALLENGE&code_challenge_method=S256&resource=$(printf %s $RES | jq -sRr @uri)&state=x"
```

Open the printed URL in a browser, sign in. The browser lands on `http://localhost:8765/callback?code=...` (a connection error page is fine); copy the `code` value.

```sh
CODE='<paste code>'
curl -s $AUTH/connect/token -d grant_type=authorization_code -d client_id=$CID -d code=$CODE \
  -d redirect_uri=$REDIR -d code_verifier=$VERIFIER -d resource=$RES | tee token.json | jq 'del(.access_token,.refresh_token)'
jq -r .access_token token.json | cut -d. -f2 | tr '_-' '/+' | base64 -d 2>/dev/null | jq '{iss, aud, sub, role, scope}'
```

PASS: the authorize step shows the login page (no `invalid_target` error), the token response contains `access_token`, and the decoded payload has `aud` containing `swingtrade` (string or array), `role` containing `swingtrade.mcp`, and `sub` equal to the value in Vault `mcp/owner_subject`. Then call the endpoint:

```sh
T=$(jq -r .access_token token.json)
curl -si -X POST $RES -H "authorization: Bearer $T" -H 'content-type: application/json' -H 'accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' | head -20       # HTTP 200 with the tool list
```

FAIL decisions:
- `invalid_target` at authorize or token: Duende rejects the `resource` value because it matches no `ApiResource`. Choose, in this order of preference: (a) a custom `IResourceValidator` in the identity repo that maps `https://swingtrade.<host>/mcp` to the `swingtrade` resource (keeps `aud: swingtrade`, no change here); (b) as a second ApiResource named after the MCP URL, which needs the API to accept that value as an additional audience (a code change in this repo, `SwingTradeAuth`; not done, ask for it). Do not weaken `aud` validation in the API.
- 403 on `/mcp`: the token `sub` differs from `owner_subject`, or the `swingtrade.mcp` role is missing.
- 401 with `error="invalid_token"`: wrong `aud` or `iss`; compare with `Auth__Authority`.

### 9.2 Claude custom connector (claude.ai or Desktop)

1. Settings > Connectors > Add custom connector. URL `https://swingtrade.mattgerega.com/mcp` (production) or the test URL.
2. Advanced settings: OAuth Client ID `swingtrade-claude-connector`, client secret from your password manager.
3. Connect, sign in as the owner, approve.
4. PASS: ask "What are my pullback candidates and how many shares for each?" and Claude calls the tools and answers with figures matching the Watchlist. Then Settings > MCP panel in the app (admin) lists the calls.
5. FAIL decisions: if the connector will not accept a pre-registered client or tries Dynamic Client Registration (Duende has none, DECISIONS finding), fall back: report to the project; a static bearer token fallback was deliberately not built (DECISIONS #67).

### 9.3 Claude Code

```sh
claude mcp add --transport http --client-id swingtrade-claude-code --callback-port 8765 swingtrade https://swingtrade.mattgerega.net/mcp
claude    # then run /mcp and authenticate swingtrade
```

PASS: `/mcp` shows swingtrade connected with its tools. An enabled allowlist (D5) must include your `/32` for this path.

## 10. After it works

1. Allowlist (D5, P9): set `mcpAllowlist.enabled: true` and add your address to `cidrs` in `values.yaml.gotmpl` (lines 92-97), keeping `160.79.104.0/21`. Needs the Envoy Gateway `SecurityPolicy` CRD (`kubectl get crd securitypolicies.gateway.envoyproxy.io`). PASS: the connector and Claude Code still work; a request from another address returns 403 from the gateway.
2. Compare MA20 and MA50 for a few tickers with your broker (`docs/M3_MANUAL_CHECK.md`).
3. Production: tag the release.
   ```sh
   cd swing-trade && git tag 1.0.0 && git push origin 1.0.0
   ```
   CI builds an unlabeled release and bumps `environments/test/images.yaml`; `swing-trade-deploy` then promotes it (copies `images.yaml` to production) and commits to `ops-prod-cluster`. Repeat steps 1.3 (production URIs), 2, 6, 7, 8 and 9 for production (`swingtrade.mattgerega.com`).
4. Take a first backup and test a restore (`docs/OPERATIONS.md` section 2).
5. Remove resolved `TODO(owner)` markers and update `docs/DECISIONS.md` ("Open items" and #76) with what you verified.

## Quick failure map

| Symptom | Where to look |
| --- | --- |
| Sign-in loop or `invalid_redirect_uri` | Step 1.3 redirect URI (scheme, host, `/oauth2/callback`); `web-secrets` client id and secret |
| Signed in but "Access not granted" | No `swingtrade.*` role in the access token (step 1.2, 1.6) |
| API pod crash on start | `Mcp__PublicUrl`/`Mcp__OwnerSubject` (step 2 `mcp`), Postgres credentials |
| ExternalSecret not synced | Vault path/property names (step 2), Vault policy |
| `/mcp` 404 from the internet | `api-mcp` route not accepted by the public gateway (step 7) |
| `/mcp` 401 `invalid_token` | `iss`/`aud` mismatch (step 9.1) |
| `/mcp` 403 | `sub` is not the owner or the `swingtrade.mcp` role is missing |
| Prices stale | `docs/OPERATIONS.md` section 3 |
