namespace Spydersoft.SwingTrade.Rules.UnitTests;

[TestFixture]
public class PositionSizerTests
{
    private static readonly RulesSettings Defaults = RulesSettings.Default;

    [TestCase(62.82, 61.40, 65.90, 15, 942.30, 21.30, 2.169, 62.90, "OK - log it")]
    [TestCase(769.64, 753.50, 773.50, 1, 769.64, 16.14, 0.239, 760.17, "SKIP - reward:risk below minimum")]
    [TestCase(192.00, 187.50, 202.00, 5, 960.00, 22.50, 2.222, 192.33, "OK - log it")]
    [TestCase(196.30, 193.50, 202.00, 5, 981.50, 14.00, 2.036, 196.33, "OK - log it")]
    [TestCase(196.60, 193.50, 202.00, 5, 983.00, 15.50, 1.742, 196.33, "SKIP - reward:risk below minimum")]
    [TestCase(62.34, 61.40, 65.90, 16, 997.44, 15.04, 3.787, 62.90, "OK - log it")]
    public void Fixtures(double entry, double stop, double target, int shares, double cost, double risk,
        double rewardRisk, double maxEntry, string verdict)
    {
        var r = PositionSizer.Size(new SizerInput((decimal)entry, (decimal)stop, (decimal)target), Defaults);

        Assert.Multiple(() =>
        {
            Assert.That(r.Shares, Is.EqualTo(shares));
            Assert.That(r.PositionCost, Is.EqualTo((decimal)cost));
            Assert.That(r.RiskDollars, Is.EqualTo((decimal)risk));
            Assert.That(Math.Round(r.RewardRisk!.Value, 3), Is.EqualTo((decimal)rewardRisk));
            Assert.That(Math.Round(r.MaxEntryForMinRewardRisk, 2), Is.EqualTo((decimal)maxEntry));
            Assert.That(r.Verdict, Is.EqualTo(verdict));
        });
    }

    [TestCase(62.34, 62.34)]
    [TestCase(62.34, 63.00)]
    public void StopAtOrAboveEntry_IsRejected(double entry, double stop)
    {
        var r = PositionSizer.Size(new SizerInput((decimal)entry, (decimal)stop, 70m), Defaults);

        Assert.That(r.Verdict, Is.EqualTo("Stop must be below entry"));
        Assert.That(r.Shares, Is.Zero);
    }

    [Test]
    public void CashLimitsShares()
    {
        var r = PositionSizer.Size(new SizerInput(62.34m, 61.40m, 65.90m, AvailableCash: 500m), Defaults);

        Assert.Multiple(() =>
        {
            Assert.That(r.SharesByCash, Is.EqualTo(8));
            Assert.That(r.Shares, Is.EqualTo(8));
            Assert.That(r.Verdict, Is.EqualTo("OK - log it"));
        });
    }

    [Test]
    public void NotEnoughCash_Skips()
    {
        var r = PositionSizer.Size(new SizerInput(62.34m, 61.40m, 65.90m, AvailableCash: 50m), Defaults);

        Assert.That(r.Verdict, Is.EqualTo("SKIP - not enough cash"));
    }

    [Test]
    public void OneShareRisksMoreThanBudget_Skips()
    {
        // Budget 30, risk per share 40, wide target so reward:risk is fine.
        var r = PositionSizer.Size(new SizerInput(100m, 60m, 200m), Defaults);

        Assert.That(r.Verdict, Is.EqualTo("SKIP - one share risks more than the budget"));
    }

    [Test]
    public void OneShareExceedsPositionCap_Skips()
    {
        // Cap is 1,000; one share costs 1,200. Risk per share 10 fits the budget.
        var r = PositionSizer.Size(new SizerInput(1200m, 1190m, 1240m), Defaults);

        Assert.That(r.Verdict, Is.EqualTo("SKIP - one share exceeds the position cap"));
    }

    [TestCase(2, false)]
    [TestCase(3, true)]
    [TestCase(4, true)]
    public void OpenPositionWarning(int openCount, bool warns)
    {
        var r = PositionSizer.Size(new SizerInput(62.34m, 61.40m, 65.90m, OpenCount: openCount), Defaults);

        Assert.That(r.OpenPositionWarning, Is.EqualTo(warns));
        Assert.That(r.Verdict, Is.EqualTo("OK - log it"));
    }

    [Test]
    public void AccountValueRiskBasis_UsesLiveCapital()
    {
        var settings = Defaults with { RiskBasis = RiskBasis.AccountValue };
        var r = PositionSizer.Size(new SizerInput(62.34m, 61.40m, 65.90m, AccountValue: 4000m), settings);

        Assert.That(r.Capital, Is.EqualTo(4000m));
        Assert.That(r.RiskBudget, Is.EqualTo(60m));
    }

    [Test]
    public void AvailableCash_ReservesOpenAndPlannedCost()
    {
        var cash = PositionSizer.AvailableCash(2000m, realizedPnl: 53.55m, openCost: 900m, plannedCost: 300m);

        Assert.That(cash, Is.EqualTo(853.55m));
    }
}
