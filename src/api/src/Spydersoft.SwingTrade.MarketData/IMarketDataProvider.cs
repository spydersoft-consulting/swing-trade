namespace Spydersoft.SwingTrade.MarketData;

/// <summary>
/// A source of daily bars. Contract for every implementation: bars sorted ascending, one per trading date
/// (New York exchange date, not UTC), <see cref="DailyBar.Close"/> split-adjusted and dividend-UNadjusted.
/// Return an empty list on holidays. Throw <see cref="ProviderUnavailableException"/> for 429/5xx/timeouts
/// and <see cref="TickerNotFoundException"/> for an unknown symbol.
/// </summary>
public interface IMarketDataProvider
{
    /// <summary>Lower-case configuration name: "alpaca", "tiingo", "fake".</summary>
    string Name { get; }

    /// <summary>Rate-limit spacing between calls, owned by the provider.</summary>
    TimeSpan MinDelayBetweenCalls { get; }

    Task<IReadOnlyList<DailyBar>> GetDailyBars(string ticker, DateOnly from, DateOnly to, CancellationToken ct);
}
