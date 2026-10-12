using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Spydersoft.SwingTrade.MarketData;

/// <summary>
/// Tiingo end-of-day prices. Uses the RAW <c>close</c> (never <c>adjClose</c>, which includes dividends).
/// A split inside the requested window is normalised to split-only by dividing each earlier bar by the product of the
/// <c>splitFactor</c> values of the later bars, so the window is internally consistent. History stored from before
/// the window is reconciled by the ingestion service's split detection.
/// </summary>
public sealed class TiingoMarketDataProvider(HttpClient http, IOptions<MarketDataOptions> options) : IMarketDataProvider
{
    public const string ProviderName = "tiingo";

    private TiingoOptions Options => options.Value.Tiingo;

    public string Name => ProviderName;
    public TimeSpan MinDelayBetweenCalls => TimeSpan.FromMilliseconds(Options.MinDelayMs);

    public Task<IReadOnlyList<DailyBar>> GetDailyBars(string ticker, DateOnly from, DateOnly to, CancellationToken ct) =>
        ProviderHttp.GuardAsync(Name, () => FetchAsync(ticker, from, to, ct), ct);

    private async Task<IReadOnlyList<DailyBar>> FetchAsync(string ticker, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var url = $"{Options.BaseUrl.TrimEnd('/')}/tiingo/daily/{Uri.EscapeDataString(ticker)}/prices" +
                  $"?startDate={from:yyyy-MM-dd}&endDate={to:yyyy-MM-dd}&format=json";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", Options.Token);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw ProviderHttp.ToException(response, ticker, Name);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        var raw = doc.RootElement.EnumerateArray().Select(ParseRaw).OrderBy(r => r.Bar.Date).ToList();

        var result = new List<DailyBar>(raw.Count);
        var cumulative = 1m; // product of split factors of bars strictly after the current one
        for (var i = raw.Count - 1; i >= 0; i--)
        {
            var (bar, splitFactor) = raw[i];
            result.Add(cumulative == 1m ? bar : Adjust(bar, cumulative));
            if (splitFactor > 0m) cumulative *= splitFactor;
        }

        result.Reverse();
        return result;
    }

    private static (DailyBar Bar, decimal SplitFactor) ParseRaw(JsonElement item)
    {
        var date = DateOnly.ParseExact(item.GetProperty("date").GetString()!.AsSpan(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var bar = new DailyBar(
            date,
            item.GetProperty("open").GetDecimal(),
            item.GetProperty("high").GetDecimal(),
            item.GetProperty("low").GetDecimal(),
            item.GetProperty("close").GetDecimal(), // raw close; adjClose is deliberately ignored
            item.TryGetProperty("volume", out var v) ? (long)v.GetDecimal() : 0L);
        var factor = item.TryGetProperty("splitFactor", out var f) && f.ValueKind == JsonValueKind.Number ? f.GetDecimal() : 1m;
        return (bar, factor);
    }

    private static DailyBar Adjust(DailyBar bar, decimal factor) => bar with
    {
        Open = Math.Round(bar.Open / factor, 4),
        High = Math.Round(bar.High / factor, 4),
        Low = Math.Round(bar.Low / factor, 4),
        Close = Math.Round(bar.Close / factor, 4),
        Volume = (long)Math.Round(bar.Volume * factor)
    };
}
