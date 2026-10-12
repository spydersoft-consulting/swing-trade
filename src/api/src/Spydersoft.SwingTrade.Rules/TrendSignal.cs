namespace Spydersoft.SwingTrade.Rules;

public sealed record TrendSignalResult(Trend Trend, Signal Signal, decimal? PctVs20);

/// <summary>Trend and Signal are derived on read from current settings, never stored.</summary>
public static class TrendSignal
{
    public static TrendSignalResult Evaluate(Indicators ind, RulesSettings s)
    {
        ArgumentNullException.ThrowIfNull(ind);
        ArgumentNullException.ThrowIfNull(s);

        decimal? pctVs20 = ind is { Ma20: { } ma20 } && ma20 != 0m ? ind.Last / ma20 - 1m : null;

        if (ind.BarsUsed < IndicatorCalculator.LongWindow || ind.Ma20 is null || ind.Ma50 is null || pctVs20 is null)
            return new TrendSignalResult(Trend.InsufficientData, Signal.InsufficientData, pctVs20);

        var trend = (ind.Last > ind.Ma50 && ind.Ma20 > ind.Ma50) ? Trend.Uptrend
            : (ind.Last < ind.Ma50 && ind.Ma20 < ind.Ma50) ? Trend.Downtrend
            : Trend.Mixed;

        var pct = pctVs20.Value;
        var signal =
            trend == Trend.Uptrend && pct >= s.PullbackBandLow && pct <= s.PullbackBandHigh ? Signal.PullbackCandidate
            : trend == Trend.Uptrend && pct > s.ExtendedThreshold ? Signal.ExtendedWait
            : trend == Trend.Downtrend ? Signal.Avoid
            : Signal.Watch;

        return new TrendSignalResult(trend, signal, pctVs20);
    }
}
