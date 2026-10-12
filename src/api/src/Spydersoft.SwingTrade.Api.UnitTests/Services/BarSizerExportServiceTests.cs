using Spydersoft.SwingTrade.Api.Services;
using Spydersoft.SwingTrade.Api.UnitTests.Support;
using Spydersoft.SwingTrade.Contracts.Bars;
using Spydersoft.SwingTrade.Contracts.Sizer;
using Spydersoft.SwingTrade.Contracts.Trades;
using Spydersoft.SwingTrade.Data.Entities;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Api.UnitTests.Services;

[TestFixture]
public class BarServiceTests
{
    private TestHarness _ctx = null!;

    [SetUp]
    public async Task SetUp() => _ctx = await TestHarness.SeededAsync();

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    private async Task AddBars(string ticker, int count, DateOnly last)
    {
        var dates = RefreshFixture.WeekdaysEnding(last, count);
        for (var i = 0; i < dates.Count; i++)
            _ctx.Db.PriceBars.Add(new PriceBar { Ticker = ticker, Date = dates[i], Open = i + 1, High = i + 1, Low = i + 1, Close = i + 1, Volume = 10 });
        await _ctx.Db.SaveChangesAsync();
    }

    [Test]
    public async Task ReturnsOldestFirst_WithMovingAveragesFromTheRulesLibrary()
    {
        await AddBars("SPY", 150, new DateOnly(2026, 10, 12));

        var result = await _ctx.Bars.GetAsync("spy", new BarsQuery());

        var bars = result.Value!.Bars;
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Ticker, Is.EqualTo("SPY"));
            Assert.That(bars, Has.Count.EqualTo(120));
            Assert.That(bars[0].Date, Is.LessThan(bars[^1].Date));
            Assert.That(bars[^1].Close, Is.EqualTo(150m));
            Assert.That(bars[^1].Ma20, Is.EqualTo(140.5m));
            Assert.That(bars[^1].Ma50, Is.EqualTo(125.5m));
            // History before the window feeds the first bar's averages.
            Assert.That(bars[0].Close, Is.EqualTo(31m));
            Assert.That(bars[0].Ma20, Is.EqualTo(21.5m));
            Assert.That(bars[0].Ma50, Is.Null);
            Assert.That(bars[19].Ma50, Is.EqualTo(25.5m));
        });
    }

    [Test]
    public async Task FewerThanNBars_HaveNullAverages()
    {
        await AddBars("SPY", 30, new DateOnly(2026, 10, 12));

        var bars = (await _ctx.Bars.GetAsync("SPY", new BarsQuery { Days = 120 })).Value!.Bars;

        Assert.Multiple(() =>
        {
            Assert.That(bars, Has.Count.EqualTo(30));
            Assert.That(bars[18].Ma20, Is.Null);
            Assert.That(bars[19].Ma20, Is.EqualTo(10.5m));
            Assert.That(bars.All(b => b.Ma50 is null), Is.True);
        });
    }

    [Test]
    public async Task DaysAboveTheCap_AreClampedTo120_AndSmallerCountsHonoured()
    {
        await AddBars("SPY", 200, new DateOnly(2026, 10, 12));

        var capped = await _ctx.Bars.GetAsync("SPY", new BarsQuery { Days = 500 });
        var ten = await _ctx.Bars.GetAsync("SPY", new BarsQuery { Days = 10 });

        Assert.That(capped.Value!.Bars, Has.Count.EqualTo(120));
        Assert.That(capped.Value.Days, Is.EqualTo(120));
        Assert.That(ten.Value!.Bars, Has.Count.EqualTo(10));
    }

    [Test]
    public async Task ABarThatIsNotCompletedYet_IsExcluded()
    {
        await AddBars("SPY", 60, new DateOnly(2026, 10, 12));
        _ctx.Clock.UtcNow = new DateTimeOffset(2026, 10, 12, 15, 0, 0, TimeSpan.Zero); // 11:00 in New York

        var bars = (await _ctx.Bars.GetAsync("SPY", new BarsQuery())).Value!.Bars;

        Assert.That(bars[^1].Date, Is.EqualTo(new DateOnly(2026, 10, 9)));
    }

    [Test]
    public async Task UnknownTicker_IsNotFound_AndBadDaysIsInvalid()
    {
        var missing = await _ctx.Bars.GetAsync("NOPE", new BarsQuery());
        var bad = await _ctx.Bars.GetAsync("SPY", new BarsQuery { Days = 0 });

        Assert.That(missing.Kind, Is.EqualTo(ResultKind.NotFound));
        Assert.That(bad.Kind, Is.EqualTo(ResultKind.Invalid));
        Assert.That(bad.Errors.Keys, Does.Contain("Days"));
    }

    [Test]
    public async Task InstrumentWithNoBars_ReturnsAnEmptyList()
    {
        var result = await _ctx.Bars.GetAsync("GLD", new BarsQuery());

        Assert.That(result.Value!.Bars, Is.Empty);
    }
}

