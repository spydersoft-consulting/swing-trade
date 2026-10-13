using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Logging;
using OidcProxy.Net.ModuleInitializers;
using OidcProxy.Net.OpenIdConnect;
using Spydersoft.Platform.Hosting.Options;
using Spydersoft.Platform.Hosting.StartupExtensions;
using Spydersoft.Platform.Hosting.Telemetry;
using StackExchange.Redis;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.AddSpydersoftTelemetry(typeof(Program).Assembly,
    new ConfigurationFunctions
    {
        // Kubernetes probes hit these every few seconds; they add nothing but noise to traces.
        AspNetFilterFunction = context => !IsHealthCheckPath(context.Request.Path.Value)
    });
builder.AddSpydersoftSerilog(true);
AppHealthCheckOptions healthCheckOptions = builder.AddSpydersoftHealthChecks();

var config = builder.Configuration
    .GetSection("OidcProxySettings")
    .Get<OidcProxyConfig>();

if (config != null)
{
    // Sessions (and the data-protection keys that sign the cookie) live in Redis when it is configured, so they
    // survive pod restarts and deploys. Without it (local dev, tests) OidcProxy falls back to in-memory sessions.
    var redisConnectionString = builder.Configuration["Redis:ConnectionString"];
    var redis = string.IsNullOrWhiteSpace(redisConnectionString) ? null : ConnectionMultiplexer.Connect(redisConnectionString);
    builder.Services.AddOidcProxy(config, options =>
    {
        if (redis is not null)
        {
            options.ConfigureRedisBackBone(redis);
        }
    });

    if (redis is not null)
    {
        // Readiness only (never liveness): without Redis the sessions are gone, so the pod should leave the Service, not restart.
        // The API is deliberately not a readiness dependency, so an API outage does not cascade into the UI shell.
        builder.Services.AddHealthChecks().AddAsyncCheck("redis", async () =>
        {
            try
            {
                await redis.GetDatabase().PingAsync();
                return HealthCheckResult.Healthy("Redis reachable");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Redis not reachable", ex);
            }
        }, tags: ["ready"]);
    }
}
else
{
    throw new InvalidOperationException("OidcProxySettings configuration section is missing or invalid.");
}

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("RequireAuthenticatedUserPolicy", policy =>
    {
        policy.RequireAuthenticatedUser();
    });

builder.Services.AddControllers();

var app = builder.Build();

// Behind the gateway the original scheme and client address arrive in X-Forwarded-*; this must run before anything
// (the OIDC redirect URI in particular) reads the request scheme.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

app.UseRouting();
app.UseSpydersoftRequestLogging();

// Served dynamically so per-environment values come from configuration (the same env-var path as OidcProxySettings)
// rather than a build-time file. Anonymous: the SPA shell needs it before any auth check happens.
app.MapMethods("/config.js", ["GET", "HEAD"], (IConfiguration configuration) =>
{
    var settings = new
    {
        api_url = configuration["Frontend:ApiUrl"] ?? "/api/v1",
    };
    return Results.Text($"globalThis.__config = {JsonSerializer.Serialize(settings)};", "application/javascript");
});

app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    IdentityModelEventSource.ShowPII = true;
    IdentityModelEventSource.LogCompleteSecurityArtifact = true;
}

app.MapControllers();
app.UseSpydersoftHealthChecks(healthCheckOptions);

app.MapFallbackToFile("/index.html");

app.UseOidcProxy();

await app.RunAsync();

static bool IsHealthCheckPath(string? path) =>
    string.Equals(path, "/livez", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(path, "/readyz", StringComparison.OrdinalIgnoreCase);
