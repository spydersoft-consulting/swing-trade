using Cronos;

namespace Spydersoft.SwingTrade.Api.Refresh;

public enum RefreshAttempt
{
    Primary,
    Second,
    CatchUp
}

public sealed record ScheduledRun(DateTimeOffset At, RefreshAttempt Attempt);

/// <summary>
/// When the nightly job fires, all evaluated in America/New_York so daylight saving is handled by the zone:
/// 17:30 weekdays, a second attempt at 20:30 (exchange corrections settle by 20:00), and a catch-up at 08:00 the next
/// morning (Tuesday to Saturday) that only runs if the latest expected bar is still missing.
/// </summary>
public class RefreshSchedule(TimeProvider time)
{
    public const string PrimaryCron = "30 17 * * 1-5";
    public const string SecondCron = "30 20 * * 1-5";
    public const string CatchUpCron = "0 8 * * 2-6";

    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static readonly (CronExpression Cron, RefreshAttempt Attempt)[] Entries =
    [
        (CronExpression.Parse(PrimaryCron), RefreshAttempt.Primary),
        (CronExpression.Parse(SecondCron), RefreshAttempt.Second),
        (CronExpression.Parse(CatchUpCron), RefreshAttempt.CatchUp)
    ];

    public ScheduledRun Next() => NextAfter(time.GetUtcNow());

    /// <summary>The first scheduled run strictly after the given instant.</summary>
    public ScheduledRun NextAfter(DateTimeOffset after) =>
        Entries
            .Select(e => (At: e.Cron.GetNextOccurrence(after, NewYork), e.Attempt))
            .Where(e => e.At is not null)
            .Select(e => new ScheduledRun(e.At!.Value, e.Attempt))
            .MinBy(r => r.At)!;
}
