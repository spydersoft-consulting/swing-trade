using Microsoft.EntityFrameworkCore;
using Spydersoft.SwingTrade.Api.Refresh;
using Spydersoft.SwingTrade.Api.UnitTests.Support;
using Spydersoft.SwingTrade.MarketData;

namespace Spydersoft.SwingTrade.Api.UnitTests.Refresh;

[TestFixture]
public class IngestionServiceTests
{
    private static readonly DateOnly Friday = new(2026, 10, 9);
    private static readonly DateOnly Monday = new(2026, 10, 12);

    [Test]
    public async Task RunningTwice_LeavesOneRowPerTickerAndDate()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY", "QQQ");

        await f.Refresh.RunAsync("test");
        var after1 = await f.Db.PriceBars.CountAsync();
        await f.Refresh.RunAsync("test");

        Assert.Multiple(async () =>
        {
            Assert.That(after1, Is.GreaterThan(100));
            Assert.That(await f.Db.PriceBars.CountAsync(), Is.EqualTo(after1));
            var duplicates = (await f.Db.PriceBars.ToListAsync()).GroupBy(b => (b.Ticker, b.Date)).Count(g => g.Count() > 1);
            Assert.That(duplicates, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ACorrectedBarInTheOverlapWindow_OverwritesTheStoredRow()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY");
        var dates = RefreshFixture.WeekdaysEnding(Friday, 60);
        f.Fake.SetBars("SPY", dates.Select(d => RefreshFixture.Bar(d, 100m)));
        await f.Refresh.RunAsync("test");
        f.Db.ChangeTracker.Clear();

        f.Fake.SetBars("SPY", dates.Select(d => RefreshFixture.Bar(d, d == Friday ? 100.07m : 100m)));
        await f.Refresh.RunAsync("test");

        var friday = await f.Db.PriceBars.AsNoTracking().SingleAsync(b => b.Ticker == "SPY" && b.Date == Friday);
        Assert.That(friday.Close, Is.EqualTo(100.07m));
        Assert.That(await f.Db.PriceBars.CountAsync(), Is.EqualTo(60));
        Assert.That((await f.Db.IndicatorSnapshots.SingleAsync(s => s.AsOfDate == Friday)).LastClose, Is.EqualTo(100.07m));
    }

    [Test]
    public async Task OneTickersFailureIsRecordedAndTheOthersComplete()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY", "QQQ", "IWM");
        f.Fake.FailFor("QQQ");

        var run = await f.Refresh.RunAsync("test");

        Assert.Multiple(async () =>
        {
            Assert.That(run!.TickersOk, Is.EqualTo(2));
            Assert.That(run.TickersFailed, Is.EqualTo(1));
            Assert.That(run.Status, Is.EqualTo(RefreshStatus.Partial));
            Assert.That(run.Message, Does.Contain("QQQ"));
            Assert.That(run.FinishedAt, Is.Not.Null);
            Assert.That(await f.Db.PriceBars.CountAsync(b => b.Ticker == "SPY"), Is.GreaterThan(0));
            Assert.That(await f.Db.PriceBars.CountAsync(b => b.Ticker == "IWM"), Is.GreaterThan(0));
            Assert.That(await f.Db.PriceBars.CountAsync(b => b.Ticker == "QQQ"), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task AllTickersFailing_MarksTheRunFailed()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY");
        f.Fake.FailFor("SPY");

        var run = await f.Refresh.RunAsync("test");

        Assert.That(run!.Status, Is.EqualTo(RefreshStatus.Failed));
        Assert.That(run.TickersFailed, Is.EqualTo(1));
    }

    [Test]
    public async Task TodaysBar_IsExcludedBeforeTheCompletionTime_AndIncludedAfter()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY");

        f.SetClock(2026, 10, 12, 15); // 11:00 New York, Monday
        await f.Refresh.RunAsync("test");
        Assert.That(await f.Db.PriceBars.MaxAsync(b => b.Date), Is.EqualTo(Friday), "Monday's intraday bar must not be stored");

        f.Db.ChangeTracker.Clear();
        f.SetClock(2026, 10, 12, 20, 15); // exactly 16:15 New York is not yet complete
        await f.Refresh.RunAsync("test");
        Assert.That(await f.Db.PriceBars.MaxAsync(b => b.Date), Is.EqualTo(Friday));

        f.Db.ChangeTracker.Clear();
        f.SetClock(2026, 10, 12, 21);
        await f.Refresh.RunAsync("test");
        Assert.That(await f.Db.PriceBars.MaxAsync(b => b.Date), Is.EqualTo(Monday));
    }

    [Test]
    public async Task NewTickerBackfills200Days_ThenOnlyTheFiveDayOverlap()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY");

        await f.Refresh.RunAsync("test");
        var first = f.Fake.Calls.Single();
        Assert.That(first.From, Is.EqualTo(Monday.AddDays(-200)));
        Assert.That(first.To, Is.EqualTo(Monday));

        f.Db.ChangeTracker.Clear();
        f.SetClock(2026, 10, 13, 21);
        await f.Refresh.RunAsync("test");

        var second = f.Fake.Calls[1];
        Assert.That(second.From, Is.EqualTo(Monday.AddDays(-5)), "five days before the latest stored bar");
        Assert.That(second.To, Is.EqualTo(new DateOnly(2026, 10, 13)));
    }

    [Test]
    public async Task Snapshot_ForCloses1To50_MatchesTheRulesFixture()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY");
        var dates = RefreshFixture.WeekdaysEnding(Friday, 50);
        f.Fake.SetBars("SPY", dates.Select((d, i) => RefreshFixture.Bar(d, i + 1)));

        await f.Refresh.RunAsync("test");

        var snapshot = await f.Db.IndicatorSnapshots.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.AsOfDate, Is.EqualTo(Friday));
            Assert.That(snapshot.LastClose, Is.EqualTo(50m));
            Assert.That(snapshot.Ma20, Is.EqualTo(40.5m));
            Assert.That(snapshot.Ma50, Is.EqualTo(25.5m));
            Assert.That(snapshot.BarsUsed, Is.EqualTo(50));
        });
    }

    [Test]
    public async Task Snapshot_With49Bars_HasNoMa50()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY");
        f.Fake.SetBars("SPY", RefreshFixture.WeekdaysEnding(Friday, 49).Select((d, i) => RefreshFixture.Bar(d, i + 1)));

        await f.Refresh.RunAsync("test");

        var snapshot = await f.Db.IndicatorSnapshots.SingleAsync();
        Assert.That(snapshot.Ma50, Is.Null);
        Assert.That(snapshot.BarsUsed, Is.EqualTo(49));
    }

    [Test]
    public async Task Snapshot_UsesStoredHistory_WhenOnlyNewBarsAreFetched()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY");
        var dates = RefreshFixture.WeekdaysEnding(Friday, 50);
        f.Fake.SetBars("SPY", dates.Select((d, i) => RefreshFixture.Bar(d, i + 1)));
        await f.Refresh.RunAsync("test");
        f.Db.ChangeTracker.Clear();

        // Monday arrives; the request window only covers the last few days but the averages need 50 stored closes.
        f.Fake.SetBars("SPY", dates.Select((d, i) => RefreshFixture.Bar(d, i + 1)).Append(RefreshFixture.Bar(Monday, 51)));
        await f.Refresh.RunAsync("test");

        var snapshot = await f.Db.IndicatorSnapshots.SingleAsync(s => s.AsOfDate == Monday);
        Assert.That(snapshot.Ma50, Is.EqualTo(Enumerable.Range(2, 50).Average(i => (decimal)i)));
        Assert.That(snapshot.BarsUsed, Is.EqualTo(50));
    }

    [Test]
    public async Task AnExistingTickerWithNoNewBar_IsNotAFailure()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY");
        f.Fake.SetBars("SPY", RefreshFixture.WeekdaysEnding(Friday, 60).Select(d => RefreshFixture.Bar(d, 100m)));
        await f.Refresh.RunAsync("test");
        f.Db.ChangeTracker.Clear();

        f.SetClock(2026, 10, 13, 21); // a holiday: the provider has nothing newer
        var run = await f.Refresh.RunAsync("test");

        Assert.That(run!.Status, Is.EqualTo(RefreshStatus.Succeeded));
        Assert.That(run.TickersFailed, Is.EqualTo(0));
    }

    [Test]
    public async Task ANewTickerThatReturnsNothing_IsAFailure()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY");
        f.Fake.SetBars("SPY", []);

        var run = await f.Refresh.RunAsync("test");

        Assert.That(run!.TickersFailed, Is.EqualTo(1));
        Assert.That(run.Message, Does.Contain("No bars"));
    }

    [Test]
    public async Task OnlyActiveInstrumentsAndTheBenchmarkAreRefreshed()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("QQQ", "XLK", "SPY");
        f.Db.Instruments.Add(new() { Ticker = "XLP", Name = "XLP", Active = false });
        (await f.Db.Instruments.SingleAsync(i => i.Ticker == "XLK")).Active = false;
        (await f.Db.Instruments.SingleAsync(i => i.Ticker == "SPY")).Active = false; // benchmark stays refreshed
        await f.Db.SaveChangesAsync();

        await f.Refresh.RunAsync("test");

        Assert.That(f.Fake.Calls.Select(c => c.Ticker), Is.EquivalentTo(new[] { "SPY", "QQQ" }));
        Assert.That(f.Fake.Calls[0].Ticker, Is.EqualTo("SPY"), "benchmark first");
    }

    [Test]
    public async Task ARestatedHistory_AfterASplit_TriggersAReBackfill()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY");
        var dates = RefreshFixture.WeekdaysEnding(Friday, 120);
        f.Fake.SetBars("SPY", dates.Select(d => RefreshFixture.Bar(d, 100m)));
        await f.Refresh.RunAsync("test");
        f.Db.ChangeTracker.Clear();
        f.Fake.Calls.Clear();

        // 2-for-1 split: the provider now restates the whole history at half price.
        f.Fake.SetBars("SPY", dates.Select(d => RefreshFixture.Bar(d, 50m)));
        var run = await f.Refresh.RunAsync("test");

        Assert.Multiple(async () =>
        {
            Assert.That(f.Fake.Calls, Has.Count.EqualTo(2), "overlap request, then the 200-day re-backfill");
            Assert.That(f.Fake.Calls[1].From, Is.EqualTo(Monday.AddDays(-200)));
            Assert.That(await f.Db.PriceBars.AllAsync(b => b.Close == 50m), Is.True);
            Assert.That(run!.Message, Does.Contain("re-backfilled"));
        });
    }

    [Test]
    public async Task SmallCorrections_DoNotTriggerAReBackfill()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY");
        var dates = RefreshFixture.WeekdaysEnding(Friday, 60);
        f.Fake.SetBars("SPY", dates.Select(d => RefreshFixture.Bar(d, 100m)));
        await f.Refresh.RunAsync("test");
        f.Db.ChangeTracker.Clear();
        f.Fake.Calls.Clear();

        f.Fake.SetBars("SPY", dates.Select(d => RefreshFixture.Bar(d, 100.5m)));
        await f.Refresh.RunAsync("test");

        Assert.That(f.Fake.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task WhenTheFallbackSuppliesBars_TheRunNamesTheProvider()
    {
        using var f = new RefreshFixture(fake => [new DownProvider("alpaca"), fake]);
        await f.AddInstrumentsAsync("SPY", "QQQ");

        var run = await f.Refresh.RunAsync("test");

        Assert.Multiple(() =>
        {
            Assert.That(run!.Provider, Is.EqualTo("fake"));
            Assert.That(run.Message, Does.Contain("SPY via fake").And.Contain("QQQ via fake"));
            Assert.That(run.TickersOk, Is.EqualTo(2));
        });
    }
}
