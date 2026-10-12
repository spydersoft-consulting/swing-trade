using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Api.Refresh;

/// <summary>
/// The "latest expected bar" and staleness rules, shared by the scheduler, the ingestion service and (from M4) the
/// watchlist. No holiday calendar: a holiday simply never gets a bar, so it reads as stale until the next session.
/// Completion comes from <see cref="BarCompletion"/> in the Rules library.
/// </summary>
public class MarketCalendar(TimeProvider time)
{
    public DateOnly TodayNewYork => BarCompletion.ToNewYork(time.GetUtcNow()).Date;

    /// <summary>The most recent weekday whose close is complete (after 16:15 New York on the day itself).</summary>
    public DateOnly LatestExpectedBar() => LatestExpectedBar(time.GetUtcNow());

    public static DateOnly LatestExpectedBar(DateTimeOffset now)
    {
        var (today, clock) = BarCompletion.ToNewYork(now);
        for (var d = today; ; d = d.AddDays(-1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            if (BarCompletion.IsCompleted(d, today, clock))
                return d;
        }
    }

    /// <summary>True when the as-of date is older than the most recent weekday close (or there is no data at all).</summary>
    public bool IsStale(DateOnly? asOfDate) => asOfDate is null || asOfDate.Value < LatestExpectedBar();
}
