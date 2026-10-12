namespace Spydersoft.SwingTrade.MarketData;

/// <summary>Small retry-with-backoff helper (no Polly: the rules are tiny and must be testable without waiting).</summary>
public sealed class RetryPolicy(int maxAttempts, TimeSpan baseDelay, IDelay delay)
{
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action(ct);
            }
            catch (ProviderUnavailableException ex) when (ex.Retryable && attempt < maxAttempts)
            {
                var wait = ex.RetryAfter ?? TimeSpan.FromTicks(baseDelay.Ticks * (1L << (attempt - 1)));
                await delay.DelayAsync(wait, ct);
            }
        }
    }
}
