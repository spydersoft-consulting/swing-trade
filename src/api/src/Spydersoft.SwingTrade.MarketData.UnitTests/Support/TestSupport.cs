using System.Net;
using System.Text;
using Microsoft.Extensions.Options;

namespace Spydersoft.SwingTrade.MarketData.UnitTests.Support;

internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(handler(request));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

internal sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class RecordingDelay : IDelay
{
    public List<TimeSpan> Delays { get; } = [];

    public Task DelayAsync(TimeSpan duration, CancellationToken ct)
    {
        Delays.Add(duration);
        return Task.CompletedTask;
    }
}

internal static class Opts
{
    public static IOptions<MarketDataOptions> Create(Action<MarketDataOptions>? configure = null)
    {
        var o = new MarketDataOptions
        {
            Providers = ["alpaca"],
            Alpaca = { KeyId = "key-id", SecretKey = "secret" },
            Tiingo = { Token = "tok" }
        };
        configure?.Invoke(o);
        return Options.Create(o);
    }
}

/// <summary>A provider whose behaviour per call is scripted.</summary>
internal sealed class ScriptedProvider(string name, Func<int, string, IReadOnlyList<DailyBar>> script, TimeSpan? minDelay = null) : IMarketDataProvider
{
    public int Calls { get; private set; }
    public string Name => name;
    public TimeSpan MinDelayBetweenCalls => minDelay ?? TimeSpan.Zero;

    public Task<IReadOnlyList<DailyBar>> GetDailyBars(string ticker, DateOnly from, DateOnly to, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(script(Calls, ticker));
    }
}
