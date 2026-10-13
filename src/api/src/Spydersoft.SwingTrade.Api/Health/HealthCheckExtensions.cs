namespace Spydersoft.SwingTrade.Api.Health;

public static class SwingTradeHealthChecks
{
    /// <summary>Tag for /readyz (matches <c>HealthCheck:ReadyTags</c> in appsettings.json).</summary>
    public const string ReadyTag = "ready";

    /// <summary>
    /// Registers the readiness checks: the database (Unhealthy when unreachable) and price freshness (Degraded when stale).
    /// Nothing is tagged "live": /livez only proves the process answers, so a database outage never restarts the pod.
    /// </summary>
    public static IServiceCollection AddSwingTradeHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag])
            .AddCheck<MarketDataFreshnessHealthCheck>("market-data", tags: [ReadyTag]);
        return services;
    }
}
