using Microsoft.Extensions.Options;

namespace Spydersoft.SwingTrade.MarketData;

/// <summary>
/// Deterministic provider for tests and for running the app without API keys. Generates a weekday series per ticker
/// (a smooth wave on a ticker-specific base price) unless bars were set explicitly. Does not know exchange holidays.
/// </summary>
public sealed class FakeMarketDataProvider : IMarketDataProvider
{
    public const string ProviderName = "fake";

    private readonly Dictionary<string, List<DailyBar>> _explicit = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> _failures = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _minDelay;
    private readonly object _gate = new();

    public FakeMarketDataProvider(IOptions<MarketDataOptions>? options = null)
    {
        var fake = options?.Value.Fake ?? new FakeOptions();
        _minDelay = TimeSpan.FromMilliseconds(fake.MinDelayMs);
        foreach (var t in fake.FailTickers)
            _failures[t] = new ProviderUnavailableException($"Fake provider configured to fail {t}.", retryable: false);
    }

    public string Name => ProviderName;
    public TimeSpan MinDelayBetweenCalls => _minDelay;

    /// <summary>Every request received, as (ticker, from, to).</summary>
    public List<(string Ticker, DateOnly From, DateOnly To)> Calls { get; } = [];

    /// <summary>Replaces the generated series for a ticker.</summary>
    public FakeMarketDataProvider SetBars(string ticker, IEnumerable<DailyBar> bars)
    {
        lock (_gate) _explicit[ticker] = bars.OrderBy(b => b.Date).ToList();
        return this;
    }

    /// <summary>Makes every call for the ticker throw. Defaults to a non-retryable unavailable error.</summary>
    public FakeMarketDataProvider FailFor(string ticker, Exception? error = null)
    {
        lock (_gate) _failures[ticker] = error ?? new ProviderUnavailableException($"Fake provider failure for {ticker}.", retryable: false);
        return this;
    }

    public Task<IReadOnlyList<DailyBar>> GetDailyBars(string ticker, DateOnly from, DateOnly to, CancellationToken ct)
    {
        lock (_gate)
        {
            Calls.Add((ticker, from, to));
            if (_failures.TryGetValue(ticker, out var error))
                throw error;

            IReadOnlyList<DailyBar> result = _explicit.TryGetValue(ticker, out var bars)
                ? bars.Where(b => b.Date >= from && b.Date <= to).ToList()
                : Generate(ticker, from, to);
            return Task.FromResult(result);
        }
    }

    /// <summary>The generated close for a ticker on a date; stable regardless of the requested range.</summary>
    public static decimal GeneratedClose(string ticker, DateOnly date)
    {
        var seed = ticker.Aggregate(17, (h, c) => unchecked(h * 31 + char.ToUpperInvariant(c)));
        var basePrice = 50m + Math.Abs(seed % 400);
        var wave = (decimal)Math.Sin(date.DayNumber / 9.0) * 6m + (date.DayNumber % 5) * 0.1m;
        return Math.Round(basePrice + wave + date.DayNumber % 700 * 0.02m, 2);
    }

    private static List<DailyBar> Generate(string ticker, DateOnly from, DateOnly to)
    {
        var list = new List<DailyBar>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            var close = GeneratedClose(ticker, d);
            list.Add(new DailyBar(d, close - 0.15m, close + 0.40m, close - 0.45m, close, 1_000_000 + d.DayNumber % 1000));
        }
        return list;
    }
}
