# Review findings (security and correctness)

Scope: authentication and authorization, the /mcp pipeline, MCP audit logging, market-data ingestion, the Rules library against `docs/BUILD_PLAN.md`, input validation, and the frontend. Decisions already in `docs/DECISIONS.md` were not re-flagged. Reviewed by reading the code; nothing was run against a real identity server, cluster or gateway.

## Fixed

| Severity | Finding | Fix |
| --- | --- | --- |
| Medium | Notes, instruments, trades and settings accepted text longer than the column (4000/200/100/50/16) and numbers larger than the column's precision. The database refused them and the caller got a bare 500 instead of a 400. | Length and range checks in the services (`ValidationErrors.MaxLength`/`MaxAmount`); tests added. `add_note` benefits too, since it calls the same service. |
| Low | The MCP call log stored the tool or prompt name verbatim. A client could put a token-shaped string in a tool name and have it stored. | Names now pass through `McpArgumentSanitizer.CleanText`; test added. |

## Not fixed

| Severity | Finding | Why |
| --- | --- | --- |
| Low | CSV export does not neutralise cells starting with `=`, `+`, `-`, `@` (Rationale and Lessons are free text). | Documented as accepted in DECISIONS #58 (owner's own data, single user). Changing it alters exported values; a leading `'` on the two text columns is the usual fix if the owner wants it. |
| Low | The stored `McpCallLog.Arguments` is cut at 1000 characters, which can leave invalid JSON. | Cosmetic; it is display-only. |
| Low | The BFF sets no security headers (CSP, X-Content-Type-Options, frame options). The SPA has no `dangerouslySetInnerHTML`, `eval` or token handling, so exposure is small. | Needs a policy decision on the CSP (inline styles). |
| Info | The MCP rate limiter is one global fixed window, not per caller. | Documented (DECISIONS #78). |
| Unverified | Cookie `SameSite` and CSRF posture of the OidcProxy session cookie for POST/PUT/DELETE through the BFF. | Library behaviour; not visible in this repo. Check in the first deploy. |
| Unverified | `aud: swingtrade` is issued by the real identity server for the MCP resource; the Claude connector accepts the pre-registered client. | Already listed as open (DECISIONS #76, owner items). |

## Checked and found correct

- Every controller action carries a known policy (a reflection test enforces it). Policies require the `swingtrade` audience, `sub` and a role; the issuer is validated through `Authority`; `MapInboundClaims=false`; the JWT handler reads only the Authorization header, so a query-string token is ignored.
- `/mcp` requires `McpOwner`, answers 401 with `resource_metadata`, 421 on a bad Host, and is rate limited before authentication. `add_note` is off by default and needs the Write policy on top of the owner check.
- The BFF proxies only `/api/v1/{**catch-all}` with `AllowAnonymous=false`; `/config.js` exposes only `api_url`. The SPA keeps no token (only the userinfo claims in localStorage).
- Provider keys travel in headers, never in URLs or log messages. Bars upsert on (Ticker, Date); incomplete bars are dropped through `BarCompletion`.
- Rules library: indicators, trend, signal order, sizer shares/verdict order, trade math, wash-sale window and re-entry date, and the dashboard figures all match the plan's formulas. No mismatch found; no formula or threshold was touched.
