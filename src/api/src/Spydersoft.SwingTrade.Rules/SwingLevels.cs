namespace Spydersoft.SwingTrade.Rules;

/// <summary>
/// The lowest and highest completed close in the lookback window. Indices are positions in the input list (oldest first).
/// </summary>
public sealed record SwingLevels(decimal SwingLow, int SwingLowIndex, decimal PriorHigh, int PriorHighIndex, int Lookback, int BarsUsed);

public static class SwingLevelCalculator
{
    public const int DefaultLookback = 20;
    public const int MinLookback = 5;
    public const int MaxLookback = 120;

    /// <param name="closesOldestFirst">Closes of completed bars only, oldest first.</param>
    /// <param name="lookback">Most recent bars to scan, clamped to <see cref="MinLookback"/>..<see cref="MaxLookback"/>.</param>
    /// <remarks>Ties resolve to the most recent bar. Bars hold closes only, so these are close-based levels, not intraday extremes.</remarks>
    public static SwingLevels Calculate(IReadOnlyList<decimal> closesOldestFirst, int lookback = DefaultLookback)
    {
        ArgumentNullException.ThrowIfNull(closesOldestFirst);
        if (closesOldestFirst.Count == 0)
            throw new ArgumentException("At least one bar is required.", nameof(closesOldestFirst));

        lookback = Math.Clamp(lookback, MinLookback, MaxLookback);
        var start = Math.Max(0, closesOldestFirst.Count - lookback);
        var lowIndex = start;
        var highIndex = start;
        for (var i = start; i < closesOldestFirst.Count; i++)
        {
            if (closesOldestFirst[i] <= closesOldestFirst[lowIndex])
                lowIndex = i;
            if (closesOldestFirst[i] >= closesOldestFirst[highIndex])
                highIndex = i;
        }

        return new SwingLevels(closesOldestFirst[lowIndex], lowIndex, closesOldestFirst[highIndex], highIndex, lookback, closesOldestFirst.Count - start);
    }
}