[TestFixture]
public class SizerServiceTests
{
    private TestHarness _ctx = null!;

    [SetUp]
    public async Task SetUp() => _ctx = await TestHarness.SeededAsync();

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    private async Task<SizerResultDto> Size(decimal entry, decimal stop, decimal target, string? ticker = null)
    {
        var result = await _ctx.Sizer.SizeAsync(new SizerRequest { Ticker = ticker, Entry = entry, Stop = stop, Target = target });
        Assert.That(result.IsOk, Is.True);
        return result.Value!;
    }

    [TestCase(62.82, 61.40, 65.90, 15, 942.30, 21.30, 2.169, 62.90, "OK - log it")]
    [TestCase(769.64, 753.50, 773.50, 1, 769.64, 16.14, 0.239, 760.17, "SKIP - reward:risk below minimum")]
    [TestCase(192.00, 187.50, 202.00, 5, 960.00, 22.50, 2.222, 192.33, "OK - log it")]
    [TestCase(196.30, 193.50, 202.00, 5, 981.50, 14.00, 2.036, 196.33, "OK - log it")]
    [TestCase(196.60, 193.50, 202.00, 5, 983.00, 15.50, 1.742, 196.33, "SKIP - reward:risk below minimum")]
    [TestCase(62.34, 61.40, 65.90, 16, 997.44, 15.04, 3.787, 62.90, "OK - log it")]
    public async Task PlanFixtures(double entry, double stop, double target, int shares, double cost, double risk, double rr, double maxEntry, string verdict)
    {
        var r = await Size((decimal)entry, (decimal)stop, (decimal)target);

        Assert.Multiple(() =>
        {
            Assert.That(r.Shares, Is.EqualTo(shares));
            Assert.That(r.PositionCost, Is.EqualTo((decimal)cost));
            Assert.That(r.RiskDollars, Is.EqualTo((decimal)risk));
            Assert.That(Math.Round(r.RewardRisk!.Value, 3), Is.EqualTo((decimal)rr));
            Assert.That(Math.Round(r.MaxEntryForMinRewardRisk, 2), Is.EqualTo((decimal)maxEntry));
            Assert.That(r.Verdict, Is.EqualTo(verdict));
            Assert.That(r.IsOk, Is.EqualTo(verdict == PositionSizer.VerdictOk));
        });
    }

    [Test]
    public async Task AvailableCash_ReservesPlannedCost_SoCashSetsTheSize()
    {
        // Seeded cash is 2,053.55; a planned 1 share at 1,553.55 reserves it down to exactly 500.
        var planned = await _ctx.Trades.CreateAsync(new CreateTradeRequest
        {
            Ticker = "SPY", Shares = 1, EntryPrice = 1553.55m, StopPrice = 1500m, TargetPrice = 1700m
        });
        Assert.That(planned.IsOk, Is.True);

        var r = await Size(62.34m, 61.40m, 65.90m);

        Assert.Multiple(() =>
        {
            Assert.That(r.AvailableCash, Is.EqualTo(500m));
            Assert.That(r.SharesByCash, Is.EqualTo(8));
            Assert.That(r.Shares, Is.EqualTo(8));
        });
    }

    [Test]
    public async Task NotEnoughCash_IsASkipVerdict()
    {
        await _ctx.Trades.CreateAsync(new CreateTradeRequest
        {
            Ticker = "SPY", Shares = 1, EntryPrice = 2053m, StopPrice = 2000m, TargetPrice = 2200m
        });

        var r = await Size(62.34m, 61.40m, 65.90m);

        Assert.That(r.Verdict, Is.EqualTo(PositionSizer.VerdictCash));
    }

    [Test]
    public async Task StopAtOrAboveEntry_IsAVerdictNotAnError()
    {
        var r = await Size(100m, 100m, 110m);

        Assert.Multiple(() =>
        {
            Assert.That(r.Verdict, Is.EqualTo("Stop must be below entry"));
            Assert.That(r.Shares, Is.EqualTo(0));
            Assert.That(r.RewardRisk, Is.Null);
        });
    }

    [Test]
    public async Task ThreeOpenPositions_AddAWarning_WithoutChangingTheVerdict()
    {
        for (var i = 0; i < 3; i++)
        {
            await _ctx.Trades.CreateAsync(new CreateTradeRequest
            {
                Ticker = "SPY", Status = TradeStatus.Open, OpenDate = new DateOnly(2026, 10, 9), Shares = 1,
                EntryPrice = 10m, StopPrice = 9m, TargetPrice = 12m
            });
        }

        var r = await Size(62.34m, 61.40m, 65.90m);

        Assert.Multiple(() =>
        {
            Assert.That(r.OpenCount, Is.EqualTo(3));
            Assert.That(r.Warning, Is.Not.Null);
            Assert.That(r.Verdict, Is.EqualTo(PositionSizer.VerdictOk));
        });
    }

