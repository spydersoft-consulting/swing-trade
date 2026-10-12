namespace Spydersoft.SwingTrade.Rules;

public sealed record TradeInput(
    int Shares,
    decimal Entry,
    decimal Stop,
    decimal Target,
    decimal? ExitPrice,
    DateOnly? OpenDate,
    DateOnly? ExitDate);

/// <summary>Pnl, PnlPct, RMultiple and DaysHeld exist only for a trade with an exit.</summary>
public sealed record TradeMetrics(
    decimal Position,
    decimal RiskDollars,
    decimal RiskPct,
    decimal PlannedRewardRisk,
    decimal? Pnl,
    decimal? PnlPct,
    decimal? RMultiple,
    int? DaysHeld,
    bool RiskOverLimit,
    bool RewardRiskBelowMin);

public static class TradeMath
{
    /// <param name="capital">The risk basis (see <see cref="PositionSizer.CapitalFor"/>).</param>
    public static TradeMetrics Compute(TradeInput t, RulesSettings s, decimal capital)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(s);

        var riskPerShare = t.Entry - t.Stop;
        var position = t.Shares * t.Entry;
        var riskDollars = riskPerShare * t.Shares;
        var riskPct = capital == 0m ? 0m : riskDollars / capital;
        // Validation rejects Stop >= Entry before storage; guard so a bad row cannot divide by zero.
        var plannedRr = riskPerShare > 0m ? (t.Target - t.Entry) / riskPerShare : 0m;

        decimal? pnl = null, pnlPct = null, r = null;
        int? days = null;
        if (t.ExitPrice is { } exit)
        {
            pnl = (exit - t.Entry) * t.Shares;
            pnlPct = exit / t.Entry - 1m;
            r = riskDollars > 0m ? pnl / riskDollars : null;
            if (t.OpenDate is { } open && t.ExitDate is { } exitDate)
                days = exitDate.DayNumber - open.DayNumber;
        }

        return new TradeMetrics(position, riskDollars, riskPct, plannedRr, pnl, pnlPct, r, days,
            riskPct > s.MaxRiskPct, plannedRr < s.MinRewardRisk);
    }
}
