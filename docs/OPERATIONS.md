# Operations

For the owner running Swing Trade in the cluster. Local development is in `README.md`. Names below follow `swing-trade-helm-config`: namespace `swing-trade-<env>` (`test`, `production`), Postgres deployment `swing-trade-data-postgres`, database `swingtrade`, app release `swing-trade-app`.

## 1. First-deploy checklist

Nothing here has been run yet; each step says where its detail lives.

1. **Identity server** (`docs/IDENTITY_SETUP.md`, repo `d:\spydersoft\identity`):
   1. Scope `swingtrade` and `ApiResource` `swingtrade` with `UserClaims = { sub, role }` (so the access token has `aud: swingtrade` and `role`).
   2. The BFF client (confidential, authorization code, redirect `https://swingtrade.<host>/oauth2/callback`, scopes `openid profile email offline_access swingtrade`). Store its id and secret in Vault (below).
   3. Roles `swingtrade.reader`, `.trader`, `.admin`, `.mcp`; assign reader, trader and admin to the owner. Assign `.mcp` only when you set up Claude.
   4. The two MCP clients (`swingtrade-claude-connector`, `swingtrade-claude-code`) when you are ready for Claude (section 4).
2. **Vault** (`secrets-k8/swing-trade/<env>/...`, never committed): `postgres` (`rootUser`, `rootPassword`, `user`, `password`), `market-data` (`alpaca_key_id`, `alpaca_secret_key`, `tiingo_token`), `mcp` (`owner_subject`), `auth` (`authority`, `client_id`, `client_secret`). Redis comes from the shared `secrets-k8/redis/<nonproduction|production>` entry; confirm swing-trade may share it.
   - Find the owner's `sub` by decoding an access token for the owner (`jwt decode`) or in the identity database; put it in `mcp/owner_subject`. The API refuses to start without it.
