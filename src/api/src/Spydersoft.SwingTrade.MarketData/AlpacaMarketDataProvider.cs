using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Spydersoft.SwingTrade.MarketData;

/// <summary>
/// Alpaca Market Data v2 daily bars: <c>feed=sip</c> (consolidated), <c>adjustment=split</c> (never dividend),
/// <c>timeframe=1Day</c>, paginated by <c>next_page_token</c>. The request end is kept at least 15 minutes in the past
/// because the free plan only serves SIP data older than that.
/// </summary>
public sealed class AlpacaMarketDataProvider(HttpClient http, IOptions<MarketDataOptions> options, TimeProvider time) : IMarketDataProvider
{
    public const string ProviderName = "alpaca";
    private const int MaxPages = 50;
    private static readonly TimeSpan MinimumAge = TimeSpan.FromMinutes(16);

    private AlpacaOptions Options => options.Value.Alpaca;

    public string Name => ProviderName;
    public TimeSpan MinDelayBetweenCalls => TimeSpan.FromMilliseconds(Options.MinDelayMs);

    public Task<IReadOnlyList<DailyBar>> GetDailyBars(string ticker, DateOnly from, DateOnly to, CancellationToken ct) =>
        ProviderHttp.GuardAsync(Name, () => FetchAsync(ticker, from, to, ct), ct);

    private async Task<IReadOnlyList<DailyBar>> FetchAsync(string ticker, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var start = ProviderHttp.NewYorkMidnight(from);
        var end = ProviderHttp.NewYorkMidnight(to.AddDays(1));
        var latestAllowed = time.GetUtcNow() - MinimumAge;
        if (end > latestAllowed) end = latestAllowed;
        if (end <= start)
            return [];

        var bars = new List<DailyBar>();
        string? pageToken = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var url = $"{Options.BaseUrl.TrimEnd('/')}/v2/stocks/{Uri.EscapeDataString(ticker)}/bars" +
                      $"?timeframe=1Day&adjustment=split&feed={Uri.EscapeDataString(Options.Feed)}&limit=10000" +
                      $"&start={Uri.EscapeDataString(Iso(start))}&end={Uri.EscapeDataString(Iso(end))}" +
                      (pageToken is null ? "" : $"&page_token={Uri.EscapeDataString(pageToken)}");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("APCA-API-KEY-ID", Options.KeyId);
            request.Headers.Add("APCA-API-SECRET-KEY", Options.SecretKey);

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw ProviderHttp.ToException(response, ticker, Name);

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            if (root.TryGetProperty("bars", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                    bars.Add(Parse(item));
            }

            pageToken = root.TryGetProperty("next_page_token", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
            if (string.IsNullOrEmpty(pageToken))
                break;
        }

        return bars.OrderBy(b => b.Date).ToList();
    }

    private static DailyBar Parse(JsonElement item) => new(
        ProviderHttp.ToNewYorkDate(DateTimeOffset.Parse(item.GetProperty("t").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)),
        item.GetProperty("o").GetDecimal(),
        item.GetProperty("h").GetDecimal(),
        item.GetProperty("l").GetDecimal(),
        item.GetProperty("c").GetDecimal(),
        item.TryGetProperty("v", out var v) ? (long)v.GetDecimal() : 0L);

    private static string Iso(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