    [Test]
    public async Task Ticker_IsNormalisedAndEchoed_AndUnknownIsAFieldError()
    {
        var ok = await Size(62.82m, 61.40m, 65.90m, " xle ");
        var bad = await _ctx.Sizer.SizeAsync(new SizerRequest { Ticker = "NOPE", Entry = 10m, Stop = 9m, Target = 12m });

        Assert.That(ok.Ticker, Is.EqualTo("XLE"));
        Assert.That(bad.Kind, Is.EqualTo(ResultKind.Invalid));
        Assert.That(bad.Errors.Keys, Is.EqualTo(new[] { "Ticker" }));
    }

    [Test]
    public async Task NonPositivePrices_AreFieldErrors()
    {
        var bad = await _ctx.Sizer.SizeAsync(new SizerRequest());

        Assert.That(bad.Errors.Keys, Is.EquivalentTo(new[] { "Entry", "Stop", "Target" }));
    }

    [Test]
    public async Task SavesNothing()
    {
        var before = _ctx.Db.Trades.Count();

        await Size(62.82m, 61.40m, 65.90m);

        Assert.That(_ctx.Db.Trades.Count(), Is.EqualTo(before));
    }
}

[TestFixture]
public class ExportServiceTests
{
    private TestHarness _ctx = null!;

    [SetUp]
    public async Task SetUp() => _ctx = await TestHarness.SeededAsync();

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task SeededTradeLog_HasHeaderAndTheComputedFixtureRow()
    {
        var csv = await _ctx.Export.TradesCsvAsync();

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Multiple(() =>
        {
            Assert.That(lines[0], Is.EqualTo(string.Join(',', ExportService.Columns)));
            Assert.That(lines, Has.Length.EqualTo(2));
            Assert.That(lines[1], Does.StartWith("1,XLE,Closed,Pullback,2026-10-05,15,62.34,61.40,65.90,65.91,2026-10-12,935.10,14.10,0.007050,3.787,53.55,0.057267,3.798,7,false,true,"));
            Assert.That(csv, Does.EndWith("\r\n"));
        });
    }

    [Test]
    public async Task UsesInvariantCulture_WhateverTheThreadCulture()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var csv = await _ctx.Export.TradesCsvAsync();

            Assert.That(csv, Does.Contain("62.34").And.Contain("53.55"));
            Assert.That(csv, Does.Not.Contain("53,55"));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [TestCase("plain", "plain")]
    [TestCase("a,b", "\"a,b\"")]
    [TestCase("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [TestCase("line1\nline2", "\"line1\nline2\"")]
    [TestCase("cr\rhere", "\"cr\rhere\"")]
    [TestCase("", "")]
    public void Escape_FollowsRfc4180(string input, string expected) =>
        Assert.That(ExportService.Escape(input), Is.EqualTo(expected));

    [Test]
    public async Task FreeTextWithCommasAndQuotes_IsQuoted()
    {
        await _ctx.Trades.CreateAsync(new CreateTradeRequest
        {
            Ticker = "XLK", Shares = 5, EntryPrice = 192m, StopPrice = 187.5m, TargetPrice = 202m,
            Rationale = "Dip, \"buy\" it"
        });

        var csv = await _ctx.Export.TradesCsvAsync();

        Assert.That(csv, Does.Contain("\"Dip, \"\"buy\"\" it\""));
    }

    [Test]
    public async Task WashSaleFlag_AppearsInTheExport()
    {
        await _ctx.AddClosedAsync("XLK", entry: 100m, exit: 95m, open: new DateOnly(2026, 9, 20), close: new DateOnly(2026, 10, 5));
        await _ctx.Trades.CreateAsync(new CreateTradeRequest
        {
            Ticker = "XLK", Status = TradeStatus.Open, OpenDate = new DateOnly(2026, 10, 9), Shares = 1,
            EntryPrice = 96m, StopPrice = 90m, TargetPrice = 110m
        });

        var csv = await _ctx.Export.TradesCsvAsync();

        Assert.That(csv.Split("\r\n").Count(l => l.Contains(",XLK,Open,") && l.Contains(",true,")), Is.EqualTo(1));
    }

    [Test]
    public void FileName_UsesTheNewYorkDate()
    {
        _ctx.Clock.UtcNow = new DateTimeOffset(2026, 10, 13, 2, 0, 0, TimeSpan.Zero); // still the 12th in New York

        Assert.That(_ctx.Export.FileName(), Is.EqualTo("swing-trade-trades-20261012.csv"));
    }
}
