namespace Spydersoft.SwingTrade.Api.Refresh;

/// <summary>Continues an already-started run in the background (the manual endpoint returns 202 immediately).</summary>
public interface IRefreshLauncher
{
    void Launch(int runId);
}

public sealed class RefreshLauncher(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<RefreshLauncher> logger) : IRefreshLauncher
{
    public void Launch(int runId) =>
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<RefreshService>().ExecuteAsync(runId, lifetime.ApplicationStopping);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background refresh run {RunId} failed", runId);
            }
        });
}
