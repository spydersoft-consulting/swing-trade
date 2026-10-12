using Microsoft.Extensions.DependencyInjection;

const string TestingEnvironmentName = "Testing";

// Fixed host ports. They sit in the 81xx-84xx block so this AppHost can run next to PitStop's (8000-9081).
const int PostgresPort = 8110;
const int ApiPort = 8180;
const int MockOidcPort = 8210;
const int WireMockPort = 8310;
const int WebPort = 8190;
const int VitePort = 5290;

var builder = DistributedApplication.CreateBuilder(args);
var isTesting = builder.Environment.EnvironmentName == TestingEnvironmentName;

// --- Postgres ---

// A pinned password instead of Aspire's random-per-run parameter: the data volume below persists across runs, but
// Postgres sets its superuser password only once, when the volume is first initialised. A password that drifts
// between runs locks you out of that volume's data. Override with Postgres:Password (user secrets) before first run.
var postgresPassword = builder.AddParameter(
    "postgres-password",
    value: builder.Configuration["Postgres:Password"] ?? "swingtrade-local-dev-password",
    secret: true);

var postgres = builder.AddPostgres("postgres", password: postgresPassword, port: PostgresPort);

if (!isTesting)
{
    postgres.WithDataVolume();
}

// The connection name matches what the API reads: ConnectionStrings:swingtrade-db.
var db = postgres.AddDatabase("swingtrade-db");

// --- Identity: the mock OIDC server (Testing always; Local when UseMockOidc is true) ---

var useMockOidc = isTesting
    || bool.TryParse(builder.Configuration["UseMockOidc"], out var useMockOidcConfig) && useMockOidcConfig;

IResourceBuilder<ContainerResource>? mockOidc = null;
if (useMockOidc)
{
    mockOidc = builder.AddContainer("mock-oidc", "ghcr.io/soluto/oidc-server-mock", "latest")
        .WithHttpEndpoint(port: MockOidcPort, targetPort: 80, name: "http", isProxied: false)
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
        // Binds the mock IdP container's own internal listener; reachable only from localhost, never a deployed endpoint.
        .WithEnvironment("ASPNETCORE_URLS", "http://+:80") // NOSONAR
        .WithEnvironment("CLIENTS_CONFIGURATION_INLINE", MockOidc.ClientsJson())
        .WithEnvironment("USERS_CONFIGURATION_INLINE", MockOidc.UsersJson())
        .WithEnvironment("API_SCOPES_INLINE", MockOidc.ApiScopesJson())
        .WithEnvironment("API_RESOURCES_INLINE", MockOidc.ApiResourcesJson())
        // The IdP session cookies default to SameSite=None, which browsers drop without Secure; the mock is plain http, so login would loop.
        .WithEnvironment("SERVER_OPTIONS_INLINE", MockOidc.ServerOptionsJson());
}

// --- API ---

var api = builder.AddProject<Projects.Spydersoft_SwingTrade_Api>("api", launchProfileName: "http")
    .WithEndpoint("http", e => { e.Port = ApiPort; e.TargetPort = ApiPort; e.IsProxied = false; })
    .WithEnvironment("ASPNETCORE_URLS", "http://localhost:" + ApiPort)
    .WithEnvironment("Telemetry__Log__Type", "otlp")
    .WithEnvironment("Telemetry__Metrics__Type", "otlp")
    .WithEnvironment("Telemetry__Trace__Type", "otlp")
    .WithReference(db)
    .WaitFor(db);

if (mockOidc is not null)
{
    // The API still validates real tokens (issuer, signature, audience) against this authority's published keys.
    api.WithEnvironment("Auth__Authority", "http://localhost:" + MockOidcPort)
       .WithEnvironment("Auth__RequireHttpsMetadata", "false")
       // The MCP endpoint is owner-only: the owner is the "mcponly" mock user. "mcpother" holds the mcp role but is not the owner.
       .WithEnvironment("Mcp__PublicUrl", "http://localhost:" + ApiPort + "/mcp")
       .WithEnvironment("Mcp__OwnerSubject", MockOidc.McpOwnerSubject)
       .WaitFor(mockOidc);
}

if (isTesting)
{
    // Market data: the real Alpaca and Tiingo HTTP code runs against a WireMock container stocked with canned
    // responses (src/api/tests/api-integration/wiremock/mappings). No test touches a real provider.
    var mappingsDir = Path.GetFullPath(Path.Combine(
        builder.Environment.ContentRootPath, "..", "api", "tests", "api-integration", "wiremock", "mappings"));
    var mockMarketData = builder.AddContainer("mock-marketdata", "wiremock/wiremock", "3.9.1")
        .WithHttpEndpoint(port: WireMockPort, targetPort: 8080, name: "http", isProxied: false)
        .WithBindMount(mappingsDir, "/home/wiremock/mappings", isReadOnly: true);

    // Provider names, keys and the rest of the Testing configuration are in the API's appsettings.Testing.json.
    api.WithEnvironment("DOTNET_ENVIRONMENT", TestingEnvironmentName)
       .WithEnvironment("ASPNETCORE_ENVIRONMENT", TestingEnvironmentName)
       .WithEnvironment("MarketData__Alpaca__BaseUrl", "http://localhost:" + WireMockPort)
       .WithEnvironment("MarketData__Tiingo__BaseUrl", "http://localhost:" + WireMockPort)
       .WaitFor(mockMarketData);
}

