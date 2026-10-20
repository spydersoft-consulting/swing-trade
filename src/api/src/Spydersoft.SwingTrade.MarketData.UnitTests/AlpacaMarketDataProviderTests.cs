using System.Net;
using System.Web;
using Spydersoft.SwingTrade.MarketData.UnitTests.Support;

namespace Spydersoft.SwingTrade.MarketData.UnitTests;

[TestFixture]
public class AlpacaMarketDataProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 22, 0, 0, TimeSpan.Zero);

    private static (AlpacaMarketDataProvider Provider, StubHttpMessageHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond, DateTimeOffset? now = null)
    {
        var handler = new StubHttpMessageHandler(respond);
        return (new AlpacaMarketDataProvider(new HttpClient(handler), Opts.Create(), new FakeTimeProvider(now ?? Now)), handler);
    }

    private const string Page1 = """
        {"bars":[
          {"t":"2026-10-08T04:00:00Z","o":10.0,"h":11.0,"l":9.5,"c":10.5,"v":1000,"n":5,"vw":10.2},
          {"t":"2026-10-09T04:00:00Z","o":10.5,"h":11.5,"l":10.0,"c":11.0,"v":2000,"n":5,"vw":10.9}],
         "symbol":"SPY","next_page_token":"abc=="}
        """;

    private const string Page2 = """
        {"bars":[{"t":"2026-10-12T04:00:00Z","o":11.0,"h":12.0,"l":10.8,"c":11.75,"v":3000,"n":5,"vw":11.5}],
         "symbol":"SPY","next_page_token":null}
        """;

    [Test]
    public async Task SendsSplitAdjustedSipDailyRequestWithCredentials()
    {
        var (provider, handler) = Create(_ => StubHttpMessageHandler.Json("""{"bars":[],"next_page_token":null}"""));

        await provider.GetDailyBars("SPY", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9), default);

        var request = handler.Requests.Single();
        var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
        Assert.Multiple(() =>
        {
            Assert.That(request.RequestUri!.AbsolutePath, Is.EqualTo("/v2/stocks/SPY/bars"));
            Assert.That(query["timeframe"], Is.EqualTo("1Day"));
            Assert.That(query["adjustment"], Is.EqualTo("split"));
            Assert.That(query["feed"], Is.EqualTo("sip"));
            Assert.That(query["start"], Is.EqualTo("2026-10-01T04:00:00Z"));   // midnight New York (EDT) in UTC
            Assert.That(query["end"], Is.EqualTo("2026-10-10T04:00:00Z"));
            Assert.That(request.Headers.GetValues("APCA-API-KEY-ID").Single(), Is.EqualTo("key-id"));
            Assert.That(request.Headers.GetValues("APCA-API-SECRET-KEY").Single(), Is.EqualTo("secret"));
        });
    }

    [Test]
    public async Task FollowsNextPageTokenAndParsesNewYorkDates()
    {
        var (provider, handler) = Create(r =>
            HttpUtility.ParseQueryString(r.RequestUri!.Query)["page_token"] is null
                ? StubHttpMessageHandler.Json(Page1)
                : StubHttpMessageHandler.Json(Page2));

        var bars = await provider.GetDailyBars("SPY", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 12), default);

        Assert.Multiple(() =>
        {
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
            Assert.That(HttpUtility.ParseQueryString(handler.Requests[1].RequestUri!.Query)["page_token"], Is.EqualTo("abc=="));
            Assert.That(bars.Select(b => b.Date), Is.EqualTo(new[] { new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 12) }));
            Assert.That(bars[1], Is.EqualTo(new DailyBar(new DateOnly(2026, 10, 9), 10.5m, 11.5m, 10.0m, 11.0m, 2000)));
        });
    }

    [Test]
    public async Task KeepsEndAtLeastFifteenMinutesInThePast()
    {
        // 20:00 UTC is 16:00 New York; asking through today must not ask for data newer than now - 15 minutes.
        var now = new DateTimeOffset(2026, 10, 12, 20, 0, 0, TimeSpan.Zero);
        var (provider, handler) = Create(_ => StubHttpMessageHandler.Json("""{"bars":[]}"""), now);

        await provider.GetDailyBars("SPY", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12), default);

        var end = DateTimeOffset.Parse(HttpUtility.ParseQueryString(handler.Requests.Single().RequestUri!.Query)["end"]!);
        Assert.That(now - end, Is.GreaterThanOrEqualTo(TimeSpan.FromMinutes(15)));
    }

    [Test]
    public async Task MakesNoCallWhenTheWholeWindowIsInTheFuture()
    {
        var (provider, handler) = Create(_ => StubHttpMessageHandler.Json("""{"bars":[]}"""));

        var bars = await provider.GetDailyBars("SPY", new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 21), default);

        Assert.That(bars, Is.Empty);
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task TooManyRequests_IsRetryableAndKeepsRetryAfter()
    {
        var (provider, _) = Create(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return response;
        });

        var ex = await Assert.ThrowsAsync<ProviderUnavailableException>(() =>
            provider.GetDailyBars("SPY", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9), default));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.Retryable, Is.True);
            Assert.That(ex.RetryAfter, Is.EqualTo(TimeSpan.FromSeconds(7)));
        });
    }

    [Test]
    public async Task Unauthorized_IsNotRetryable()
    {
        var (provider, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var ex = await Assert.ThrowsAsync<ProviderUnavailableException>(() =>
            provider.GetDailyBars("SPY", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9), default));
        Assert.That(ex!.Retryable, Is.False);
    }

    [Test]
    public async Task UnknownSymbol_IsTickerNotFound()
    {
        var (provider, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        await Assert.ThrowsAsync<TickerNotFoundException>(() =>
            provider.GetDailyBars("ZZZZ", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9), default));
    }

    [Test]
    public async Task NetworkFailure_IsWrappedAsUnavailable()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("boom"));
        var provider = new AlpacaMarketDataProvider(new HttpClient(handler), Opts.Create(), new FakeTimeProvider(Now));
        await Assert.ThrowsAsync<ProviderUnavailableException>(() =>
            provider.GetDailyBars("SPY", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9), default));
    }

    [Test]
    public void ConfiguredMinDelayIsHonoured()
    {
        var options = Opts.Create(o => { o.Alpaca.Feed = "iex"; o.Alpaca.MinDelayMs = 250; });
        var provider = new AlpacaMarketDataProvider(new HttpClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("{}"))), options, new FakeTimeProvider(Now));
        Assert.That(provider.MinDelayBetweenCalls, Is.EqualTo(TimeSpan.FromMilliseconds(250)));
    }
}
