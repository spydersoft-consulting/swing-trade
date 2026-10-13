using Microsoft.EntityFrameworkCore;
using Spydersoft.SwingTrade.Data;

namespace Spydersoft.SwingTrade.Api.Refresh;

/// <summary>
/// Opt-in first-run load (<c>Refresh:RunOnStartupWhenEmpty</c>, DECISIONS #102): when the application starts and no price
/// bars exist yet, run one refresh so the Watchlist is not empty. It does nothing once any bar is stored, so restarts never
/// cause a refresh. Off by default (DECISIONS #46: production loads data with POST /api/v1/refresh after the first deploy);
/// the Development settings turn it on for the Local profile, which uses the fake provider.
/// </summary>
public class StartupRefreshService(IServiceScopeFactory scopeFactory, ILogger<StartupRefreshService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let host start-up (and the other hosted services) finish before the first query.
        await Task.Yield();
        await RunIfEmptyAsync(stoppingToken);
    }

    internal async Task RunIfEmptyAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SwingTradeDbContext>();
            if (await db.PriceBars.AnyAsync(ct))
            {
                logger.LogInformation("Startup refresh skipped: price bars already exist");
                return;
            }

            logger.LogInformation("No price bars stored; running the first refresh");
            var run = await scope.ServiceProvider.GetRequiredService<RefreshService>().RunAsync("startup", ct);
            if (run is null)
                logger.LogWarning("Startup refresh skipped: another refresh is running");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Startup refresh failed");
        }
    }
}
