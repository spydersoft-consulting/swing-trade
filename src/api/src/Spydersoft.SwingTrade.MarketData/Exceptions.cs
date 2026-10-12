namespace Spydersoft.SwingTrade.MarketData;

/// <summary>Rate limit, server error, timeout or authentication problem. The registry may fall back.</summary>
public class ProviderUnavailableException : Exception
{
    public ProviderUnavailableException(string message, bool retryable = true, TimeSpan? retryAfter = null, Exception? inner = null)
        : base(message, inner)
    {
        Retryable = retryable;
        RetryAfter = retryAfter;
    }

    /// <summary>False for configuration problems such as bad credentials, where retrying cannot help.</summary>
    public bool Retryable { get; }

    /// <summary>Server-provided back-off (Retry-After), when given.</summary>
    public TimeSpan? RetryAfter { get; }
}

/// <summary>The provider does not know the symbol.</summary>
public class TickerNotFoundException(string ticker, string provider)
    : Exception($"{provider} does not know ticker {ticker}.");

/// <summary>Every configured provider failed for one ticker.</summary>
public class AllProvidersFailedException(string ticker, IReadOnlyList<string> failures)
    : Exception($"All providers failed for {ticker}: {string.Join("; ", failures)}")
{
    public IReadOnlyList<string> Failures { get; } = failures;
}
