using TestHarness = Spydersoft.SwingTrade.Api.UnitTests.Support.TestHarness;
using Microsoft.EntityFrameworkCore;
using Spydersoft.SwingTrade.Api.UnitTests.Support;
using Spydersoft.SwingTrade.Contracts.Trades;
using Spydersoft.SwingTrade.Data;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Api.UnitTests.Services;

[TestFixture]
public class SeedAndDashboardTests
{
    [Test]
    public async Task Seed_CreatesSettingsInstrumentsAndTheXleTrade()
    {
        using var ctx = await TestHarness.SeededAsync();

        Assert.That(await ctx.Db.Settings.CountAsync(), Is.EqualTo(1));
        Assert.That(await ctx.Db.Instruments.CountAsync(i => i.Active), Is.EqualTo(12));
        Assert.That(await ctx.Db.Instruments.CountAsync(i => !i.Active), Is.EqualTo(12));
        Assert.That(await ctx.Db.Instruments.Where(i => i.IsBenchmark).Select(i => i.Ticker).ToListAsync(), Is.EqualTo(new[] { "SPY" }));
        var trade = await ctx.Db.Trades.SingleAsync();
        Assert.That(trade.Ticker, Is.EqualTo("XLE"));
        Assert.That(trade.Status, Is.EqualTo(TradeStatus.Closed));
    }

    [Test]
    public async Task Seed_IsIdempotent()
    {
        using var ctx = await TestHarness.SeededAsync();

        await DatabaseSeeder.SeedAsync(ctx.Db, ctx.Clock);

        Assert.That(await ctx.Db.Settings.CountAsync(), Is.EqualTo(1));
        Assert.That(await ctx.Db.Instruments.CountAsync(), Is.EqualTo(24));
        Assert.That(await ctx.Db.Trades.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Seed_DoesNotRestoreDeletedTradeOrOverwriteSettings()
    {
        using var ctx = await TestHarness.SeededAsync();
        var tradeId = (await ctx.Db.Trades.SingleAsync()).Id;
        await ctx.Trades.DeleteAsync(tradeId);
        (await ctx.Db.Settings.SingleAsync()).StartingCapital = 5000m;
        await ctx.Db.SaveChangesAsync();

        await DatabaseSeeder.SeedAsync(ctx.Db, ctx.Clock);

        Assert.That(await ctx.Db.Trades.CountAsync(), Is.EqualTo(0));
        Assert.That((await ctx.Db.Settings.SingleAsync()).StartingCapital, Is.EqualTo(5000m));
    }

    [Test]
    public async Task Dashboard_ReproducesThePlanFixture()
    {
        using var ctx = await TestHarness.SeededAsync();

        var d = await ctx.Dashboard.GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(d.RealizedPnl, Is.EqualTo(53.55m));
            Assert.That(d.AccountValue, Is.EqualTo(2053.55m));
            Assert.That(Math.Round(d.ReturnPct * 100m, 3), Is.EqualTo(2.678m));
            Assert.That(Math.Round(d.TaxSetAside, 2), Is.EqualTo(14.46m));
            Assert.That(Math.Round(d.AfterTaxPnl, 2), Is.EqualTo(39.09m));
            Assert.That(d.WinRate, Is.EqualTo(1m));
            Assert.That(Math.Round(d.AvgR!.Value, 2), Is.EqualTo(3.80m));
            Assert.That(d.ProfitFactor, Is.Null);
            Assert.That(d.CapitalAtWork, Is.EqualTo(0m));
            Assert.That(d.OpenCount, Is.EqualTo(0));
            Assert.That(d.ClosedCount, Is.EqualTo(1));
            Assert.That(d.SpyReturnPct, Is.Null, "no benchmark prices yet");
            Assert.That(d.BySetup["Pullback"].NetPnl, Is.EqualTo(53.55m));
        });
    }

    [Test]
    public async Task Dashboard_UsesBenchmarkPricesWhenPresent()
    {
        using var ctx = await TestHarness.SeededAsync();
        ctx.Db.PriceBars.AddRange(
            new Data.Entities.PriceBar { Ticker = "SPY", Date = new DateOnly(2026, 10, 5), Close = 100m },
            new Data.Entities.PriceBar { Ticker = "SPY", Date = new DateOnly(2026, 10, 12), Close = 101m });
        await ctx.Db.SaveChangesAsync();

        var d = await ctx.Dashboard.GetAsync();

        Assert.That(d.SpyReturnPct, Is.EqualTo(0.01m));
        Assert.That(d.YouMinusSpy, Is.EqualTo(d.ReturnPct - 0.01m));
    }

    [Test]
    public async Task SeededTrade_ReturnsTheClosedTradeFixtureThroughTheService()
    {
        using var ctx = await TestHarness.SeededAsync();

        var trade = (await ctx.Trades.ListAsync(new TradeListQuery())).Single();

        Assert.Multiple(() =>
        {
            Assert.That(trade.Position, Is.EqualTo(935.10m));
            Assert.That(trade.RiskDollars, Is.EqualTo(14.10m));
            Assert.That(Math.Round(trade.RiskPct * 100m, 3), Is.EqualTo(0.705m));
            Assert.That(Math.Round(trade.PlannedRewardRisk, 3), Is.EqualTo(3.787m));
            Assert.That(trade.Pnl, Is.EqualTo(53.55m));
            Assert.That(Math.Round(trade.PnlPct!.Value * 100m, 3), Is.EqualTo(5.727m));
            Assert.That(Math.Round(trade.RMultiple!.Value, 3), Is.EqualTo(3.798m));
            Assert.That(trade.DaysHeld, Is.EqualTo(7));
            Assert.That(trade.RiskOverLimit, Is.False);
            Assert.That(trade.RewardRiskBelowMin, Is.False);
            Assert.That(trade.WashSaleFlag, Is.False);
        });
    }
}
