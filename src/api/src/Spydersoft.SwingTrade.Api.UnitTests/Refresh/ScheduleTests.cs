using Spydersoft.SwingTrade.Api.Refresh;
using Spydersoft.SwingTrade.Api.UnitTests.Support;

namespace Spydersoft.SwingTrade.Api.UnitTests.Refresh;

[TestFixture]
public class ScheduleTests
{
    private static DateTimeOffset Utc(int y, int m, int d, int h, int min = 0) => new(y, m, d, h, min, 0, TimeSpan.Zero);

    private static ScheduledRun Next(DateTimeOffset after) => new RefreshSchedule(new FakeTimeProvider { UtcNow = after }).Next();

    // 2026-10-12 is a Monday and New York is on daylight time (UTC-4).
    [Test]
    public void BeforeThePrimaryTime_TheNextRunIs1730NewYorkTheSameDay() =>
        Assert.That(Next(Utc(2026, 10, 12, 12)), Is.EqualTo(new ScheduledRun(Utc(2026, 10, 12, 21, 30), RefreshAttempt.Primary)));

    [Test]
    public void AfterThePrimary_TheSecondAttemptIs2030() =>
        Assert.That(Next(Utc(2026, 10, 12, 21, 30)), Is.EqualTo(new ScheduledRun(Utc(2026, 10, 13, 0, 30), RefreshAttempt.Second)));

    [Test]
    public void AfterTheSecond_TheCatchUpIs0800NextMorning() =>
        Assert.That(Next(Utc(2026, 10, 13, 0, 30)), Is.EqualTo(new ScheduledRun(Utc(2026, 10, 13, 12), RefreshAttempt.CatchUp)));

    [Test]
    public void AfterTheCatchUp_TheNextPrimaryIsThatEvening() =>
        Assert.That(Next(Utc(2026, 10, 13, 12)), Is.EqualTo(new ScheduledRun(Utc(2026, 10, 13, 21, 30), RefreshAttempt.Primary)));

    [Test]
    public void Friday_CatchUpIsSaturdayMorning_ThenNothingUntilMondayEvening()
    {
        var saturdayCatchUp = Next(Utc(2026, 10, 10, 0, 30)); // after Friday 20:30 New York
        Assert.That(saturdayCatchUp, Is.EqualTo(new ScheduledRun(Utc(2026, 10, 10, 12), RefreshAttempt.CatchUp)));

        var monday = Next(saturdayCatchUp.At);
        Assert.That(monday, Is.EqualTo(new ScheduledRun(Utc(2026, 10, 12, 21, 30), RefreshAttempt.Primary)));
    }

    [Test]
    public void WeekendAndMondayMorning_HaveNoRun()
    {
        // From Sunday noon the next run is Monday 17:30, not a Monday 08:00 catch-up (nothing is missing from Friday).
        Assert.That(Next(Utc(2026, 10, 11, 16)), Is.EqualTo(new ScheduledRun(Utc(2026, 10, 12, 21, 30), RefreshAttempt.Primary)));
    }

    [Test]
    public void AfterDaylightSavingEnds_1730NewYorkMovesToTwoThirtyUtc()
    {
        // DST ended Sunday 2026-11-01; New York is UTC-5 from then on.
        Assert.That(Next(Utc(2026, 10, 31, 12)), Is.EqualTo(new ScheduledRun(Utc(2026, 11, 2, 22, 30), RefreshAttempt.Primary)),
            "Saturday noon UTC: no Saturday evening run, Monday 17:30 EST is 22:30 UTC");
        Assert.That(Next(Utc(2026, 10, 30, 21, 30)), Is.EqualTo(new ScheduledRun(Utc(2026, 10, 31, 0, 30), RefreshAttempt.Second)), "Friday still on EDT");
        Assert.That(Next(Utc(2026, 11, 2, 22, 30)), Is.EqualTo(new ScheduledRun(Utc(2026, 11, 3, 1, 30), RefreshAttempt.Second)));
        Assert.That(Next(Utc(2026, 11, 3, 1, 30)), Is.EqualTo(new ScheduledRun(Utc(2026, 11, 3, 13), RefreshAttempt.CatchUp)), "08:00 EST is 13:00 UTC");
    }

    [Test]
    public void AfterDaylightSavingStarts_1730NewYorkMovesBackToTwentyOneThirtyUtc()
    {
        // DST began Sunday 2026-03-08. Friday the 6th is still EST (22:30 UTC); Monday the 9th is EDT (21:30 UTC).
        Assert.That(Next(Utc(2026, 3, 6, 14)), Is.EqualTo(new ScheduledRun(Utc(2026, 3, 6, 22, 30), RefreshAttempt.Primary)));
        Assert.That(Next(Utc(2026, 3, 8, 12)), Is.EqualTo(new ScheduledRun(Utc(2026, 3, 9, 21, 30), RefreshAttempt.Primary)));
    }

    [Test]
    public void NextAfter_IsStrictlyLater_SoTheSameInstantIsNeverScheduledTwice()
    {
        var schedule = new RefreshSchedule(new FakeTimeProvider());
        var first = schedule.NextAfter(Utc(2026, 10, 12, 12));
        Assert.That(schedule.NextAfter(first.At).At, Is.GreaterThan(first.At));
    }

    // Monday 2026-10-12. 11:00 New York = 15:00 UTC; 16:15 New York = 20:15 UTC.
    [TestCase(15, 0, "2026-10-09", TestName = "Monday morning expects Friday")]
    [TestCase(20, 15, "2026-10-09", TestName = "Exactly 16:15 is not yet complete")]
    [TestCase(20, 16, "2026-10-12", TestName = "After 16:15 expects today")]
    public void LatestExpectedBar_OnAMonday(int hour, int minute, string expected) =>
        Assert.That(MarketCalendar.LatestExpectedBar(Utc(2026, 10, 12, hour, minute)), Is.EqualTo(DateOnly.Parse(expected)));

    [Test]
    public void LatestExpectedBar_OnWeekends_IsFriday()
    {
        Assert.That(MarketCalendar.LatestExpectedBar(Utc(2026, 10, 10, 15)), Is.EqualTo(new DateOnly(2026, 10, 9)));
        Assert.That(MarketCalendar.LatestExpectedBar(Utc(2026, 10, 11, 23)), Is.EqualTo(new DateOnly(2026, 10, 9)));
    }

    [Test]
    public void Stale_WhenTheAsOfDateIsOlderThanTheLatestExpectedBar()
    {
        var clock = new FakeTimeProvider { UtcNow = Utc(2026, 10, 12, 21) }; // Monday 17:00 New York
        var calendar = new MarketCalendar(clock);

        Assert.Multiple(() =>
        {
            Assert.That(calendar.IsStale(new DateOnly(2026, 10, 12)), Is.False);
            Assert.That(calendar.IsStale(new DateOnly(2026, 10, 9)), Is.True);
            Assert.That(calendar.IsStale(null), Is.True);
        });

        clock.UtcNow = Utc(2026, 10, 12, 15); // Monday morning: Friday's bar is current
        Assert.That(calendar.IsStale(new DateOnly(2026, 10, 9)), Is.False);
    }
}
