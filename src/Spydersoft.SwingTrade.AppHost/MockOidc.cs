/// <summary>
/// Configuration for the soluto/oidc-server-mock container. One test user per persona, each carrying a single
/// <c>role</c> claim (roles are hierarchical in the API's policies, not in the assignments). The same names and
/// credentials are used by <c>src/api/tests/api-integration/tests/auth.ts</c>.
/// </summary>
internal static class MockOidc
{
    public const string ApiClientId = "swingtrade-api-test";
    public const string ApiClientSecret = "swingtrade-api-test-secret";

    /// <summary>The subject of the "mcponly" user: the one identity the Testing API lets through /mcp (Mcp:OwnerSubject).</summary>
    public const string McpOwnerSubject = "mcponly-1";

    // Issues one-second access tokens so a spec can wait for a genuinely expired token (the Testing API runs with no clock skew).
    public const string ShortLivedClientId = "swingtrade-api-test-short";

    /// <summary>The confidential authorization-code client the web BFF (OidcProxy) signs in with. Allowed to request the swingtrade scope, so the access token it holds carries aud and the role claim.</summary>
    public const string WebClientId = "swingtrade-web-test";
    public const string WebClientSecret = "swingtrade-web-test-secret";

    public static string ClientsJson() => $$"""
    [
      {
        "ClientId": "{{ApiClientId}}",
        "ClientSecrets": ["{{ApiClientSecret}}"],
        "AllowedGrantTypes": ["password"],
        "AllowedScopes": ["openid", "profile", "swingtrade", "other-api"],
        "RequireClientSecret": true
      },
      {
        "ClientId": "{{ShortLivedClientId}}",
        "ClientSecrets": ["{{ApiClientSecret}}"],
        "AllowedGrantTypes": ["password"],
        "AllowedScopes": ["openid", "profile", "swingtrade"],
        "AccessTokenLifetime": 1,
        "RequireClientSecret": true
      },
      {
        "ClientId": "{{WebClientId}}",
        "ClientSecrets": ["{{WebClientSecret}}"],
        "AllowedGrantTypes": ["authorization_code"],
        "AllowedScopes": ["openid", "profile", "email", "offline_access", "swingtrade"],
        "RedirectUris": ["http://localhost:8190/oauth2/callback", "http://localhost:5290/oauth2/callback"],
        "PostLogoutRedirectUris": ["http://localhost:8190/", "http://localhost:5290/"],
        "RequireConsent": false,
        "RequirePkce": false,
        "AllowOfflineAccess": true
      }
    ]
    """;

    public static string ServerOptionsJson() => """
    {
      "Authentication": {
        "CookieSameSiteMode": "Lax",
        "CheckSessionCookieSameSiteMode": "Lax"
      }
    }
    """;

    public static string UsersJson() => """
    [
      { "SubjectId": "norole-1",  "Username": "norole",  "Password": "Test123!", "Claims": [ { "Type": "name", "Value": "No Role" } ] },
      { "SubjectId": "reader-1",  "Username": "reader",  "Password": "Test123!", "Claims": [ { "Type": "name", "Value": "Reader" },  { "Type": "role", "Value": "swingtrade.reader" } ] },
      { "SubjectId": "trader-1",  "Username": "trader",  "Password": "Test123!", "Claims": [ { "Type": "name", "Value": "Trader" },  { "Type": "role", "Value": "swingtrade.trader" } ] },
      { "SubjectId": "admin-1",   "Username": "admin",   "Password": "Test123!", "Claims": [ { "Type": "name", "Value": "Admin" },   { "Type": "role", "Value": "swingtrade.admin" } ] },
      { "SubjectId": "mcponly-1", "Username": "mcponly", "Password": "Test123!", "Claims": [ { "Type": "name", "Value": "MCP only" }, { "Type": "role", "Value": "swingtrade.mcp" } ] },
      { "SubjectId": "mcpother-1", "Username": "mcpother", "Password": "Test123!", "Claims": [ { "Type": "name", "Value": "MCP other" }, { "Type": "role", "Value": "swingtrade.mcp" } ] }
    ]
    """;

    // "swingtrade" is the audience the API requires; "other-api" mints a validly signed token for the wrong audience.
    public static string ApiScopesJson() => """
    [
      { "Name": "swingtrade", "DisplayName": "Swing Trade API", "UserClaims": ["role"] },
      { "Name": "other-api", "DisplayName": "Some other API", "UserClaims": ["role"] }
    ]
    """;

    public static string ApiResourcesJson() => """
    [
      { "Name": "swingtrade", "DisplayName": "Swing Trade API", "Scopes": ["swingtrade"], "UserClaims": ["role"] },
      { "Name": "other-api", "DisplayName": "Some other API", "Scopes": ["other-api"], "UserClaims": ["role"] }
    ]
    """;
}
