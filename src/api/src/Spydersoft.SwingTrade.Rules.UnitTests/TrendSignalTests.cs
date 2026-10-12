namespace Spydersoft.SwingTrade.Rules.UnitTests;

[TestFixture]
public class TrendSignalTests
{
    private static readonly RulesSettings Defaults = RulesSettings.Default;

    [TestCase(769.64, 764.83, 763.70, "Uptrend", 0.629, "Pullback candidate")]
    [TestCase(749.58, 727.78, 716.82, "Uptrend", 2.995, "Watch")]
    [TestCase(62.82, 63.47, 62.12, "Uptrend", -1.024, "Watch")]
    [TestCase(166.18, 168.38, 168.53, "Downtrend", -1.307, "Avoid")]
    [TestCase(198.78, 194.52, 189.33, "Uptrend", 2.190, "Watch")]
    [TestCase(751.27, 737.54, 725.27, "Uptrend", 1.862, "Pullback candidate")]
    [TestCase(778.57, 767.98, 767.99, "Mixed", 1.379, "Watch")]
    [TestCase(200.00, 190.00, 185.00, "Uptrend", 5.263, "Extended - wait")]
    [TestCase(99.00, 100.00, 90.00, "Uptrend", -1.000, "Pullback candidate")]
    [TestCase(102.00, 100.00, 90.00, "Uptrend", 2.000, "Pullback candidate")]
    [TestCase(102.01, 100.00, 90.00, "Uptrend", 2.010, "Watch")]
    [TestCase(105.00, 100.00, 90.00, "Uptrend", 5.000, "Watch")]
    [TestCase(105.01, 100.00, 90.00, "Uptrend", 5.010, "Extended - wait")]
    [TestCase(100.00, 100.00, 100.00, "Mixed", 0.000, "Watch")]
    public void Fixtures(double last, double ma20, double ma50, string trend, double pctVs20Percent, string signal)
    {
        var ind = new Indicators((decimal)last, (decimal)ma20, (decimal)ma50, 50);

        var t = TrendSignal.Evaluate(ind, Defaults);

        Assert.Multiple(() =>
        {
            Assert.That(t.Trend.Display(), Is.EqualTo(trend));
            Assert.That(Math.Round(t.PctVs20!.Value * 100m, 3), Is.EqualTo((decimal)pctVs20Percent));
            Assert.That(t.Signal.Display(), Is.EqualTo(signal));
        });
    }

    [Test]
    public void FewerThan50Bars_IsInsufficientData()
    {
        var ind = IndicatorCalculator.Calculate(Enumerable.Range(1, 49).Select(i => (decimal)i).ToList());

        var t = TrendSignal.Evaluate(ind, Defaults);

        Assert.Multiple(() =>
        {
            Assert.That(t.Trend.Display(), Is.EqualTo("Insufficient data"));
            Assert.That(t.Signal.Display(), Is.EqualTo("Insufficient data"));
        });
    }

    [Test]
    public void ThresholdChange_AppliesImmediately()
    {
        var ind = new Indicators(102.01m, 100m, 90m, 50);

        var wide = TrendSignal.Evaluate(ind, Defaults with { PullbackBandHigh = 0.03m });

        Assert.That(wide.Signal.Display(), Is.EqualTo("Pullback candidate"));
    }
}
