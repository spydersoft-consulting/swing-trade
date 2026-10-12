using System.Net;

namespace Spydersoft.SwingTrade.MarketData;

internal static class ProviderHttp
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    /// <summary>Maps a non-success response to the typed exception the fallback logic understands.</summary>
    public static Exception ToException(HttpResponseMessage response, string ticker, string provider)
    {
        var status = response.StatusCode;
        if (status is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest)
            return new TickerNotFoundException(ticker, provider);

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return new ProviderUnavailableException($"{provider} rejected the credentials ({(int)status}).", retryable: false);

        TimeSpan? retryAfter = response.Headers.RetryAfter is { } ra
            ? ra.Delta ?? (ra.Date is { } d ? d - DateTimeOffset.UtcNow : null)
            : null;
        if (retryAfter is { } t && t < TimeSpan.Zero) retryAfter = null;

        var retryable = status == HttpStatusCode.TooManyRequests || (int)status >= 500 || status == HttpStatusCode.RequestTimeout;
        return new ProviderUnavailableException($"{provider} returned HTTP {(int)status}.", retryable, retryAfter);
    }

    /// <summary>Wraps transport failures and timeouts (not caller cancellation) as retryable provider errors.</summary>
    public static async Task<T> GuardAsync<T>(string provider, Func<Task<T>> action, CancellationToken ct)
    {
        try
        {
            return await action();
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderUnavailableException($"{provider} request failed: {ex.Message}", inner: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ProviderUnavailableException($"{provider} request timed out.", inner: ex);
        }
    }

    public static DateOnly ToNewYorkDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, NewYork).DateTime);

    /// <summary>Midnight New York on the given date, as an instant.</summary>
    public static DateTimeOffset NewYorkMidnight(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, NewYork.GetUtcOffset(local));
    }
}
