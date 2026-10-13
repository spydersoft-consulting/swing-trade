# Identity server setup for role-based authorization

The Swing Trade API authorizes by role (see "Authorization and roles" in `docs/BUILD_PLAN.md`). The identity server (`auth.mattgerega.net` for test and `auth.mattgerega.com` for production, Duende IdentityServer, repo `d:\spydersoft\identity`) must be changed to issue the right access token. This repo does not edit the identity repo; apply the changes there. The clients, the `swingtrade` scope and the roles are created in the identity admin UI only (DECISIONS #120). The single code change is the loopback redirect validator in "Client 2" below.

## What the API expects in the access token

| Claim | Value |
| --- | --- |
| `iss` | `https://auth.<environment_hostname>` (config `Auth:Authority`): `auth.mattgerega.net` test, `auth.mattgerega.com` production (DECISIONS #116) |
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
| `Mcp__PublicUrl` | `https://swingtrade-mcp.<environment_hostname>/mcp` (absolute, https, the dedicated MCP host; becomes the metadata `resource` verbatim) |
| `Mcp__OwnerSubject` | the owner's Duende `sub` (find it in the access token or the identity database) |
| `Mcp__AllowedHosts__0` | the MCP host (`swingtrade-mcp.<environment_hostname>`); the chart config sets it explicitly. Default if unset: the host of `Mcp__PublicUrl` |
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

### Client 2: Claude Code (public, authorization code + PKCE, no secret, port-agnostic loopback)

A separate client so a leaked CLI configuration never exposes the connector's secret (decision 70). It is a native app, so it receives the code on a loopback address with a port the CLI picks at random on every login (RFC 8252 section 7.3). Decision 118: register it port-less and add a custom redirect validator in the identity server. Create the client in the admin UI (decision 120):

```csharp
new Client
{
    ClientId = "swingtrade-claude-code",
    ClientName = "Claude Code (Swing Trade MCP)",
    RequireClientSecret = false,       // public client
    AllowedGrantTypes = GrantTypes.Code,
    RequirePkce = true,
    AllowPlainTextPkce = false,
    RedirectUris = { "http://127.0.0.1", "http://localhost" },   // no port
    AllowedScopes = { "swingtrade", "offline_access" },
    AllowOfflineAccess = true,
    AccessTokenLifetime = 3600,
    RequireConsent = false
}
```

Duende matches redirect URIs exactly, so without the validator below every login fails with `invalid_redirect_uri` because the port never matches. **The identity server owner must deploy this change before Claude Code login works.** The connector client (`swingtrade-claude-connector`) uses the exact `https://claude.ai/api/mcp/auth_callback` and is unaffected.

#### The validator (identity repo; read-only here, apply it there)

Location: `identity_server/source/Spydersoft.Identity/Services/LoopbackRedirectUriValidator.cs`, namespace `Spydersoft.Identity.Services` (next to `EmailSender.cs`). It extends Duende's `StrictRedirectUriValidator` (Duende.IdentityServer 8.0.4), so every other client keeps the exact-match behaviour. Duende's built-in `StrictRedirectUriValidatorAppAuth` is not used because it applies to all clients and only handles `127.0.0.1`, not `localhost`.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Duende.IdentityServer.Configuration;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Validation;

namespace Spydersoft.Identity.Services
{
    /// <summary>
    /// Accepts loopback redirect URIs on any port (RFC 8252 section 7.3) for the native public clients listed in
    /// <see cref="LoopbackClientIds"/>. Every other client, and every non-loopback URI, falls through to the strict exact
    /// match of <see cref="StrictRedirectUriValidator"/>.
    /// </summary>
    /// <seealso cref="StrictRedirectUriValidator" />
    public class LoopbackRedirectUriValidator(IdentityServerOptions options) : StrictRedirectUriValidator(options)
    {
        /// <summary>The clients that may use a port-agnostic loopback redirect.</summary>
        public static readonly IReadOnlySet<string> LoopbackClientIds =
            new HashSet<string>(StringComparer.Ordinal) { "swingtrade-claude-code" };

        /// <summary>The loopback hosts allowed by RFC 8252 (IPv4, name and IPv6 literal).</summary>
        private static readonly string[] LoopbackHosts = ["127.0.0.1", "localhost", "[::1]"];

        /// <inheritdoc />
        public override async Task<bool> IsRedirectUriValidAsync(RedirectUriValidationContext context, CancellationToken ct)
        {
            if (await base.IsRedirectUriValidAsync(context, ct))
            {
                return true;
            }

            return IsLoopbackMatch(context.RequestedUri, context.Client);
        }

        /// <summary>
        /// True when <paramref name="requestedUri"/> is an http loopback URI whose scheme, host and path equal a registered
        /// port-less URI; only the port may differ.
        /// </summary>
        /// <param name="requestedUri">The redirect_uri of the authorize or token request.</param>
        /// <param name="client">The client making the request.</param>
        /// <returns><c>true</c> if the loopback redirect is acceptable.</returns>
        public static bool IsLoopbackMatch(string? requestedUri, Client client)
        {
            if (client is null || !LoopbackClientIds.Contains(client.ClientId))
            {
                return false;
            }

            if (!TryGetLoopback(requestedUri, out Uri? requested) || requested is null)
            {
                return false;
            }

            return client.RedirectUris.Any(registered =>
                TryGetLoopback(registered, out Uri? reg)
                && reg is not null
                && reg.IsDefaultPort
                && string.Equals(reg.Host, requested.Host, StringComparison.OrdinalIgnoreCase)
                && (reg.AbsolutePath == "/" || string.Equals(reg.AbsolutePath, requested.AbsolutePath, StringComparison.Ordinal)));
        }

        private static bool TryGetLoopback(string? value, out Uri? uri)
        {
            uri = null;
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed)
                || parsed.Scheme != Uri.UriSchemeHttp
                || !string.IsNullOrEmpty(parsed.UserInfo)
                || !string.IsNullOrEmpty(parsed.Query)
                || !string.IsNullOrEmpty(parsed.Fragment)
                || !LoopbackHosts.Contains(parsed.Host, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            uri = parsed;
            return true;
        }
    }
}
```

A registered path of `/` (what `http://127.0.0.1` parses to) accepts any path on that host, for example Claude Code's `/callback`; a registered path other than `/` must match exactly. The scheme must be `http`, so a remote host, `https`, userinfo, a query or a fragment never matches.

Register it in `identity_server/source/Spydersoft.Identity/Program.cs`, in the `builder.Services.AddIdentityServer(...)` chain that starts around line 151, after the `AddOperationalStore(...)` call (the chain ends at the closing `});` of its options lambda). The exact line to add:

```csharp
    .AddRedirectUriValidator<LoopbackRedirectUriValidator>();
```

so the end of the chain reads `... options.TokenCleanupInterval = 30;` `})` then `.AddRedirectUriValidator<LoopbackRedirectUriValidator>();`. `Program.cs` already has `using Spydersoft.Identity.Services;`. Only the `;` moves from the `AddOperationalStore` call to the new line.

Unit-test sketch, in `identity_server/source/Spydersoft.Identity.Tests/LoopbackRedirectUriValidatorTests.cs` (NUnit, as `OptionsTests.cs`; the test project already references the identity project, which brings Duende in). Check that your Duende version lets you set `RedirectUriValidationContext.RequestedUri` and `.Client`; the static `IsLoopbackMatch` can be tested without the context:

```csharp
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Validation;

using Spydersoft.Identity.Services;

namespace Spydersoft.identity.tests
{
    public class LoopbackRedirectUriValidatorTests
    {
        private static Client CodeClient(string id = "swingtrade-claude-code") => new()
        {
            ClientId = id,
            RedirectUris = { "http://127.0.0.1", "http://localhost" },
        };

        [TestCase("http://127.0.0.1:54321/callback")]
        [TestCase("http://127.0.0.1:1/callback")]
        [TestCase("http://localhost:61234/callback")]
        [TestCase("http://localhost/callback")]
        public void LoopbackOnAnyPort_IsAccepted(string uri) =>
            Assert.That(LoopbackRedirectUriValidator.IsLoopbackMatch(uri, CodeClient()), Is.True);

        [TestCase("https://127.0.0.1:54321/callback")]          // scheme
        [TestCase("http://evil.example:54321/callback")]        // host
        [TestCase("http://127.0.0.1.evil.example/callback")]    // host suffix
        [TestCase("http://user@127.0.0.1:54321/callback")]      // userinfo
        [TestCase("http://127.0.0.1:54321/callback?x=1")]       // query
        [TestCase("http://[::1]:54321/callback")]               // not registered
        [TestCase("not a uri")]
        [TestCase("")]
        public void NonLoopbackOrUnregistered_IsRejected(string uri) =>
            Assert.That(LoopbackRedirectUriValidator.IsLoopbackMatch(uri, CodeClient()), Is.False);

        [Test]
        public void OtherClients_StillNeedAnExactMatch() =>
            Assert.That(LoopbackRedirectUriValidator.IsLoopbackMatch("http://127.0.0.1:54321/callback", CodeClient("swingtrade-web")), Is.False);

        [Test]
        public async Task ExactRegisteredUri_StillPassesTheStrictBase()
        {
            var validator = new LoopbackRedirectUriValidator(new Duende.IdentityServer.Configuration.IdentityServerOptions());
            var client = CodeClient();
            var ok = await validator.IsRedirectUriValidAsync(
                new RedirectUriValidationContext { RequestedUri = "http://127.0.0.1", Client = client }, CancellationToken.None);
            Assert.That(ok, Is.True);
        }
    }
}
```

### API resource and scope

This is the existing one from the section above and needs no change: scope `swingtrade`, `ApiResource` `swingtrade` with `UserClaims = { "sub", "role" }`, so the access token has `aud: swingtrade` and the `role` claim. Both MCP clients above request the `swingtrade` scope.

Resource indicators (RFC 8707): MCP clients send `resource=<MCP URL>` on the authorization and token requests. Duende treats `resource` as the name of an `ApiResource`, so a request for the MCP URL can fail with `invalid_target` while the only resource is named `swingtrade`. This is the main thing to check when the connector is first added. If it fails, either relax Duende's resource-indicator handling for these two clients (a custom `IResourceValidator` that maps the MCP URL to the `swingtrade` resource), or register an `ApiResource` whose name is the MCP URL and accept that value as a second audience in the API. The second option changes the audience contract, so it is not done here (decision 76).

### Role assignment

Assign `swingtrade.mcp` to the owner. It is independent of the REST roles: it does not give REST access, and the REST roles do not give MCP access. To let Claude call `add_note` the owner also needs `swingtrade.trader` and `Mcp__EnableAddNote=true`.

### Add the custom connector in Claude

1. claude.ai (or Desktop) > Settings > Connectors > Add custom connector.
2. URL: the value of `Mcp__PublicUrl`, the dedicated MCP host: `https://swingtrade-mcp.mattgerega.com/mcp` (production) or `https://swingtrade-mcp.mattgerega.net/mcp` (test).
3. Open Advanced settings and enter OAuth Client ID `swingtrade-claude-connector` and the client secret.
4. Connect, sign in at the identity server as the owner, approve. Ask: "What are my pullback candidates and how many shares for each?" (use the `weekly_review` prompt, or read `journal://rules` first).
5. Claude Code: `claude mcp add --transport http --client-id swingtrade-claude-code swingtrade https://swingtrade-mcp.mattgerega.com/mcp`, then `/mcp` in a session to authenticate. No `--callback-port` is needed: the port is chosen per login and the validator above accepts any port. This works only after the identity server owner has deployed the `LoopbackRedirectUriValidator`.

### Public route in Helm (decision 115, supersedes 68)

The MCP endpoint has its own hostname, `swingtrade-mcp.<environment_hostname>`, on the existing `main` Gateway in `envoy-gateway-system` (no new gateway). The UI hostname `swingtrade.<environment_hostname>` keeps only the UI/BFF route, with no MCP path, and the BFF does not proxy `/mcp`; login plus roles protect the UI. This is implemented in `charts/swing-trade` (route `api-mcp`), with hostnames set in `swing-trade-helm-config`. The route exposes only `/mcp` and `/.well-known/oauth-protected-resource` (the prefix also covers `/.well-known/oauth-protected-resource/mcp`). The plain snippet is kept for reference:

```yaml
apiVersion: gateway.networking.k8s.io/v1
kind: HTTPRoute
metadata:
  name: swing-trade-app-api-mcp
spec:
  parentRefs:
    - name: main
      namespace: envoy-gateway-system
  hostnames:
    - swingtrade-mcp.mattgerega.com          # dedicated MCP host (the UI is swingtrade.mattgerega.com)
  rules:
    - matches:
        - path: { type: PathPrefix, value: /mcp }
        - path: { type: PathPrefix, value: /.well-known/oauth-protected-resource }
      backendRefs:
        - name: swing-trade-app-api          # the API service, not the BFF
          port: 80
```

`Mcp__PublicUrl` must equal `https://swingtrade-mcp.<environment_hostname>/mcp` exactly (it becomes the metadata `resource`) and `Mcp__AllowedHosts__0` must be the MCP host. DNS and a gateway TLS listener are needed for both hostnames in each environment. Public reachability of `mattgerega.net` is unconfirmed; any `*.mattgerega.com` host is reachable, so check `swingtrade-mcp.mattgerega.net` from the internet.

Optional source allowlist (decision 119): the chart's `mcpAllowlist` is off by default. After the connector works, enable it with Anthropic's outbound range `160.79.104.0/21` (claude.ai, Desktop and mobile) plus your own `/32` for Claude Code. The API also enforces a Host allowlist, a rate limit and the owner check, and sends no CORS headers, so the allowlist is defence in depth, not the only control.
