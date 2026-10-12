using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Spydersoft.SwingTrade.MarketData;

/// <summary>Bars plus the name of the provider that supplied them.</summary>
public sealed record BarFetchResult(string Provider, IReadOnlyList<DailyBar> Bars);

/// <summary>What the ingestion service depends on: fetch bars and learn which provider supplied them.</summary>
public interface IBarFetcher
{
    /// <summary>Configured provider order, for example "alpaca>tiingo".</summary>
    string ProviderChain { get; }

    /// <param name="expectedLatestBar">
    /// When given, a provider whose newest bar is older than this is treated as lagging and the next provider is tried too;
    /// the result with the newest bar wins.
    /// </param>
    Task<BarFetchResult> FetchAsync(string ticker, DateOnly from, DateOnly to, DateOnly? expectedLatestBar, CancellationToken ct);
}

/// <summary>
/// Tries the configured providers in order for each ticker. Spaces calls per provider by its
/// <see cref="IMarketDataProvider.MinDelayBetweenCalls"/> and retries retryable failures with back-off.
/// Create one instance per refresh run (it remembers when each provider was last called).
/// </summary>
public sealed class FallbackMarketDataProvider : IMarketDataProvider, IBarFetcher
{
    private readonly IReadOnlyList<IMarketDataProvider> _providers;
    private readonly RetryPolicy _retry;
    private readonly IDelay _delay;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Dictionary<string, DateTimeOffset> _lastCall = new(StringComparer.OrdinalIgnoreCase);

    public FallbackMarketDataProvider(
        IReadOnlyList<IMarketDataProvider> providers,
        IOptions<MarketDataOptions> options,
        IDelay delay,
        TimeProvider time,
        ILogger<FallbackMarketDataProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(options);
        if (providers.Count == 0)
            throw new ArgumentException("At least one provider is required.", nameof(providers));

        _providers = providers;
        _delay = delay;
        _time = time;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _retry = new RetryPolicy(
            Math.Max(1, options.Value.MaxAttempts),
            TimeSpan.FromMilliseconds(options.Value.RetryBaseDelayMs),
            delay);
    }

    public string Name => "fallback";
    public string ProviderChain => string.Join('>', _providers.Select(p => p.Name));
    public TimeSpan MinDelayBetweenCalls => TimeSpan.Zero;

    public async Task<IReadOnlyList<DailyBar>> GetDailyBars(string ticker, DateOnly from, DateOnly to, CancellationToken ct) =>
        (await FetchAsync(ticker, from, to, null, ct)).Bars;

    public async Task<BarFetchResult> FetchAsync(string ticker, DateOnly from, DateOnly to, DateOnly? expectedLatestBar, CancellationToken ct)
    {
        var failures = new List<string>();
        BarFetchResult? best = null;

        foreach (var provider in _providers)
        {
            IReadOnlyList<DailyBar> bars;
            try
            {
                bars = await _retry.ExecuteAsync(token => CallAsync(provider, ticker, from, to, token), ct);
            }
            catch (Exception ex) when (ex is ProviderUnavailableException or TickerNotFoundException)
            {
                _logger.LogWarning(ex, "Provider {Provider} failed for {Ticker}; trying the next provider", provider.Name, ticker);
                failures.Add($"{provider.Name}: {ex.Message}");
                continue;
            }

            var result = new BarFetchResult(provider.Name, bars);
            if (expectedLatestBar is not { } expected || (bars.Count > 0 && bars.Max(b => b.Date) >= expected))
                return result;

            // Lagging or empty: remember it, but give the next provider a chance to have the expected bar.
            if (best is null || Latest(result) > Latest(best))
                best = result;
        }

        if (best is not null)
            return best;

        throw new AllProvidersFailedException(ticker, failures);
    }

    private static DateOnly Latest(BarFetchResult r) => r.Bars.Count == 0 ? DateOnly.MinValue : r.Bars.Max(b => b.Date);

    private async Task<IReadOnlyList<DailyBar>> CallAsync(IMarketDataProvider provider, string ticker, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (_lastCall.TryGetValue(provider.Name, out var last))
        {
            var wait = provider.MinDelayBetweenCalls - (_time.GetUtcNow() - last);
            if (wait > TimeSpan.Zero)
                await _delay.DelayAsync(wait, ct);
        }

        _lastCall[provider.Name] = _time.GetUtcNow();
        return await provider.GetDailyBars(ticker, from, to, ct);
    }
}
