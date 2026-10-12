namespace Spydersoft.SwingTrade.Rules.UnitTests;

[TestFixture]
public class WashSaleTests
{
    private static readonly RulesSettings Defaults = RulesSettings.Default;

    private static readonly ClosedTradeRef Loss =
        new(1, "XLE", null, new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 5), -20m);

    private static bool Flagged(DateOnly openDate, params ClosedTradeRef[] closed) =>
        WashSale.IsFlagged(99, "XLE", null, openDate, closed, Defaults);

    [TestCase("2026-11-04", true)]
    [TestCase("2026-11-05", false)]
    [TestCase("2026-09-05", true)]
    [TestCase("2026-09-04", false)]
    public void WindowBoundaries(string open, bool expected) =>
        Assert.That(Flagged(DateOnly.Parse(open), Loss), Is.EqualTo(expected));

    [Test]
    public void ReentryOkAfter_IsLossExitPlusWindowPlusOne() =>
        Assert.That(WashSale.ReentryOkAfter("XLE", null, [Loss], Defaults), Is.EqualTo(new DateOnly(2026, 11, 5)));

    [Test]
    public void NoLoss_IsClear() =>
        Assert.That(WashSale.ReentryOkAfter("XLE", null, [Loss with { Pnl = 53.55m }], Defaults), Is.Null);

    [Test]
    public void WinningTrade_CreatesNoFlag() =>
        Assert.That(Flagged(new DateOnly(2026, 10, 20), Loss with { Pnl = 10m }), Is.False);

    [Test]
    public void TradeNeverFlagsItself() =>
        Assert.That(WashSale.IsFlagged(1, "XLE", null, new DateOnly(2026, 9, 28), [Loss], Defaults), Is.False);

    [Test]
    public void OtherTicker_DoesNotFlag() =>
        Assert.That(WashSale.IsFlagged(99, "SPY", null, new DateOnly(2026, 10, 20), [Loss], Defaults), Is.False);

    [Test]
    public void SameGroup_Flags() =>
        Assert.That(WashSale.IsFlagged(99, "SPLG", "SP500", new DateOnly(2026, 10, 20),
            [Loss with { Ticker = "SPY", WashSaleGroup = "SP500" }], Defaults), Is.True);

    [Test]
    public void ReentryUsesLatestLoss()
    {
        var later = Loss with { Id = 2, ExitDate = new DateOnly(2026, 10, 9) };

        Assert.That(WashSale.ReentryOkAfter("XLE", null, [Loss, later], Defaults), Is.EqualTo(new DateOnly(2026, 11, 9)));
    }
}
