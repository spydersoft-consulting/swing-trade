namespace Spydersoft.SwingTrade.Rules.UnitTests;

[TestFixture]
public class DashboardTests
{
    private static readonly DashboardTrade Xle = new(TradeStatus.Closed, TradeSetup.Pullback, 15, 62.34m, 61.40m,
        65.91m, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12), true);

    [Test]
    public void ClosedTradeFixture()
    {
        var d = DashboardCalculator.Compute([Xle], RulesSettings.Default, benchmark: null);

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
            Assert.That(d.AvgLoss, Is.Null);
            Assert.That(d.CapitalAtWork, Is.Zero);
            Assert.That(d.OpenCount, Is.Zero);
            Assert.That(d.PlanAdherence, Is.EqualTo(1m));
            Assert.That(d.AvgDaysHeld, Is.EqualTo(7m));
        });
    }

    [Test]
    public void NoTrades_HasNoRates()
    {
        var d = DashboardCalculator.Compute([], RulesSettings.Default, null);

        Assert.Multiple(() =>
        {
            Assert.That(d.WinRate, Is.Null);
            Assert.That(d.AvgR, Is.Null);
            Assert.That(d.AccountValue, Is.EqualTo(2000m));
        });
    }

    [Test]
    public void LossesGiveProfitFactorAndNoTaxSetAside()
    {
        var loss = Xle with { ExitPrice = 60m };   // -35.10
        var d = DashboardCalculator.Compute([loss], RulesSettings.Default, null);

        Assert.Multiple(() =>
        {
            Assert.That(d.TaxSetAside, Is.Zero);
            Assert.That(d.WinRate, Is.Zero);
            Assert.That(d.ProfitFactor, Is.Zero);
            Assert.That(d.AvgLoss, Is.EqualTo(-35.10m));
        });
    }

    [Test]
    public void OpenAndPlannedTrades()
    {
        var open = new DashboardTrade(TradeStatus.Open, TradeSetup.Breakout, 10, 50m, 48m, null,
            new DateOnly(2026, 10, 1), null, null);
        var planned = open with { Status = TradeStatus.Planned, OpenDate = null };
        var loss = Xle with { ExitPrice = 60m, Setup = TradeSetup.Breakout };

        var d = DashboardCalculator.Compute([open, planned, Xle, loss], RulesSettings.Default, null);

        Assert.Multiple(() =>
        {
            Assert.That(d.OpenCount, Is.EqualTo(1));
            Assert.That(d.CapitalAtWork, Is.EqualTo(500m));
            Assert.That(d.CapitalAtWorkPct, Is.EqualTo(0.25m));
            Assert.That(d.RiskAtStops, Is.EqualTo(20m));
            Assert.That(d.RiskAtStopsPct, Is.EqualTo(0.01m));
            Assert.That(d.ReservedByPlanned, Is.EqualTo(500m));
            Assert.That(d.ClosedCount, Is.EqualTo(2));
            Assert.That(d.ProfitFactor, Is.EqualTo(53.55m / 35.10m));
            Assert.That(d.BySetup[TradeSetup.Pullback].Trades, Is.EqualTo(1));
            Assert.That(d.BySetup[TradeSetup.Breakout].NetPnl, Is.EqualTo(-35.10m));
            Assert.That(d.AvailableCash, Is.EqualTo(2000m + 53.55m - 35.10m - 500m - 500m));
        });
    }

    [Test]
    public void BenchmarkComparison()
    {
        var d = DashboardCalculator.Compute([Xle], RulesSettings.Default, new BenchmarkPrices(700m, 735m));

        Assert.Multiple(() =>
        {
            Assert.That(d.SpyReturnPct, Is.EqualTo(0.05m));
            Assert.That(d.YouMinusSpy, Is.EqualTo(d.ReturnPct - 0.05m));
        });
    }

    [Test]
    public void EquityCurve_StepsOnExitDates()
    {
        var d = DashboardCalculator.Compute([Xle], RulesSettings.Default, null);

        Assert.That(d.EquityCurve, Is.EqualTo(new[] { new EquityPoint(new DateOnly(2026, 10, 12), 2053.55m) }));
    }
}
