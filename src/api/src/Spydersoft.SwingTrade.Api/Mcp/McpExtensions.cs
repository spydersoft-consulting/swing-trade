using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Spydersoft.SwingTrade.Api.Refresh;
using Spydersoft.SwingTrade.Api.Services;

namespace Spydersoft.SwingTrade.Api.Mcp;

public static class McpExtensions
{
    /// <summary>The application services both the REST controllers and the MCP tools call (DECISIONS #9).</summary>
    public static IServiceCollection AddSwingTradeApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<SettingsService>();
        services.AddScoped<InstrumentService>();
        services.AddScoped<TradeService>();
        services.AddScoped<NoteService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<WatchlistService>();
        services.AddScoped<BarService>();
        services.AddScoped<SizerService>();
        services.AddScoped<ExportService>();
        return services;
    }

    /// <summary>
    /// The MCP server (Streamable HTTP, stateless), its options and validation, the rate limiter, the call audit and the
    /// retention service. Authentication itself is the JWT bearer scheme the rest of the API uses.
    /// </summary>
    public static IServiceCollection AddSwingTradeMcp(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection(McpOptions.Section).Get<McpOptions>() ?? new McpOptions();
        options.Authority = configuration["Auth:Authority"];
        options.Validate(relaxed: environment.IsDevelopment() || environment.IsEnvironment("Testing"));

        services.Configure<McpOptions>(configuration.GetSection(McpOptions.Section));
        services.PostConfigure<McpOptions>(o => o.Authority = configuration["Auth:Authority"]);

        services.AddScoped<McpEnvelopeFactory>();
        services.AddSingleton<McpCallRecorder>();
        services.AddHostedService<McpCallLogRetentionService>();

        var builder = services.AddMcpServer()
            .WithHttpTransport(t => t.Stateless = true)
            .AddAuthorizationFilters()
            .WithTools<JournalTools>()
            .WithResources<JournalResources>()
            .WithPrompts<JournalPrompts>()
            .WithRequestFilters(McpAuditFilters.Apply);
        if (options.EnableAddNote)
            builder.WithTools<JournalWriteTools>();

        services.AddRateLimiter(rl =>
        {
            rl.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rl.AddPolicy(McpOptions.RateLimitPolicy, context =>
            {
                var limit = context.RequestServices.GetRequiredService<IOptions<McpOptions>>().Value.RequestsPerMinute;
                return RateLimitPartition.GetFixedWindowLimiter("mcp", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
            });
        });
        return services;
    }

    /// <summary>Host allowlist and rate limiting for the MCP routes. Place before authentication.</summary>
    public static IApplicationBuilder UseSwingTradeMcp(this IApplicationBuilder app)
    {
        app.UseWhen(IsMcpRoute, branch => branch.UseMiddleware<McpHostFilterMiddleware>());
        app.UseRateLimiter();
        return app;
    }

    /// <summary>Maps /mcp (owner and mcp role required) and the anonymous RFC 9728 protected resource metadata.</summary>
    public static IEndpointRouteBuilder MapSwingTradeMcp(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMcp(McpOptions.McpPath)
            .RequireAuthorization(SwingTradeAuth.Policies.McpOwner)
            .RequireRateLimiting(McpOptions.RateLimitPolicy);

        foreach (var path in new[] { McpOptions.MetadataPath, McpOptions.MetadataPath + McpOptions.McpPath })
        {
            endpoints.MapGet(path, (IOptions<McpOptions> options) => Metadata(options.Value))
                .AllowAnonymous()
                .RequireRateLimiting(McpOptions.RateLimitPolicy)
                .ExcludeFromDescription();
        }

        return endpoints;
    }

    internal static bool IsMcpRoute(HttpContext context) =>
        context.Request.Path.StartsWithSegments(McpOptions.McpPath, StringComparison.OrdinalIgnoreCase)
        || context.Request.Path.StartsWithSegments(McpOptions.MetadataPath, StringComparison.OrdinalIgnoreCase);

    private static IResult Metadata(McpOptions o)
    {
        if (o.PublicUrl is null || o.PublicUri is null)
            return TypedResults.Problem("The MCP public URL is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

        var servers = new List<string>();
        if (!string.IsNullOrWhiteSpace(o.Authority))
            servers.Add(o.Authority.TrimEnd('/'));

        // RFC 9728: resource is the MCP URL exactly as configured; the issuer is listed first.
        return TypedResults.Json(new Dictionary<string, object>
        {
            ["resource"] = o.PublicUrl,
            ["authorization_servers"] = servers,
            ["scopes_supported"] = new[] { SwingTradeAuth.Audience },
            ["bearer_methods_supported"] = new[] { "header" },
            ["resource_name"] = "Swing Trade journal"
        });
    }
}

/// <summary>Answers 421 for a Host header not on the allowlist. Only applied to the MCP routes.</summary>
public sealed class McpHostFilterMiddleware(RequestDelegate next, IOptions<McpOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var host = context.Request.Host.Host;
        if (!options.Value.EffectiveAllowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status421MisdirectedRequest;
            return;
        }

        await next(context);
    }
}
