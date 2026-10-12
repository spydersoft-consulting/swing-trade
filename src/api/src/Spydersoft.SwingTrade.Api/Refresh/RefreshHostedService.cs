namespace Spydersoft.SwingTrade.Api.Refresh;

/// <summary>
/// Runs the refresh at the times <see cref="RefreshSchedule"/> gives. The DbContext and typed HTTP clients are scoped,
/// so each attempt resolves <see cref="RefreshService"/> from a fresh scope (same pattern as PitStop's RecallCheckJob).
/// Disabled with Refresh:ScheduleEnabled=false.
/// </summary>
public class RefreshHostedService(
    IServiceScopeFactory scopeFactory,
    RefreshSchedule schedule,
    TimeProvider time,
    ILogger<RefreshHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cursor = time.GetUtcNow();
        while (!stoppingToken.IsCancellationRequested)
        {
            var next = schedule.NextAfter(cursor);
            logger.LogInformation("Next {Attempt} refresh at {At:O}", next.Attempt, next.At);

            var wait = next.At - time.GetUtcNow();
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, time, stoppingToken);

            // Never schedule the same instant twice, even if the timer fired fractionally early.
            cursor = next.At > time.GetUtcNow() ? next.At : time.GetUtcNow();

            await RunAttemptAsync(next, stoppingToken);
        }
    }

    internal async Task RunAttemptAsync(ScheduledRun scheduled, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var refresh = scope.ServiceProvider.GetRequiredService<RefreshService>();

            if (scheduled.Attempt == RefreshAttempt.CatchUp && !await refresh.IsLatestBarMissingAsync(ct))
            {
                logger.LogInformation("Catch-up refresh skipped: the latest expected bar is already stored");
                return;
            }

            var run = await refresh.RunAsync($"scheduled-{scheduled.Attempt.ToString().ToLowerInvariant()}", ct);
            if (run is null)
                logger.LogWarning("Scheduled {Attempt} refresh skipped: another refresh is running", scheduled.Attempt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Scheduled {Attempt} refresh failed", scheduled.Attempt);
        }
    }
}
