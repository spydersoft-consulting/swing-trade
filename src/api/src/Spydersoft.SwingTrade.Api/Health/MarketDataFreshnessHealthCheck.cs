using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Spydersoft.SwingTrade.Api.Refresh;
using Spydersoft.SwingTrade.Data;

namespace Spydersoft.SwingTrade.Api.Health;

/// <summary>
/// Readiness detail: Degraded (still HTTP 200, so the pod keeps serving) when the newest stored indicator date is older than the
/// latest expected bar, or when no prices have been loaded yet. It is the same staleness rule as the Watchlist's badge
/// (<see cref="MarketCalendar.IsStale"/>), so an operator can see a silent provider failure from the probe body.
/// Never Unhealthy: stale prices are not a reason to stop serving the journal.
/// </summary>
public sealed class MarketDataFreshnessHealthCheck(SwingTradeDbContext db, MarketCalendar calendar) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var latest = await db.IndicatorSnapshots.Select(s => (DateOnly?)s.AsOfDate).MaxAsync(cancellationToken);
            if (latest is null)
                return HealthCheckResult.Degraded("No price data yet; run POST /api/v1/refresh");

            var data = new Dictionary<string, object> { ["latestAsOf"] = latest.Value.ToString("yyyy-MM-dd"), ["latestExpected"] = calendar.LatestExpectedBar().ToString("yyyy-MM-dd") };
            return calendar.IsStale(latest)
                ? HealthCheckResult.Degraded($"Prices are stale: newest as-of {latest:yyyy-MM-dd}, expected {calendar.LatestExpectedBar():yyyy-MM-dd}", data: data)
                : HealthCheckResult.Healthy("Prices current", data);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The database check reports the outage; this one only says it could not look.
            return HealthCheckResult.Degraded("Price freshness could not be read", ex);
        }
    }
}
