namespace Spydersoft.SwingTrade.Rules.UnitTests;

[TestFixture]
public class IndicatorTests
{
    [Test]
    public void ClosesOneThroughFifty()
    {
        var closes = Enumerable.Range(1, 50).Select(i => (decimal)i).ToList();

        var ind = IndicatorCalculator.Calculate(closes);
        var t = TrendSignal.Evaluate(ind, RulesSettings.Default);

        Assert.Multiple(() =>
        {
            Assert.That(ind.Last, Is.EqualTo(50m));
            Assert.That(ind.Ma20, Is.EqualTo(40.5m));
            Assert.That(ind.Ma50, Is.EqualTo(25.5m));
            Assert.That(Math.Round(t.PctVs20!.Value * 100m, 3), Is.EqualTo(23.457m));
            Assert.That(t.Trend.Display(), Is.EqualTo("Uptrend"));
            Assert.That(t.Signal.Display(), Is.EqualTo("Extended - wait"));
        });
    }

    [Test]
    public void FortyNineBars_HaveNoMa50()
    {
        var ind = IndicatorCalculator.Calculate(Enumerable.Range(1, 49).Select(i => (decimal)i).ToList());

        Assert.Multiple(() =>
        {
            Assert.That(ind.Ma50, Is.Null);
            Assert.That(ind.BarsUsed, Is.EqualTo(49));
        });
    }

    [Test]
    public void OnlyMostRecentBarsAreAveraged()
    {
        // 100 bars; the first 50 are junk and must not affect MA50.
        var closes = Enumerable.Repeat(1000m, 50).Concat(Enumerable.Range(1, 50).Select(i => (decimal)i)).ToList();

        Assert.That(IndicatorCalculator.Calculate(closes).Ma50, Is.EqualTo(25.5m));
    }

    [Test]
    public void BarDatedTodayBeforeClose_IsNotCompleted()
    {
        var today = new DateOnly(2026, 10, 12);

        Assert.Multiple(() =>
        {
            Assert.That(BarCompletion.IsCompleted(today, today, new TimeOnly(15, 59)), Is.False);
            Assert.That(BarCompletion.IsCompleted(today, today, new TimeOnly(16, 15)), Is.False);
            Assert.That(BarCompletion.IsCompleted(today, today, new TimeOnly(16, 16)), Is.True);
            Assert.That(BarCompletion.IsCompleted(today.AddDays(-1), today, new TimeOnly(9, 0)), Is.True);
        });
    }

    [Test]
    public void CompletedFilter_ConvertsUtcToNewYork()
    {
        // 2026-10-12 19:00 UTC = 15:00 EDT: today's bar is still forming.
        var now = new DateTimeOffset(2026, 10, 12, 19, 0, 0, TimeSpan.Zero);
        Assert.That(BarCompletion.IsCompleted(new DateOnly(2026, 10, 12), now), Is.False);

        // 21:00 UTC = 17:00 EDT: complete.
        now = new DateTimeOffset(2026, 10, 12, 21, 0, 0, TimeSpan.Zero);
        Assert.That(BarCompletion.IsCompleted(new DateOnly(2026, 10, 12), now), Is.True);
    }

    [Test]
    public void MovingAverageSeries_IsNullUntilTheWindowFills_AndMatchesCalculateAtEveryBar()
    {
        var closes = Enumerable.Range(1, 60).Select(i => (decimal)(i * i % 37) + 10m).ToList();

        var ma20 = IndicatorCalculator.MovingAverageSeries(closes, IndicatorCalculator.ShortWindow);
        var ma50 = IndicatorCalculator.MovingAverageSeries(closes, IndicatorCalculator.LongWindow);

        Assert.Multiple(() =>
        {
            Assert.That(ma20, Has.Count.EqualTo(60));
            Assert.That(ma20.Take(19), Has.All.Null);
            Assert.That(ma50.Take(49), Has.All.Null);
            for (var i = 0; i < closes.Count; i++)
            {
                var expected = IndicatorCalculator.Calculate(closes.Take(i + 1).ToList());
                Assert.That(ma20[i], Is.EqualTo(expected.Ma20), $"MA20 at {i}");
                Assert.That(ma50[i], Is.EqualTo(expected.Ma50), $"MA50 at {i}");
            }
        });
    }

    [Test]
    public void MovingAverageSeries_RejectsAWindowBelowOne() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => IndicatorCalculator.MovingAverageSeries([1m], 0));
}