3. **Cluster plumbing** (`swing-trade-helm-config/.devops/deploy.yml`): the `apps/swing-trade` directories and Argo CD applications in `ops-nonprod-cluster` and `ops-prod-cluster`; register `pipeline-deploy.yml`.
4. **Gateway**: fill in the public gateway name for the `/mcp` route (`TODO(owner)` in `apps/swing-trade-app/config/values.yaml.gotmpl`) and, optionally, enable the CIDR allowlist.
5. **Pipeline**: confirm the SonarCloud projects (`spydersoft-consulting_swing-trade`, `spydersoft-consulting_swing-trade-web-ui`) and the registry service connection, then push to `main`. CI builds, runs both Playwright suites, publishes the images and the chart, and bumps `environments/test/images.yaml`. A tag (`git tag 1.0.0`) builds a release, which the deploy pipeline promotes to production.
6. **Migrations and seed** run on the API's first start (Settings defaults, the 24 tickers, the sample XLE trade). Check `kubectl -n swing-trade-test get pods` and `GET /readyz`.
7. **Load prices once.** The API never refreshes at start in a deployment (DECISIONS #46). Sign in as admin, press **Refresh now** (or `POST /api/v1/refresh`), and watch `GET /api/v1/refresh/runs/{id}`. `GET /readyz` reads `Degraded` ("No price data yet") until then; afterwards `Healthy`. Check the 12 active tickers have an as-of date and no stale badge.
8. **Real data check**: compare MA20 and MA50 for a few tickers with your broker chart (`docs/M3_MANUAL_CHECK.md`).
9. **Review Settings** (admin): starting capital, risk, tax rate (an assumption), thresholds. Settings are database rows, not configuration.

## 2. Backup and restore (PostgreSQL)

**What to back up: the whole database.** The tables are `Settings`, `Instruments`, `Trades`, `JournalNotes`, `PriceBars`, `IndicatorSnapshots`, `RefreshRuns`, `McpCallLogs` (plus `__EFMigrationsHistory`).

| Table | Importance |
| --- | --- |
| `Trades`, `JournalNotes`, `Settings` | **Irreplaceable.** They are your journal, notes and thresholds. |
| `Instruments` | Small; your watchlist choices. |
| `PriceBars`, `IndicatorSnapshots` | **Re-creatable.** A refresh re-backfills 200 days per ticker from the provider. Old snapshots (history) are not recomputed, so a restore without them only loses past as-of rows. |
| `RefreshRuns`, `McpCallLogs` | Audit trail; nice to keep, safe to lose (`McpCallLogs` is purged after 90 days anyway). |

The data volume is an NFS `PersistentVolumeClaim` (10Gi). That is storage, not a backup. Take logical dumps regularly (weekly is enough for one hour of use a week, and always before a risky deploy).

Dump (custom format, compressed), run from a machine with `kubectl`:

```sh
NS=swing-trade-production
kubectl -n $NS exec deploy/swing-trade-data-postgres -- \
  sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" pg_dump -U "$POSTGRES_USER" -d swingtrade -Fc' \
  > swingtrade-$(date +%F).dump
```

Journal-only dump (smaller; prices come back with a refresh):

```sh
kubectl -n $NS exec deploy/swing-trade-data-postgres -- \
  sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" pg_dump -U "$POSTGRES_USER" -d swingtrade -Fc -t \"Trades\" -t \"JournalNotes\" -t \"Settings\" -t \"Instruments\"' \
  > swingtrade-journal-$(date +%F).dump
```

Keep the dump somewhere other than the cluster (encrypted; it holds your trades). `POSTGRES_USER`/`POSTGRES_PASSWORD` in the pod are the root user from the `postgres-secrets` secret.

Restore into an empty database (a new cluster, or after dropping and recreating `swingtrade`):

```sh
kubectl -n $NS scale deploy/swing-trade-app-api --replicas=0          # stop the API so nothing writes
kubectl -n $NS exec -i deploy/swing-trade-data-postgres -- \
  sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" pg_restore -U "$POSTGRES_USER" -d swingtrade --clean --if-exists --no-owner --role="$POSTGRES_NON_ROOT_USER"' \
  < swingtrade-2026-10-12.dump
kubectl -n $NS scale deploy/swing-trade-app-api --replicas=1
```

If the app user cannot see the restored tables, `ALTER DATABASE swingtrade OWNER TO <user>;` and re-run with `--no-owner --role=<user>`. After a restore, press **Refresh now** to catch prices up. **Test a restore once** into a scratch database (`createdb swingtrade_restore`, `pg_restore -d swingtrade_restore ...`) before you need it. A dump taken with a newer schema than the running image needs the newer image (migrations only go forward).

Locally (Aspire's Postgres): the host port is shown in the Aspire dashboard; use `pg_dump -h localhost -p <port> -U postgres -d swingtrade -Fc`. The data volume is `spydersoft.swingtrade.apphost-<hash>-postgres-data`.

Also export the CSV (`GET /api/v1/export/trades.csv`) at tax time and keep it with your records; it is a convenience view, not a backup.

## 3. Refresh schedule and staleness troubleshooting

Weekdays, evaluated in `America/New_York` (DECISIONS #46): 17:30 primary, 20:30 second attempt (always runs, so late corrections overwrite), 08:00 next morning catch-up (runs only if some ticker lacks the latest expected bar). There is no holiday calendar: on a market holiday every ticker reads stale until the next session. One refresh runs at a time (scheduler and manual share a lock); a crash leaves a `Running` row that is marked `Abandoned` at the next run.

A stale badge means a ticker's as-of date is older than the most recent weekday close (after 16:15 New York). `GET /readyz` reports the same thing as `Degraded` ("Prices are stale"), and `GET /api/v1/refresh/runs` shows what happened. Work down this list:

1. **Is it just a holiday or early evening?** Before 16:15 New York the latest expected bar is the previous weekday. After a holiday the badge clears at the next session.
2. **Last run** (`GET /api/v1/refresh/runs`, or the Watchlist screen): `Succeeded`, `Partial` (some tickers failed; the `Message` lists them), `Failed`, `Abandoned`, `Cancelled`. A message like "XLK via tiingo" means Alpaca could not serve it and the fallback did.
3. **Credentials** (`Failed` with 401/403 messages): the Alpaca key or Tiingo token is wrong, expired or revoked. Rotate it (section 6). The fallback is tried on 401/403.
4. **Rate limits** (429/5xx): retried with back-off (3 attempts); the 20:30 and 08:00 attempts usually recover. Check the provider's status page.
5. **One ticker only**: delisted, renamed or unknown to the provider (400/404/422). Deactivate it in the UI.
6. **Nothing ran**: check the API log for `Next ... refresh at ...` lines and for `Refresh:ScheduleEnabled` (must be true). Only one API replica should run. The pod's clock and the `tzdata` in the image matter: a missing zone would throw at start (verified present in the image, DECISIONS #108).
7. **Run it by hand**: Watchlist, **Refresh now** (admin), or `POST /api/v1/refresh`. `409` means one is already running.
8. **A split** shows as a large step in a ticker's closes; the run detects a restated history (more than 10% difference on re-requested bars) and re-backfills that ticker (DECISIONS #43).

Logs: Serilog to the OTLP collector (Grafana Alloy); refusals are logged too (section 7).

## 4. MCP exposure checklist and security notes

Claude's cloud apps need a public HTTPS endpoint; the app publishes only `/mcp` and the OAuth discovery path (DECISIONS #67, #68). Before turning it on:

- [ ] `Mcp__PublicUrl` is the absolute https URL (`https://swingtrade.<host>/mcp`, a ConfigMap) and `Mcp__OwnerSubject` is the owner's `sub` (Vault). The API will not start otherwise.
- [ ] The `api-mcp` HTTPRoute is attached to the **public** gateway, matches **only** `/mcp` and `/.well-known/oauth-protected-resource`, and backs onto the **api** Service (not the BFF). `curl https://swingtrade.<host>/api/v1/trades` from the internet must not reach the API, and `/` must not reach the UI.
- [ ] Optional: enable the CIDR allowlist (`160.79.104.0/21` for Anthropic's cloud apps plus your own `/32` for Claude Code).
- [ ] Unauthenticated `GET https://swingtrade.<host>/.well-known/oauth-protected-resource` returns the metadata; `POST /mcp` without a token returns `401` with `WWW-Authenticate: Bearer resource_metadata="..."`.
- [ ] Identity server: the two clients exist with the exact redirect URIs; `swingtrade.mcp` is assigned to the owner only. Verify Duende emits `aud: swingtrade` for a request that carries the MCP URL as the RFC 8707 `resource` (`invalid_target` is the usual failure; see `docs/IDENTITY_SETUP.md`). **Not yet verified** against the real server.
- [ ] Add the custom connector in Claude (URL, then Advanced settings: the connector client id and secret), sign in as the owner, and ask for the pullback candidates. **Not yet verified** with the real connector.
- [ ] After the first calls, Settings, MCP panel (admin) shows them, and no token appears in the log.

Security notes:

- A valid token is necessary but not sufficient: the endpoint also requires the `swingtrade.mcp` role and `sub == Mcp:OwnerSubject`. Another user, even with the role, gets 403. The REST roles do not grant MCP and the MCP role does not grant REST.
- Tokens are accepted from the `Authorization` header only, never the query string. They are never logged; the call log keeps the caller's `sub`, tool, sanitised arguments and duration for 90 days.
- Defence in depth: a Host allowlist (421), a fixed-window rate limit (default 120 per minute, 429), no CORS. The tools are read-only; `add_note` is off (`Mcp__EnableAddNote=false`) and, if enabled, also needs `swingtrade.trader`.
- To cut off Claude immediately: remove `swingtrade.mcp` from the owner (new tokens), disable the `api-mcp` route (`route.api-mcp.enabled: false`), or set `Mcp__OwnerSubject` to something unused. Access tokens already issued live up to an hour (`AccessTokenLifetime`).
- The data is personal financial data. Keep the public route as narrow as it is; never add CORS or widen the path list.

## 5. Retention

- `McpCallLogs`: rows older than 90 days are deleted at API start and every six hours (`Mcp:RetentionDays` is a constant, not configuration).
- `PriceBars`, `IndicatorSnapshots`: kept forever (small: roughly 24 tickers by 250 days a year).
- `RefreshRuns`, `JournalNotes`, `Trades`: kept forever.

## 6. Key rotation

| Secret | Where | How |
| --- | --- | --- |
| Alpaca key and secret, Tiingo token | Vault `market-data` | Generate new ones at the provider, update Vault, wait for the ExternalSecret (`refreshInterval` 1h) or force a sync, then restart the API (`kubectl rollout restart deploy/swing-trade-app-api`; env vars are read at start). Press **Refresh now** to confirm. |
| BFF client secret | Vault `auth` and Duende client | Add the new secret to the Duende client (it can hold two), update Vault, restart the web deployment, remove the old secret. Users are signed out only if sessions are lost. |
| Claude connector client secret | Duende client `swingtrade-claude-connector` | Generate a new secret, update it in Claude (Settings, Connectors, Advanced), reconnect. |
| Postgres user password | Vault `postgres`, and the database | The init script creates the user only on the **first** start of an empty data volume, so changing Vault alone does not change the database. Run `ALTER USER "<user>" WITH PASSWORD '<new>'` in Postgres, update Vault, then restart the API. Do the same for the root password. |
| Redis password | shared `secrets-k8/redis/<env>` | Owned by the platform; restart the web deployment after it changes (sessions are rebuilt, users sign in again). |
| Owner subject | Vault `mcp` | Only changes if you move to a different identity account. Update Vault and restart the API. |

Never put a real value in this repository or in `swing-trade-helm-config`.

## 7. Logging and health

- `GET /livez` is the process only (no database), so a database outage never restarts the pod. `GET /readyz` includes the database (`Unhealthy`, 503, when unreachable) and price freshness (`Degraded`, still 200, when stale or empty). The BFF has the same two endpoints; with Redis configured its `/readyz` pings Redis. Probes are filtered out of traces.
- Authorization refusals are logged (DECISIONS #103): a 403 as a Warning with the user's `sub`, the path and the required policy; a 401 as Information with the path and policy. The Authorization header and tokens are never read by that logger. Repeated 403s from one `sub` or 401s from outside are worth a look.

## 8. Known limitations

- **No holiday calendar** (DECISIONS #47): a holiday reads stale until the next session.
- **One API replica**: the refresh lock and schedule are in-process, so a second replica would double every scheduled run (DECISIONS #48).
- **Expectancy and drawdown** are not implemented because the plan gives no formula (DECISIONS #14); the dashboard and the MCP `get_dashboard` omit them.
- **CSV export** does not neutralise cells that start with `=`, `+`, `-` or `@` (DECISIONS #58); the free-text fields are your own.
- **BFF sessions** live in Redis in the cluster; locally and without Redis they are in memory. OidcProxy.Net 1.1.x has a token-renewal lock race, so the SPA sends its API calls one at a time (DECISIONS #90).
- **Not yet verified against the real systems**: Duende token issuance for the MCP `resource` parameter, the Claude custom connector and Claude Code, the BFF against the real identity server, the Helm chart and helm-config on the cluster, and the CI pipeline (none of these were run from the build machine; the images were built and started under podman).
- **The `add_note` tool** has unit tests only for its enabled state (the Testing stack has no flag for it).
- **Wash-sale** is by ticker (the optional group field is passed through if you fill it in); it is informational and not tax advice.
- **Stale-data semantics** assume US equity sessions; a ticker that stops trading looks stale forever until deactivated.

## 9. Dependency advisories

- `Microsoft.OpenApi` 2.0.0 (GHSA-v5pm-xwqc-g5wc, NU1903) came in through `Microsoft.AspNetCore.OpenApi` 10.0.8. Resolved by moving to 10.0.12, which depends on `Microsoft.OpenApi` 2.12 (DECISIONS #105). The build has no NuGet warnings.
- `MessagePack` 2.5.192 (several advisories, NU1902/NU1903) came in through `Aspire.Hosting`, in the AppHost only (a local and test tool, not deployed). Pinned to 2.5.301 with central transitive pinning (DECISIONS #105).
- NU1507 (multiple package sources without source mapping) is fixed by the `packageSourceMapping` in `src/nuget.config` (DECISIONS #106).
- Re-check with `dotnet list package --vulnerable --include-transitive` and `yarn npm audit` periodically.
