namespace Spydersoft.SwingTrade.Rules;

/// <summary>Last completed close and its simple moving averages. An average is null until enough bars exist.</summary>
public sealed record Indicators(decimal Last, decimal? Ma20, decimal? Ma50, int BarsUsed);

public static class IndicatorCalculator
{
    public const int ShortWindow = 20;
    public const int LongWindow = 50;

    /// <param name="closesOldestFirst">Closes of completed bars only, oldest first.</param>
    public static Indicators Calculate(IReadOnlyList<decimal> closesOldestFirst)
    {
        ArgumentNullException.ThrowIfNull(closesOldestFirst);
        if (closesOldestFirst.Count == 0)
            throw new ArgumentException("At least one bar is required.", nameof(closesOldestFirst));

        return new Indicators(
            closesOldestFirst[^1],
            Average(closesOldestFirst, ShortWindow),
            Average(closesOldestFirst, LongWindow),
            closesOldestFirst.Count);
    }

    /// <summary>
    /// The simple average of the <paramref name="window"/> closes ending at each bar, for charting. An entry is null until
    /// <paramref name="window"/> bars exist. The last entry equals the MA that <see cref="Calculate"/> returns for the same input.
    /// </summary>
    public static IReadOnlyList<decimal?> MovingAverageSeries(IReadOnlyList<decimal> closesOldestFirst, int window)
    {
        ArgumentNullException.ThrowIfNull(closesOldestFirst);
        ArgumentOutOfRangeException.ThrowIfLessThan(window, 1);

        var series = new decimal?[closesOldestFirst.Count];
        for (var end = 0; end < series.Length; end++)
            series[end] = Average(closesOldestFirst, window, end + 1);
        return series;
    }

    private static decimal? Average(IReadOnlyList<decimal> closes, int window) => Average(closes, window, closes.Count);

    private static decimal? Average(IReadOnlyList<decimal> closes, int window, int count)
    {
        if (count < window)
            return null;

        decimal sum = 0m;
        for (var i = count - window; i < count; i++)
            sum += closes[i];
        return sum / window;
    }
}

public static class BarCompletion
{
    public const string MarketTimeZoneId = "America/New_York";

    /// <summary>A bar dated today only counts once New York time is after this.</summary>
    public static readonly TimeOnly CompletedAfter = new(16, 15);

    /// <summary>
    /// A bar is completed when its date is before today in New York, or it is today's bar and
    /// New York time is after 16:15. An intraday bar must never enter the averages.
    /// </summary>
    public static bool IsCompleted(DateOnly barDate, DateOnly todayNewYork, TimeOnly nowNewYork)
    {
        if (barDate < todayNewYork)
            return true;
        return barDate == todayNewYork && nowNewYork > CompletedAfter;
    }

    public static bool IsCompleted(DateOnly barDate, DateTimeOffset now)
    {
        var (today, time) = ToNewYork(now);
        return IsCompleted(barDate, today, time);
    }

    public static (DateOnly Date, TimeOnly Time) ToNewYork(DateTimeOffset now)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(MarketTimeZoneId);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        return (DateOnly.FromDateTime(local.DateTime), TimeOnly.FromDateTime(local.DateTime));
    }
}
