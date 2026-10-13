# Contributing to Swing Trade

Swing Trade is a small self-hosted side project (a personal swing-trading journal). Pull requests, bug reports and ideas are welcome.

This document covers the workflow for getting a change merged. For setup and the project layout see `README.md`; for design choices see `docs/DECISIONS.md`; for the spec see `docs/BUILD_PLAN.md`.

This is a monorepo: the API (`src/api`), the web frontend (`src/web`) and the combined Helm chart (`charts/swing-trade`) build and publish together from one pipeline (`.devops/pipeline-ci.yml`). Deployment configuration is in the sibling repo `swing-trade-helm-config`.

## Ground rules

- **Be excellent to each other.** Assume good faith, give actionable feedback, and do not be rude.
- **One change per PR.** Refactors, features and unrelated cleanups land separately.
- **Tests stay green.** Do not push past a red build.
- **Never change a formula, threshold or rule on your own.** The rules are in `docs/BUILD_PLAN.md` and are tested against the workbook fixtures. If something is ambiguous, pick the documented default and add a row to `docs/DECISIONS.md`.
- **One code path to the rules.** Controllers, MCP tools and the refresh job call `Spydersoft.SwingTrade.Rules` through the shared services. The UI formats numbers; it never computes them.

## Before you start

For anything non-trivial (a new endpoint, a new screen, a schema change, a dependency bump that touches more than one file) open an issue first to sanity-check the direction. Small fixes and obvious bugs can go straight to a PR.

A few things are out of scope and will likely be closed:

- Environment-specific URLs (OIDC authority, API addresses, OTLP endpoints) hard-coded into `appsettings.json`. Production values come from environment variables (Helm); `appsettings.Development.json` is for local-only values.
- Bypassing the BFF, for example calling the API from the React app with a token held in the browser. Tokens stay server-side.
- Net-new authentication mechanisms. Access is OAuth2/OIDC with `swingtrade.*` roles; API keys, basic auth and the like are not on the roadmap. Roles are enforced by policies on every action (a test fails the build otherwise).
- Any LLM or AI call made by the app at runtime. Claude reaches the app through MCP; the app never calls Claude.
- Brokerage credentials, order placement, or a non-PostgreSQL database.
- Secrets in the repo. Keys and tokens go in user-secrets locally and Vault in the cluster.

## Development setup

Prerequisites: .NET 10 SDK, Node 24 with corepack (Yarn 4), and Docker or podman (`ASPIRE_CONTAINER_RUNTIME=podman`).

```bash
corepack enable
yarn install                                                          # repo root: UI workspace
dotnet run --project src/Spydersoft.SwingTrade.AppHost --launch-profile Local   # Postgres, mock IdP, API, BFF, Vite
```

See `docs/apphost-launch-profiles.md` for ports and the mock users.

### NuGet authentication

This repo references `Spydersoft.Platform.Hosting` from a GitHub Packages feed (`src/nuget.config`). You need a GitHub personal access token with `read:packages`; GitHub Packages requires authentication even for public packages.

## Branch workflow

- `main` represents what is deployed (or about to be). The first iteration was committed straight to `main`; use branches and PRs from here on.
- Feature work goes on `feature/<short-description>`, fixes on `fix/<short-description>`. Branch off `main` and rebase before opening the PR.
- Versions come from GitVersion (`GitVersion.yml`): `main` builds are `X.Y.Z-beta.N`, a tag makes a release `X.Y.Z`.

## Commit messages

Keep messages clean: subject line plus body only. No `Co-Authored-By` trailers for AI tools and no "Generated with" lines.

- Subject under 70 characters, imperative mood ("Add stale badge to the drawer").
- The body explains the why when it is not obvious from the diff.
- One logical change per commit.

## Pull requests

Open the PR against `main`. The pipeline restores from the GitHub Packages feed, builds `Spydersoft.SwingTrade.slnx` (which also builds the SPA), runs the unit tests (`**/*.UnitTests/*.csproj`) and the Vitest suite, runs SonarCloud analysis, and then the two Playwright suites. PR builds do not publish images or the chart; `main` publishes betas and bumps the helm-config `test` environment, and a tag publishes a release.

A good PR description states what changed and why in one to three bullets, calls out anything that needs attention (migration, API change, UX change), includes a screenshot for visual changes, and has a test plan if behaviour changed.

## Code style

| Concern | Tool |
| --- | --- |
| C# formatting | `dotnet format`, `.editorconfig` |
| TypeScript / React | ESLint (`yarn lint` in `src/web/src/swing-trade-ui`) |
| JSON / YAML / Markdown / TS formatting | Prettier (120 columns), `yarn lint-staged` at the root |
| Line endings | LF |

There are no git hooks yet (Husky was left out on purpose, DECISIONS #98): run `yarn lint-staged`, `dotnet format` and the tests yourself before you push.

React conventions: function components and hooks; one feature per folder under `src/components`; state in Redux Toolkit slices; Tailwind utilities and PrimeReact only (no third styling system); never import the raw generated client in components, wrap calls in `src/api/swingTradeApi.ts`; show every figure exactly as the API returned it.

## Tests

- **Rules** (`Spydersoft.SwingTrade.Rules.UnitTests`) hold the workbook fixtures. They must pass exactly; write them first for any rule change that the owner has approved.
- **API unit tests** (`Spydersoft.SwingTrade.Api.UnitTests`, `MarketData.UnitTests`) use NUnit 4 on Microsoft.Testing.Platform with the EF Core InMemory provider and hand-rolled fakes. `dotnet test Spydersoft.SwingTrade.slnx`.
- **UI tests**: `cd src/web/src/swing-trade-ui && yarn test` (Vitest and Testing Library).
- **End-to-end**: `src/api/tests/api-integration` and `src/web/tests/web-integration` (Playwright). Each boots the AppHost `Testing` profile with a mock IdP and WireMock, so no test touches a real provider. New endpoints land with an API spec that includes the role matrix.

New endpoints need controller and service tests; new screens with non-trivial logic need Vitest tests.

## Schema migrations

EF Core migrations live in `src/api/src/Spydersoft.SwingTrade.Data/Migrations/` and are applied at API startup. Add one with:

```bash
dotnet ef migrations add <Name> --project src/api/src/Spydersoft.SwingTrade.Data --startup-project src/api/src/Spydersoft.SwingTrade.Data --output-dir Migrations
```

Do not edit a migration after it has been merged; add a new one. Note a schema change in `docs/OPERATIONS.md` if it affects backup or restore.

## API client

The TypeScript client in `src/web/src/swing-trade-ui/src/api/generated/` is generated from the committed OpenAPI document `src/api/openapi/swing-trade.json`. After an API change run `yarn api:update` in the UI project, and commit the regenerated document and client. Do not hand-edit generated files.

## Reporting bugs

Open an issue with what you did, what you expected, what happened instead (status code, message, logs or a screenshot), and the environment (local Aspire or a cluster; browser and OS for UI issues). For security-sensitive issues, email the maintainer instead of opening a public issue.

## License

By contributing you agree your contributions are licensed under the [MIT License](LICENSE).
