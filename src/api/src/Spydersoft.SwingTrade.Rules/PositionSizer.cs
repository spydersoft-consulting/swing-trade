namespace Spydersoft.SwingTrade.Rules;

public sealed record SizerInput(
    decimal Entry,
    decimal Stop,
    decimal Target,
    decimal? AvailableCash = null,
    int OpenCount = 0,
    decimal? AccountValue = null);

public sealed record SizerResult(
    decimal Capital,
    decimal RiskBudget,
    decimal RiskPerShare,
    int SharesByRisk,
    int SharesByCap,
    int? SharesByCash,
    int Shares,
    decimal PositionCost,
    decimal RiskDollars,
    decimal RiskPct,
    decimal? RewardRisk,
    decimal MaxEntryForMinRewardRisk,
    string Verdict,
    bool OpenPositionWarning);

public static class PositionSizer
{
    public const string VerdictOk = "OK - log it";
    public const string VerdictStopAboveEntry = "Stop must be below entry";
    public const string VerdictRewardRisk = "SKIP - reward:risk below minimum";
    public const string VerdictRiskBudget = "SKIP - one share risks more than the budget";
    public const string VerdictPositionCap = "SKIP - one share exceeds the position cap";
    public const string VerdictCash = "SKIP - not enough cash";

    public static decimal CapitalFor(RulesSettings s, decimal? accountValue) =>
        s.RiskBasis == RiskBasis.AccountValue && accountValue is { } av ? av : s.StartingCapital;

    /// <summary>StartingCapital + RealizedPnL - cost of Open trades - cost of Planned trades.</summary>
    public static decimal AvailableCash(decimal startingCapital, decimal realizedPnl, decimal openCost, decimal plannedCost) =>
        startingCapital + realizedPnl - openCost - plannedCost;

    public static SizerResult Size(SizerInput input, RulesSettings s)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(s);

        var capital = CapitalFor(s, input.AccountValue);
        var riskBudget = capital * s.MaxRiskPct;
        var riskPerShare = input.Entry - input.Stop;
        var maxEntry = (input.Target + s.MinRewardRisk * input.Stop) / (1m + s.MinRewardRisk);
        var warn = input.OpenCount >= s.MaxOpenPositions;

        if (riskPerShare <= 0m)
        {
            return new SizerResult(capital, riskBudget, riskPerShare, 0, 0, input.AvailableCash is null ? null : 0,
                0, 0m, 0m, 0m, null, maxEntry, VerdictStopAboveEntry, warn);
        }

        var byRisk = (int)Math.Floor(riskBudget / riskPerShare);
        var byCap = (int)Math.Floor(capital * s.MaxPositionPct / input.Entry);
        int? byCash = input.AvailableCash is { } cash ? (int)Math.Floor(cash / input.Entry) : null;

        var shares = Math.Max(0, Math.Min(Math.Min(byRisk, byCap), byCash ?? int.MaxValue));
        var riskDollars = shares * riskPerShare;
        var rewardRisk = (input.Target - input.Entry) / riskPerShare;

        var verdict =
            rewardRisk < s.MinRewardRisk ? VerdictRewardRisk
            : byRisk < 1 ? VerdictRiskBudget
            : byCap < 1 ? VerdictPositionCap
            : byCash is < 1 ? VerdictCash
            : VerdictOk;

        return new SizerResult(capital, riskBudget, riskPerShare, byRisk, byCap, byCash, shares,
            shares * input.Entry, riskDollars, riskDollars / capital, rewardRisk, maxEntry, verdict, warn);
    }
}
