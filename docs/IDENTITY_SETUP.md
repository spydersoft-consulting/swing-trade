# Identity server setup for role-based authorization

The Swing Trade API authorizes by role (see "Authorization and roles" in `docs/BUILD_PLAN.md`). The identity server (`auth.mattgerega.net`, Duende IdentityServer, repo `d:\spydersoft\identity`) must be changed to issue the right access token. This repo does not edit the identity repo; apply the changes there.

## What the API expects in the access token

| Claim | Value |
| --- | --- |
| `iss` | `https://auth.mattgerega.net` (config `Auth:Authority`) |
| `aud` | `swingtrade` (constant `SwingTradeAuth.Audience`; comes from the API resource name) |
| `sub` | the user's subject id (required) |
| `role` | one string or an array: `swingtrade.reader`, `swingtrade.trader`, `swingtrade.admin`, and for MCP `swingtrade.mcp` |

The API sets `MapInboundClaims = false` and `RoleClaimType = "role"`. A token with no matching `swingtrade.*` role gets 403.

## Changes needed in the identity repo

1. **API scope.** Add a scope `swingtrade` (display name "Swing Trade API"). It does not need to carry identity claims itself.
2. **API resource.** Add an `ApiResource` named `swingtrade` with `Scopes = { "swingtrade" }` and `UserClaims = { "sub", "role" }`. Naming it `swingtrade` makes Duende put `aud: swingtrade` in the access token when the scope is requested. `UserClaims` includes `role` so the role claim is emitted into the access token (not only the id token).
3. **Client for the web BFF (OidcProxy).** Add `swingtrade` to the client's `AllowedScopes` and request it from the BFF (`scope: openid profile swingtrade`). Authorization code with PKCE; the BFF forwards the access token unchanged.
4. **Role source.** The users must have claims of type `role` available to the profile service. Reuse whatever mechanism emits `authentication.admin` today (the Identity user-role store or a claim on the user). Ensure the profile service issues the `role` claim for the `swingtrade` resource's `UserClaims`.
5. **Create the roles** `swingtrade.reader`, `swingtrade.trader`, `swingtrade.admin`, `swingtrade.mcp`.
6. **Assign to the owner:** `swingtrade.reader`, `swingtrade.trader`, `swingtrade.admin`. Assign `swingtrade.mcp` to the owner only when the MCP connector is set up (M5); it is independent of the REST roles. Roles are hierarchical in the API (admin satisfies trader satisfies reader), so one assignment would suffice, but assigning all three keeps the token readable and survives a policy change.
7. **MCP client (M5).** The Claude connector's PKCE client also requests the `swingtrade` scope, so its token carries `aud: swingtrade` and the `swingtrade.mcp` role.

## Verify

Decode an access token for the owner (jwt.io offline or `jwt decode`) and confirm `aud` contains `swingtrade`, `sub` is present, and `role` contains the expected values. Then call `GET /api/v1/trades` with it (expect 200); a token with no `swingtrade.*` role must return 403, and `PUT /api/v1/settings` with only `swingtrade.trader` must return 403.

## API configuration

Only `Auth:Authority` is configuration. The audience and role names are constants in `SwingTradeAuth` and are not configurable.
