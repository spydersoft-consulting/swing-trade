# Decisions

Where `docs/BUILD_PLAN.md` and the reference app (PitStop) disagree on tooling, the reference app wins. Status: **Proposed** until the owner approves the M0/M1 plan.

| # | Topic | Plan said | Decision (proposed) | Why |
| --- | --- | --- | --- | --- |
| 1 | Test framework | xUnit | NUnit 4 on Microsoft.Testing.Platform | Reference app; CI template uses `testRunner: mtp` |
| 2 | Frontend data/state | TanStack Query | Redux Toolkit slices + generated hey-api client | Reference app |
| 3 | UI library / charts | Recharts | PrimeReact + Tailwind 4; Chart.js for charts | Reference app ("matching the reference app's UI library") |
| 4 | Local orchestration | Docker Compose | .NET Aspire AppHost locally; Helm in-cluster; no compose file | Reference app has no compose |
| 5 | Auth (web/API) | single user | OidcProxy BFF + JWT bearer at `auth.mattgerega.net`, policies Read/Write; single owner (no per-row OwnerId) | Reference app, but one user |
| 6 | Package manager / Node | n/a | Yarn 4, Node 24 | Reference app |
| 7 | Solution format / layout | `Journal.*` projects | `Spydersoft.SwingTrade.*` names; `.slnx`; `Rules`, `Data`, `MarketData`, `Contracts`, `Api`, `UnitTests`, `AppHost`, `Frontend`, `swing-trade-ui` | Reference app naming; plan's project split preserved |
| 8 | API route prefix | `/api/...` | `/api/v1/...` | Reference app convention; MCP stays at `/mcp` |
| 9 | Controllers vs services | thin API over services | Controllers and MCP tools both call shared application services, which call the Rules library | Plan rule: one code path to the rules |
| 10 | Database | same as reference | PostgreSQL via Aspire Npgsql, migrations applied at startup | Reference app |

## Open items needing the owner (M0)

- Market-data provider: not yet evaluated against current terms and free-tier limits (needs live web check). Candidates: Alpaca, Tiingo, Alpha Vantage, Stooq.
- MCP auth: the existing IdP at `auth.mattgerega.net` is the natural authorization server. Need to confirm it supports PKCE S256, Dynamic Client Registration or Client ID Metadata Documents, and the redirect `https://claude.ai/api/mcp/auth_callback`. Otherwise static bearer token fallback.
- Public hosting address for `/mcp` (separate HTTPRoute on the API; the BFF-only exposure of the REST API stays private).
- Namespace / repo names (`swing-trade`, `swing-trade-helm-config`) and SonarCloud project key.
