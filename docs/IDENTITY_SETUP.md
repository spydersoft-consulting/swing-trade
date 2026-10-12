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
3. **Client for the web BFF (OidcProxy).** Add `swingtrade` to the client's `AllowedScopes` and request it from the BFF (the BFF's `appsettings.json` asks for `openid profile email offline_access swingtrade`). Authorization code (the BFF is a confidential client; PitStop's runs it with PKCE off, set it to match); redirect URI `https://<ui host>/oauth2/callback`, post-logout `https://<ui host>/`. The BFF forwards the access token unchanged. The UI reads the user's roles from `GET /api/v1/auth/info`, so the `role` claim only needs to be in the access token (not the id token).
4. **Role source.** The users must have claims of type `role` available to the profile service. Reuse whatever mechanism emits `authentication.admin` today (the Identity user-role store or a claim on the user). Ensure the profile service issues the `role` claim for the `swingtrade` resource's `UserClaims`.
5. **Create the roles** `swingtrade.reader`, `swingtrade.trader`, `swingtrade.admin`, `swingtrade.mcp`.
6. **Assign to the owner:** `swingtrade.reader`, `swingtrade.trader`, `swingtrade.admin`. Assign `swingtrade.mcp` to the owner only when the MCP connector is set up (M5); it is independent of the REST roles. Roles are hierarchical in the API (admin satisfies trader satisfies reader), so one assignment would suffice, but assigning all three keeps the token readable and survives a policy change.
7. **MCP client (M5).** The Claude connector's PKCE client also requests the `swingtrade` scope, so its token carries `aud: swingtrade` and the `swingtrade.mcp` role.

## Verify

Decode an access token for the owner (jwt.io offline or `jwt decode`) and confirm `aud` contains `swingtrade`, `sub` is present, and `role` contains the expected values. Then call `GET /api/v1/trades` with it (expect 200); a token with no `swingtrade.*` role must return 403, and `PUT /api/v1/settings` with only `swingtrade.trader` must return 403.

## API configuration

`Auth:Authority` is the identity configuration for REST. The audience and role names are constants in `SwingTradeAuth` and are not configurable. The MCP endpoint adds the `Mcp:*` keys listed in the MCP section below.

## MCP endpoint: OAuth clients and configuration (M5)

The API is only the resource server for `/mcp` (decision 67). Duende is the authorization server. Nothing here is applied from this repo; make the changes in the identity repo.

### What the API checks on `/mcp`

1. A bearer token in the `Authorization` header (never the query string), issued by `Auth:Authority`, with `aud` = `swingtrade` and a `sub`.
2. The `swingtrade.mcp` role in the `role` claim (policy `Mcp`).
3. `sub` equals the configured owner (`Mcp:OwnerSubject`). Another user with the role is refused with 403. With no owner configured the endpoint refuses everyone.

An unauthenticated request gets `401` with `WWW-Authenticate: Bearer resource_metadata="https://<host>/.well-known/oauth-protected-resource"`. That document (RFC 9728, also at `/.well-known/oauth-protected-resource/mcp`) names `resource` = the MCP URL exactly as configured in `Mcp:PublicUrl`, `authorization_servers` = [`Auth:Authority`], `scopes_supported` = [`swingtrade`] and `bearer_methods_supported` = [`header`]. Claude follows it to Duende's discovery document (`/.well-known/openid-configuration` or the OAuth authorization-server metadata).

### API configuration (Helm values or environment)

| Key | Value |
| --- | --- |
| `Mcp__PublicUrl` | `https://<public host>/mcp` (absolute, https; becomes the metadata `resource`) |
| `Mcp__OwnerSubject` | the owner's Duende `sub` (find it in the access token or the identity database) |
| `Mcp__AllowedHosts__0` | optional; the Host header the gateway forwards. Default: the host of `Mcp__PublicUrl` |
| `Mcp__EnableAddNote` | `false` (default). `true` registers `add_note`, which then also needs `swingtrade.trader` |
| `Mcp__RequestsPerMinute` | optional, default 120 for `/mcp` and the metadata routes together |
| `Auth__Authority` | the issuer, exactly as in Duende's discovery document, no trailing slash |

Outside Development and Testing the API refuses to start without a valid https `Mcp__PublicUrl` and an `Mcp__OwnerSubject`.

### Client 1: the Claude connector (confidential, authorization code + PKCE)

Used by claude.ai, Claude Desktop and mobile. Anthropic's servers call back to a fixed URL.

```csharp
new Client
{
    ClientId = "swingtrade-claude-connector",
    ClientName = "Claude custom connector (Swing Trade MCP)",
    ClientSecrets = { new Secret("<generated secret>".Sha256()) },   // keep the plain value in a password manager only
    AllowedGrantTypes = GrantTypes.Code,
    RequirePkce = true,                // S256 only
    AllowPlainTextPkce = false,
    RequireClientSecret = true,
    RedirectUris = { "https://claude.ai/api/mcp/auth_callback" },
    AllowedScopes = { "swingtrade", "offline_access" },
    AllowOfflineAccess = true,         // refresh tokens, so the connector stays connected
    AccessTokenLifetime = 3600,
    RequireConsent = false
}
```

### Client 2: Claude Code (public, authorization code + PKCE, no secret)

A separate client so a leaked CLI configuration never exposes the connector's secret (decision 70). It is a native app, so it uses a loopback redirect on a port the CLI picks.

```csharp
new Client
{
    ClientId = "swingtrade-claude-code",
    ClientName = "Claude Code (Swing Trade MCP)",
    RequireClientSecret = false,       // public client
    AllowedGrantTypes = GrantTypes.Code,
    RequirePkce = true,
    AllowPlainTextPkce = false,
    RedirectUris = { "http://127.0.0.1", "http://localhost" },   // port-agnostic loopback, RFC 8252 section 7.3
    AllowedScopes = { "swingtrade", "offline_access" },
    AllowOfflineAccess = true,
    AccessTokenLifetime = 3600,
    RequireConsent = false
}
```

Duende matches redirect URIs exactly, so a loopback port that changes on every login needs one of: (a) a small `IRedirectUriValidator` that, for `127.0.0.1`, `localhost` and `[::1]` only, ignores the port; or (b) a fixed port. Claude Code accepts `--callback-port`, so you can register the exact URI (for example `http://localhost:8765/callback`) and skip the validator. Verify which one your Duende version needs; it was not testable from this repo.

### API resource and scope

This is the existing one from the section above and needs no change: scope `swingtrade`, `ApiResource` `swingtrade` with `UserClaims = { "sub", "role" }`, so the access token has `aud: swingtrade` and the `role` claim. Both MCP clients above request the `swingtrade` scope.

Resource indicators (RFC 8707): MCP clients send `resource=<MCP URL>` on the authorization and token requests. Duende treats `resource` as the name of an `ApiResource`, so a request for the MCP URL can fail with `invalid_target` while the only resource is named `swingtrade`. This is the main thing to check when the connector is first added. If it fails, either relax Duende's resource-indicator handling for these two clients (a custom `IResourceValidator` that maps the MCP URL to the `swingtrade` resource), or register an `ApiResource` whose name is the MCP URL and accept that value as a second audience in the API. The second option changes the audience contract, so it is not done here (decision 76).

### Role assignment

Assign `swingtrade.mcp` to the owner. It is independent of the REST roles: it does not give REST access, and the REST roles do not give MCP access. To let Claude call `add_note` the owner also needs `swingtrade.trader` and `Mcp__EnableAddNote=true`.

### Add the custom connector in Claude

1. claude.ai (or Desktop) > Settings > Connectors > Add custom connector.
2. URL: the value of `Mcp__PublicUrl`, for example `https://swing.mattgerega.net/mcp`.
3. Open Advanced settings and enter OAuth Client ID `swingtrade-claude-connector` and the client secret.
4. Connect, sign in at the identity server as the owner, approve. Ask: "What are my pullback candidates and how many shares for each?" (use the `weekly_review` prompt, or read `journal://rules` first).
5. Claude Code: `claude mcp add --transport http --client-id swingtrade-claude-code swingtrade https://swing.mattgerega.net/mcp` (add `--callback-port <port>` if you registered a fixed redirect), then `/mcp` in a session to authenticate.

### Public route in Helm (decision 68)

The UI and REST API stay on the private route. Add a second route on the same hostname, attached to the public gateway, that matches only these paths. Nothing else is reachable from the internet. This repo does not edit the Helm repo; the snippet is for it:

```yaml
apiVersion: gateway.networking.k8s.io/v1
kind: HTTPRoute
metadata:
  name: swing-trade-api-mcp
spec:
  parentRefs:
    - name: <public-gateway>            # the internet-facing gateway, not the private one
      namespace: <gateway-namespace>
  hostnames:
    - swing.mattgerega.net              # same hostname as the UI
  rules:
    - matches:
        - path: { type: PathPrefix, value: /mcp }
        - path: { type: PathPrefix, value: /.well-known/oauth-protected-resource }
      backendRefs:
        - name: swing-trade-api         # the API service, not the BFF
          port: 8080
```

Optionally restrict the public gateway or a policy attached to this route to Anthropic's outbound range `160.79.104.0/21` (claude.ai, Desktop and mobile) plus your own address for Claude Code. The API also enforces a Host allowlist, a rate limit and the owner check, and sends no CORS headers, so the allowlist is defence in depth, not the only control.
