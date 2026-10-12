using Spydersoft.SwingTrade.Api.UnitTests.Support;
using Spydersoft.SwingTrade.Contracts.Settings;
using Spydersoft.SwingTrade.Contracts.Watchlist;
using Spydersoft.SwingTrade.Data.Entities;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Api.UnitTests.Services;

[TestFixture]
public class WatchlistServiceTests
{
    private TestHarness _ctx = null!;

    private static readonly DateOnly Today = new(2026, 10, 12);

    [SetUp]
    public async Task SetUp()
    {
        _ctx = await TestHarness.SeededAsync();
        // Fixture rows from the plan: SPY pullback candidate, QQQ watch (+2.995%), XLV avoid, XLK extended-ish.
        await Snapshot("SPY", Today, 769.64m, 764.83m, 763.70m);
        await Snapshot("QQQ", Today, 749.58m, 727.78m, 716.82m);
        await Snapshot("XLV", Today, 166.18m, 168.38m, 168.53m);
        await Snapshot("XLE", Today.AddDays(-7), 62.82m, 63.47m, 62.12m);
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    private async Task Snapshot(string ticker, DateOnly asOf, decimal last, decimal ma20, decimal ma50, int bars = 50)
    {
        _ctx.Db.IndicatorSnapshots.Add(new IndicatorSnapshot
        {
            Ticker = ticker, AsOfDate = asOf, LastClose = last, Ma20 = ma20, Ma50 = ma50, BarsUsed = bars
        });
        await _ctx.Db.SaveChangesAsync();
    }

    private async Task<List<WatchlistRowDto>> Get(WatchlistQuery? query = null)
    {
        var result = await _ctx.Watchlist.GetAsync(query ?? new WatchlistQuery());
        Assert.That(result.IsOk, Is.True);
        return result.Value!;
    }

    [Test]
    public async Task Returns_OneRowPerActiveTicker_WithRulesLibraryTrendAndSignal()
    {
        var rows = await Get();

        Assert.That(rows, Has.Count.EqualTo(12));
        var spy = rows.Single(r => r.Ticker == "SPY");
        var qqq = rows.Single(r => r.Ticker == "QQQ");
        var xlv = rows.Single(r => r.Ticker == "XLV");
        Assert.Multiple(() =>
        {
            Assert.That(spy.Signal, Is.EqualTo(Signal.PullbackCandidate));
            Assert.That(spy.SignalText, Is.EqualTo("Pullback candidate"));
            Assert.That(spy.Trend, Is.EqualTo(Trend.Uptrend));
            Assert.That(Math.Round(spy.PctVs20!.Value * 100m, 3), Is.EqualTo(0.629m));
            Assert.That(spy.AsOfDate, Is.EqualTo(Today));
            Assert.That(spy.Stale, Is.False);
            Assert.That(qqq.Signal, Is.EqualTo(Signal.Watch));
            Assert.That(xlv.Signal, Is.EqualTo(Signal.Avoid));
            Assert.That(xlv.TrendText, Is.EqualTo("Downtrend"));
        });
    }

    [Test]
    public async Task TickerWithoutASnapshot_IsInsufficientDataAndStale()
    {
        var gld = (await Get()).Single(r => r.Ticker == "GLD");

        Assert.Multiple(() =>
        {
            Assert.That(gld.AsOfDate, Is.Null);
            Assert.That(gld.Last, Is.Null);
            Assert.That(gld.Signal, Is.EqualTo(Signal.InsufficientData));
            Assert.That(gld.Stale, Is.True);
        });
    }

    [Test]
    public async Task OldAsOfDate_IsStale_AndTodayIsNot()
    {
        var rows = await Get();

        Assert.That(rows.Single(r => r.Ticker == "XLE").Stale, Is.True);
        Assert.That(rows.Single(r => r.Ticker == "SPY").Stale, Is.False);
    }

    [Test]
    public async Task UsesTheLatestSnapshotPerTicker()
    {
        await Snapshot("SPY", Today.AddDays(-3), 700m, 700m, 700m);

        var spy = (await Get()).Single(r => r.Ticker == "SPY");

        Assert.That(spy.AsOfDate, Is.EqualTo(Today));
    }

    [Test]
    public async Task InactiveTickers_AreNotListed()
    {
        var rows = await Get();

        Assert.That(rows.Select(r => r.Ticker), Does.Not.Contain("XLP"));
    }

    [Test]
    public async Task DefaultSort_PutsPullbackCandidatesFirst_ThenTicker()
    {
        var tickers = (await Get()).Select(r => r.Ticker).ToList();

        Assert.That(tickers[0], Is.EqualTo("SPY"));
        Assert.That(tickers.Skip(1), Is.EqualTo(tickers.Skip(1).OrderBy(t => t, StringComparer.Ordinal)));
    }

    [TestCase("PullbackCandidate")]
    [TestCase("Pullback candidate")]
    [TestCase("pullback CANDIDATE")]
    public async Task SignalFilter_AcceptsNameOrDisplayText(string filter)
    {
        var rows = await Get(new WatchlistQuery { Signal = filter });

        Assert.That(rows.Select(r => r.Ticker), Is.EqualTo(new[] { "SPY" }));
    }

    [Test]
    public async Task SignalFilter_ExtendedWaitDisplayTextWithDash()
    {
        await Snapshot("IWM", Today, 200m, 190m, 185m);

        var rows = await Get(new WatchlistQuery { Signal = "Extended - wait" });

        Assert.That(rows.Select(r => r.Ticker), Is.EqualTo(new[] { "IWM" }));
    }

    [Test]
    public async Task UnknownSignalOrSort_Is400WithFieldErrors()
    {
        var bad = await _ctx.Watchlist.GetAsync(new WatchlistQuery { Signal = "moon", Sort = "sideways" });

        Assert.That(bad.Kind, Is.EqualTo(Spydersoft.SwingTrade.Api.Services.ResultKind.Invalid));
        Assert.That(bad.Errors.Keys, Is.EquivalentTo(new[] { "Signal", "Sort" }));
    }

    [Test]
    public async Task Sort_ByPctVs20Descending_PutsNullsLast()
    {
        var rows = await Get(new WatchlistQuery { Sort = "-pctVs20" });

        Assert.That(rows.First().Ticker, Is.EqualTo("QQQ"));
        Assert.That(rows.Last().PctVs20, Is.Null);
    }

    [Test]
    public async Task Sort_ByTickerDescending()
    {
        var rows = await Get(new WatchlistQuery { Sort = "-ticker" });

        Assert.That(rows.First().Ticker, Is.EqualTo("XLY"));
    }

    [Test]
    public async Task ThresholdChange_AppliesOnTheNextCall_WithoutARefresh()
    {
        Assert.That((await Get()).Single(r => r.Ticker == "QQQ").Signal, Is.EqualTo(Signal.Watch));

        var s = await _ctx.Settings.GetAsync();
        var update = new UpdateSettingsRequest
        {
            StartingCapital = s.StartingCapital, MaxRiskPct = s.MaxRiskPct, MaxOpenPositions = s.MaxOpenPositions,
            MinRewardRisk = s.MinRewardRisk, TaxSetAsideRate = s.TaxSetAsideRate, WashSaleWindowDays = s.WashSaleWindowDays,
            MaxPositionPct = s.MaxPositionPct, PullbackBandLow = s.PullbackBandLow, PullbackBandHigh = 0.04m,
            ExtendedThreshold = s.ExtendedThreshold, RiskBasis = s.RiskBasis
        };
        Assert.That((await _ctx.Settings.UpdateAsync(update)).IsOk, Is.True);

        Assert.That((await Get()).Single(r => r.Ticker == "QQQ").Signal, Is.EqualTo(Signal.PullbackCandidate));
    }

    [Test]
    public async Task ReentryOkAfter_ComesFromALosingClosedTrade()
    {
        await _ctx.AddClosedAsync("XLE", entry: 62m, exit: 60m, open: new DateOnly(2026, 9, 20), close: new DateOnly(2026, 10, 5));

        var rows = await Get();

        Assert.That(rows.Single(r => r.Ticker == "XLE").ReentryOkAfter, Is.EqualTo(new DateOnly(2026, 11, 5)));
        Assert.That(rows.Single(r => r.Ticker == "SPY").ReentryOkAfter, Is.Null);
    }
}
