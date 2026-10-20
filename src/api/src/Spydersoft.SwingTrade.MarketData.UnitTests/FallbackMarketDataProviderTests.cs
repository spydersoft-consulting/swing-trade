using Spydersoft.SwingTrade.MarketData.UnitTests.Support;

namespace Spydersoft.SwingTrade.MarketData.UnitTests;

[TestFixture]
public class FallbackMarketDataProviderTests
{
    private static readonly DateOnly D = new(2026, 10, 9);
    private static DailyBar Bar(DateOnly d, decimal close = 10m) => new(d, close, close, close, close, 1);

    private static FallbackMarketDataProvider Create(IReadOnlyList<IMarketDataProvider> providers, RecordingDelay? delay = null, Action<MarketDataOptions>? cfg = null) =>
        new(providers, Opts.Create(cfg), delay ?? new RecordingDelay(), new FakeTimeProvider(DateTimeOffset.UnixEpoch));

    private static ScriptedProvider Ok(string name, params DailyBar[] bars) => new(name, (_, _) => bars);
    private static ScriptedProvider Down(string name, bool retryable = false) =>
        new(name, (_, _) => throw new ProviderUnavailableException($"{name} down", retryable));

    [Test]
    public async Task UsesProvidersInConfiguredOrder_AndStopsAtFirstSuccess()
    {
        var first = Ok("alpaca", Bar(D));
        var second = Ok("tiingo", Bar(D));

        var result = await Create([first, second]).FetchAsync("SPY", D, D, null, default);

        Assert.Multiple(() =>
        {
            Assert.That(result.Provider, Is.EqualTo("alpaca"));
            Assert.That(first.Calls, Is.EqualTo(1));
            Assert.That(second.Calls, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task FallsThroughWhenFirstIsUnavailable_AndRecordsTheSupplier()
    {
        var second = Ok("tiingo", Bar(D, 11m));

        var result = await Create([Down("alpaca"), second]).FetchAsync("SPY", D, D, null, default);

        Assert.Multiple(() =>
        {
            Assert.That(result.Provider, Is.EqualTo("tiingo"));
            Assert.That(result.Bars.Single().Close, Is.EqualTo(11m));
        });
    }

    [Test]
    public async Task FallsThroughOnUnknownTicker()
    {
        var notFound = new ScriptedProvider("alpaca", (_, t) => throw new TickerNotFoundException(t, "alpaca"));
        var result = await Create([notFound, Ok("tiingo", Bar(D))]).FetchAsync("SPY", D, D, null, default);
        Assert.That(result.Provider, Is.EqualTo("tiingo"));
    }

    [Test]
    public async Task LaggingFirstProvider_IsSupersededWhenTheNextHasTheExpectedBar()
    {
        var lagging = Ok("alpaca", Bar(D.AddDays(-1)));
        var current = Ok("tiingo", Bar(D.AddDays(-1)), Bar(D));

        var result = await Create([lagging, current]).FetchAsync("SPY", D.AddDays(-5), D, D, default);

        Assert.That(result.Provider, Is.EqualTo("tiingo"));
    }

    [Test]
    public async Task WhenNoProviderHasTheExpectedBar_TheNewestResultIsReturned()
    {
        var a = Ok("alpaca", Bar(D.AddDays(-2)));
        var b = Ok("tiingo", Bar(D.AddDays(-1)));

        var result = await Create([a, b]).FetchAsync("SPY", D.AddDays(-5), D, D, default);

        Assert.That(result.Provider, Is.EqualTo("tiingo"));
    }

    [Test]
    public async Task EmptyAnswerIsAcceptedWhenNoBarIsExpected()
    {
        var first = Ok("alpaca");
        var second = Ok("tiingo", Bar(D));

        var result = await Create([first, second]).FetchAsync("SPY", D, D, null, default);

        Assert.Multiple(() =>
        {
            Assert.That(result.Provider, Is.EqualTo("alpaca"));
            Assert.That(result.Bars, Is.Empty);
        });
    }

    [Test]
    public async Task AllProvidersFailing_ThrowsWithEachReason()
    {
        var ex = await Assert.ThrowsAsync<AllProvidersFailedException>(() =>
            Create([Down("alpaca"), Down("tiingo")]).FetchAsync("SPY", D, D, null, default));
        Assert.That(ex!.Failures, Has.Count.EqualTo(2));
        Assert.That(ex.Message, Does.Contain("alpaca down").And.Contain("tiingo down"));
    }

    [Test]
    public async Task RetriesUpToThreeTimesWithBackoff()
    {
        var delay = new RecordingDelay();
        var flaky = new ScriptedProvider("alpaca", (call, _) =>
            call < 3 ? throw new ProviderUnavailableException("429") : [Bar(D)]);

        var result = await Create([flaky], delay).FetchAsync("SPY", D, D, null, default);

        Assert.Multiple(() =>
        {
            Assert.That(flaky.Calls, Is.EqualTo(3));
            Assert.That(result.Bars, Has.Count.EqualTo(1));
            Assert.That(delay.Delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }));
        });
    }

    [Test]
    public async Task GivesUpAfterThreeAttempts()
    {
        var alwaysDown = Down("alpaca", retryable: true);
        await Assert.ThrowsAsync<AllProvidersFailedException>(() => Create([alwaysDown]).FetchAsync("SPY", D, D, null, default));
        Assert.That(alwaysDown.Calls, Is.EqualTo(3));
    }

    [Test]
    public async Task NonRetryableFailureIsNotRetried()
    {
        var down = Down("alpaca", retryable: false);
        await Assert.ThrowsAsync<AllProvidersFailedException>(() => Create([down]).FetchAsync("SPY", D, D, null, default));
        Assert.That(down.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task SpacesCallsToTheSameProviderByItsMinDelay()
    {
        var delay = new RecordingDelay();
        var provider = new ScriptedProvider("tiingo", (_, _) => [Bar(D)], TimeSpan.FromSeconds(1));
        var fallback = Create([provider], delay);

        await fallback.FetchAsync("SPY", D, D, null, default);
        await fallback.FetchAsync("QQQ", D, D, null, default);

        // The fake clock never advances, so the second call waits the full minimum delay; the first waits for nothing.
        Assert.That(delay.Delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(1) }));
    }

    [Test]
    public void ProviderChain_ListsNamesInOrder() =>
        Assert.That(Create([Ok("alpaca"), Ok("tiingo")]).ProviderChain, Is.EqualTo("alpaca>tiingo"));
}