// --- Web: the BFF (OidcProxy) that serves the SPA and forwards /api/v1 with the user's access token, plus Vite ---

string webClientId, webClientSecret, webAuthority;
if (mockOidc is not null)
{
    webClientId = MockOidc.WebClientId;
    webClientSecret = MockOidc.WebClientSecret;
    webAuthority = "http://localhost:" + MockOidcPort;
}
else
{
    webClientId = builder.Configuration["OidcProxy:ClientId"]
        ?? throw new InvalidOperationException("OidcProxy:ClientId is not set in user secrets.");
    webClientSecret = builder.Configuration["OidcProxy:ClientSecret"]
        ?? throw new InvalidOperationException("OidcProxy:ClientSecret is not set in user secrets.");
    webAuthority = "https://auth.mattgerega.net";
}

var frontendProjectDir = Path.GetFullPath(
    Path.Combine(builder.Environment.ContentRootPath, "..", "web", "src", "Spydersoft.SwingTrade.Frontend"));

var web = builder.AddProject<Projects.Spydersoft_SwingTrade_Frontend>("web", launchProfileName: "http")
    .WithEndpoint("http", e => { e.Port = WebPort; e.TargetPort = WebPort; e.IsProxied = false; })
    .WithEnvironment("ASPNETCORE_URLS", "http://localhost:" + WebPort)
    .WithEnvironment("ASPNETCORE_CONTENTROOT", frontendProjectDir)
    .WithEnvironment("OidcProxySettings__Oidc__Authority", webAuthority)
    .WithEnvironment("OidcProxySettings__Oidc__ClientId", webClientId)
    .WithEnvironment("OidcProxySettings__Oidc__ClientSecret", webClientSecret)
    .WithEnvironment("OidcProxySettings__ReverseProxy__Clusters__swingTradeApi__Destinations__destination1__Address", "http://localhost:" + ApiPort + "/")
    .WithEnvironment("Telemetry__Log__Type", "otlp")
    .WithEnvironment("Telemetry__Metrics__Type", "otlp")
    .WithEnvironment("Telemetry__Trace__Type", "otlp")
    .WithHttpHealthCheck("/livez", endpointName: "http")
    .WaitFor(api);

if (mockOidc is not null)
{
    // OidcProxy.Net rewrites its own redirect_uri to https unless told otherwise; the mock IdP and the BFF are plain http.
    web.WithEnvironment("OidcProxySettings__AlwaysRedirectToHttps", "false");
    web.WaitFor(mockOidc);
}

if (!isTesting)
{
    // The Vite dev server (hot reload). It proxies /api, /oauth2 and the probes to the BFF, so sign-in works from
    // http://localhost:5290 as well (that redirect URI is registered for the mock client). Testing serves the built
    // SPA from the BFF instead, so the suite never depends on a dev server.
    builder.AddViteApp("swing-trade-ui", "../web/src/swing-trade-ui")
        .WithYarn()
        .WithEnvironment("BFF_URL", "http://localhost:" + WebPort)
        .WithEndpoint("http", e => { e.Port = VitePort; e.IsProxied = false; });
}

var app = builder.Build();

if (isTesting)
{
    // Child-resource logs only go to the Aspire dashboard, which nobody sees in CI. Echo them to the console so a
    // stack that never comes up is diagnosable from the pipeline log.
    EchoResourceLogs(app);
}

await app.RunAsync();

static void EchoResourceLogs(DistributedApplication app)
{
    var logger = app.Services.GetRequiredService<ResourceLoggerService>();
    var notifications = app.Services.GetRequiredService<ResourceNotificationService>();

    _ = Task.Run(() => EchoResourceStates(notifications, logger));
}

// Logs are keyed by the replica's resource id (for example "api-ab12cd"), not the model resource name, so log watchers
// start the first time a resource id shows up in the state stream.
static async Task EchoResourceStates(ResourceNotificationService notifications, ResourceLoggerService logger)
{
    var lastStates = new Dictionary<string, string?>();
    try
    {
        await foreach (var resourceEvent in notifications.WatchAsync())
        {
            var id = resourceEvent.ResourceId;
            var state = resourceEvent.Snapshot.State?.Text;
            if (!lastStates.TryGetValue(id, out var previous))
            {
                _ = Task.Run(() => EchoLogs(logger, id));
            }
            else if (previous == state)
            {
                continue;
            }

            lastStates[id] = state;
            Console.WriteLine($"[state] {id}: {state} (exit code {resourceEvent.Snapshot.ExitCode})");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[state] watcher failed: {ex}");
    }
}

static async Task EchoLogs(ResourceLoggerService logger, string resourceId)
{
    try
    {
        await foreach (var batch in logger.WatchAsync(resourceId))
        {
            foreach (var line in batch)
            {
                Console.WriteLine($"[{resourceId}] {line.Content}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{resourceId}] log watcher failed: {ex}");
    }
}
