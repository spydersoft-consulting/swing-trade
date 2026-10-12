namespace Spydersoft.SwingTrade.Rules.UnitTests;

[TestFixture]
public class TradeMathTests
{
    private static readonly TradeInput Xle = new(15, 62.34m, 61.40m, 65.90m, 65.91m,
        new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12));

    [Test]
    public void ClosedTradeFixture()
    {
        var m = TradeMath.Compute(Xle, RulesSettings.Default, capital: 2000m);

        Assert.Multiple(() =>
        {
            Assert.That(m.Position, Is.EqualTo(935.10m));
            Assert.That(m.RiskDollars, Is.EqualTo(14.10m));
            Assert.That(Math.Round(m.RiskPct * 100m, 3), Is.EqualTo(0.705m));
            Assert.That(Math.Round(m.PlannedRewardRisk, 3), Is.EqualTo(3.787m));
            Assert.That(m.Pnl, Is.EqualTo(53.55m));
            Assert.That(Math.Round(m.PnlPct!.Value * 100m, 3), Is.EqualTo(5.727m));
            Assert.That(Math.Round(m.RMultiple!.Value, 3), Is.EqualTo(3.798m));
            Assert.That(m.DaysHeld, Is.EqualTo(7));
            Assert.That(m.RiskOverLimit, Is.False);
            Assert.That(m.RewardRiskBelowMin, Is.False);
        });
    }

    [Test]
    public void OpenTradeHasNoPnl()
    {
        var m = TradeMath.Compute(Xle with { ExitPrice = null, ExitDate = null }, RulesSettings.Default, 2000m);

        Assert.Multiple(() =>
        {
            Assert.That(m.Pnl, Is.Null);
            Assert.That(m.RMultiple, Is.Null);
            Assert.That(m.DaysHeld, Is.Null);
        });
    }

    [Test]
    public void FlagsRiskAndRewardRisk()
    {
        // 30 shares risking 1.00 each is exactly 1.5% (not over); 31 shares is over.
        var t = new TradeInput(31, 100m, 99m, 101m, null, null, null);

        var m = TradeMath.Compute(t, RulesSettings.Default, 2000m);

        Assert.Multiple(() =>
        {
            Assert.That(m.RiskOverLimit, Is.True);
            Assert.That(m.RewardRiskBelowMin, Is.True);
        });
    }

    [Test]
    public void RiskExactlyAtLimit_IsNotFlagged()
    {
        var m = TradeMath.Compute(new TradeInput(30, 100m, 99m, 105m, null, null, null), RulesSettings.Default, 2000m);

        Assert.That(m.RiskOverLimit, Is.False);
    }
}
