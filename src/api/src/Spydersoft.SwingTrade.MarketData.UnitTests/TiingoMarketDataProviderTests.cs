using Spydersoft.SwingTrade.MarketData.UnitTests.Support;

namespace Spydersoft.SwingTrade.MarketData.UnitTests;

[TestFixture]
public class TiingoMarketDataProviderTests
{
    // An ex-dividend date: adjClose is below close. The stored Close must be the raw close.
    private const string ExDividend = """
        [{"date":"2026-10-08T00:00:00.000Z","close":100.00,"high":101.0,"low":99.0,"open":99.5,"volume":1000,
          "adjClose":99.40,"adjHigh":100.4,"adjLow":98.4,"adjOpen":98.9,"adjVolume":1000,"divCash":0.0,"splitFactor":1.0},
         {"date":"2026-10-09T00:00:00.000Z","close":101.00,"high":102.0,"low":100.0,"open":100.5,"volume":1200,
          "adjClose":100.60,"adjHigh":101.6,"adjLow":99.6,"adjOpen":100.1,"adjVolume":1200,"divCash":0.6,"splitFactor":1.0}]
        """;

    private static (TiingoMarketDataProvider Provider, StubHttpMessageHandler Handler) Create(string json)
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(json));
        return (new TiingoMarketDataProvider(new HttpClient(handler), Opts.Create()), handler);
    }

    [Test]
    public async Task UsesRawCloseNotAdjClose()
    {
        var (provider, _) = Create(ExDividend);

        var bars = await provider.GetDailyBars("SPY", new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 9), default);

        Assert.Multiple(() =>
        {
            Assert.That(bars.Select(b => b.Close), Is.EqualTo(new[] { 100.00m, 101.00m }), "raw close, never adjClose");
            Assert.That(bars[0].Open, Is.EqualTo(99.5m));
            Assert.That(bars[0].High, Is.EqualTo(101.0m));
            Assert.That(bars[1].Date, Is.EqualTo(new DateOnly(2026, 10, 9)));
        });
    }

    [Test]
    public async Task SendsTokenHeaderAndDateRange()
    {
        var (provider, handler) = Create("[]");

        var bars = await provider.GetDailyBars("QQQ", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 12), default);

        var request = handler.Requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(bars, Is.Empty);
            Assert.That(request.RequestUri!.AbsolutePath, Is.EqualTo("/tiingo/daily/QQQ/prices"));
            Assert.That(request.RequestUri.Query, Does.Contain("startDate=2026-10-01").And.Contain("endDate=2026-10-12").And.Contain("format=json"));
            Assert.That(request.Headers.Authorization!.ToString(), Is.EqualTo("Token tok"));
        });
    }

    [Test]
    public async Task SplitInsideTheWindow_AdjustsEarlierBarsToSplitOnlyBasis()
    {
        // 2-for-1 split effective 10-09: raw prices halve. Earlier bars are divided by the later split factor.
        const string json = """
            [{"date":"2026-10-07T00:00:00.000Z","close":100.0,"high":102.0,"low":98.0,"open":99.0,"volume":1000,"adjClose":50.0,"splitFactor":1.0},
             {"date":"2026-10-08T00:00:00.000Z","close":102.0,"high":104.0,"low":100.0,"open":101.0,"volume":1000,"adjClose":51.0,"splitFactor":1.0},
             {"date":"2026-10-09T00:00:00.000Z","close":52.0,"high":53.0,"low":50.0,"open":51.0,"volume":2000,"adjClose":52.0,"splitFactor":2.0}]
            """;
        var (provider, _) = Create(json);

        var bars = await provider.GetDailyBars("XYZ", new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 9), default);

        Assert.Multiple(() =>
        {
            Assert.That(bars.Select(b => b.Close), Is.EqualTo(new[] { 50.0m, 51.0m, 52.0m }));
            Assert.That(bars[0].High, Is.EqualTo(51.0m));
            Assert.That(bars[0].Volume, Is.EqualTo(2000));
        });
    }

    [Test]
    public async Task UnknownTicker_IsTickerNotFound()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        var provider = new TiingoMarketDataProvider(new HttpClient(handler), Opts.Create());
        await Assert.ThrowsAsync<TickerNotFoundException>(() => provider.GetDailyBars("ZZZ", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), default));
    }
}
