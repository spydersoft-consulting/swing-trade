using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Spydersoft.SwingTrade.Data;

namespace Spydersoft.SwingTrade.Api.Health;

/// <summary>
/// Readiness: the API cannot serve anything without Postgres, so an unreachable database is Unhealthy (HTTP 503 from /readyz,
/// which takes the pod out of the Service). It is tagged "ready" only, never "live": a database outage must not restart the pod.
/// </summary>
public sealed class DatabaseHealthCheck(SwingTradeDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Database reachable")
                : HealthCheckResult.Unhealthy("Database not reachable");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Database check failed", ex);
        }
    }
}
