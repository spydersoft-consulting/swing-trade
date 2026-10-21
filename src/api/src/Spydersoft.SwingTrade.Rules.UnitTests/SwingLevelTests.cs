namespace Spydersoft.SwingTrade.Rules.UnitTests;

[TestFixture]
public class SwingLevelTests
{
    private static readonly decimal[] Closes = [10m, 12m, 9m, 11m, 15m, 13m, 14m, 12m];

    [Test]
    public void FindsLowestAndHighestCloseInTheWindow()
    {
        var levels = SwingLevelCalculator.Calculate(Closes, 8);

        Assert.Multiple(() =>
        {
            Assert.That(levels.SwingLow, Is.EqualTo(9m));
            Assert.That(levels.SwingLowIndex, Is.EqualTo(2));
            Assert.That(levels.PriorHigh, Is.EqualTo(15m));
            Assert.That(levels.PriorHighIndex, Is.EqualTo(4));
            Assert.That(levels.BarsUsed, Is.EqualTo(8));
        });
    }

    [Test]
    public void OnlyScansTheLookbackWindow()
    {
        var levels = SwingLevelCalculator.Calculate(Closes, 5);

        Assert.Multiple(() =>
        {
            Assert.That(levels.SwingLow, Is.EqualTo(11m));
            Assert.That(levels.SwingLowIndex, Is.EqualTo(3));
            Assert.That(levels.PriorHigh, Is.EqualTo(15m));
            Assert.That(levels.Lookback, Is.EqualTo(5));
            Assert.That(levels.BarsUsed, Is.EqualTo(5));
        });
    }

    [Test]
    public void FewerBarsThanTheLookback_UsesWhatExists()
    {
        var levels = SwingLevelCalculator.Calculate([5m, 7m, 6m], 20);

        Assert.Multiple(() =>
        {
            Assert.That(levels.BarsUsed, Is.EqualTo(3));
            Assert.That(levels.SwingLow, Is.EqualTo(5m));
            Assert.That(levels.PriorHigh, Is.EqualTo(7m));
        });
    }

    [Test]
    public void TiesResolveToTheMostRecentBar()
    {
        var levels = SwingLevelCalculator.Calculate([9m, 15m, 9m, 15m, 12m], 5);

        Assert.Multiple(() =>
        {
            Assert.That(levels.SwingLowIndex, Is.EqualTo(2));
            Assert.That(levels.PriorHighIndex, Is.EqualTo(3));
        });
    }

    [TestCase(0, 5)]
    [TestCase(1000, 120)]
    public void LookbackIsClamped(int requested, int expected) =>
        Assert.That(SwingLevelCalculator.Calculate(Enumerable.Range(1, 200).Select(i => (decimal)i).ToList(), requested).Lookback, Is.EqualTo(expected));

    [Test]
    public void NoBars_Throws() =>
        Assert.Throws<ArgumentException>(() => SwingLevelCalculator.Calculate([]));
}
