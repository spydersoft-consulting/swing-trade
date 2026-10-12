namespace Spydersoft.SwingTrade.MarketData;

/// <summary>Seam for waiting, so retry back-off and rate-limit spacing are instant in tests.</summary>
public interface IDelay
{
    Task DelayAsync(TimeSpan duration, CancellationToken ct);
}

public sealed class TaskDelay : IDelay
{
    public Task DelayAsync(TimeSpan duration, CancellationToken ct) =>
        duration <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(duration, ct);
}
