# Swing Trade web

The React app (`src/swing-trade-ui`) and the ASP.NET BFF that serves it (`src/Spydersoft.SwingTrade.Frontend`).

```
browser --> Frontend BFF (OidcProxy.Net) --/api/v1--> Swing Trade API
               |                                         (validates the token, enforces roles)
               +-- identity provider (OIDC, the BFF holds the tokens)
```

- The browser never sees a token. The BFF signs the user in, keeps the session, serves the built SPA from `wwwroot`, and forwards `/api/v1/*` to the API with the user's access token. `/mcp` and `/.well-known` are not proxied.
- The SPA reads the user's roles from `GET /api/v1/auth/info` and hides actions they cannot perform. The API is the only enforcement point. A user with no `swingtrade.*` role sees an "Access not granted" page.
- Every number on screen comes from the API; the UI only formats (money to cents, percent to one decimal).

## Run it

Prerequisites: .NET 10 SDK, Node 24 with corepack (Yarn 4), a container runtime (Docker, or `ASPIRE_CONTAINER_RUNTIME=podman`).

```bash
corepack enable
yarn install                      # at the repo root: installs the swing-trade-ui workspace
dotnet run --project src/Spydersoft.SwingTrade.AppHost --launch-profile Local
```

Open http://localhost:8190 (the BFF serving the built SPA) or http://localhost:5290 (Vite with hot reload). Sign in with the mock users in `docs/apphost-launch-profiles.md` (`reader`, `trader`, `admin`, `norole`; password `Test123!`). `-p:SkipSpaBuild=true` skips the SPA build in the BFF project.

## UI commands (in `src/swing-trade-ui`)

| Command | |
| --- | --- |
| `yarn dev` | Vite dev server (proxies `/api`, `/oauth2` to the BFF on 8190; `BFF_URL` overrides) |
| `yarn build` | `tsc` and Vite build to `dist/` |
| `yarn lint` / `yarn lint:fix` | ESLint and Prettier |
| `yarn test` / `yarn test-ci` | Vitest (`test-ci` writes junit and coverage to `output/`) |
| `yarn api:generate` | Regenerate `src/api/generated` from `src/api/openapi/swing-trade.json` |
| `yarn api:update` | Rebuild the spec from the API project, then regenerate |

## Browser tests

`tests/web-integration` is a Playwright project (its own Yarn project) that boots the AppHost `Testing` profile and drives Chromium through the mock sign-in.

```bash
cd src/web/tests/web-integration
corepack enable && yarn install --immutable
npx playwright install chromium
# podman instead of Docker: export ASPIRE_CONTAINER_RUNTIME=podman
yarn test
```

Remove leftover containers (`podman ps -a`) from a killed run before starting again; the ports are fixed.
