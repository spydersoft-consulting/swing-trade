namespace Spydersoft.SwingTrade.Rules;

public enum RiskBasis
{
    StartingCapital,
    AccountValue
}

public enum TradeStatus
{
    Planned,
    Open,
    Closed,
    Cancelled
}

public enum TradeSetup
{
    Pullback,
    Breakout,
    MeanReversion,
    Other
}

public enum Trend
{
    InsufficientData,
    Uptrend,
    Downtrend,
    Mixed
}

public enum Signal
{
    InsufficientData,
    PullbackCandidate,
    ExtendedWait,
    Avoid,
    Watch
}

/// <summary>
/// The thresholds every rule reads. Percentages are fractions (0.015 means 1.5%).
/// Mirrors the single Settings row; defaults are the plan's seed values.
/// </summary>
public sealed record RulesSettings(
    decimal StartingCapital = 2000m,
    decimal MaxRiskPct = 0.015m,
    int MaxOpenPositions = 3,
    decimal MinRewardRisk = 2.0m,
    decimal TaxSetAsideRate = 0.27m,
    int WashSaleWindowDays = 30,
    decimal MaxPositionPct = 0.50m,
    decimal PullbackBandLow = -0.01m,
    decimal PullbackBandHigh = 0.02m,
    decimal ExtendedThreshold = 0.05m,
    RiskBasis RiskBasis = RiskBasis.StartingCapital)
{
    public static RulesSettings Default { get; } = new();
}

public static class DisplayNames
{
    public static string Display(this Trend trend) => trend switch
    {
        Trend.Uptrend => "Uptrend",
        Trend.Downtrend => "Downtrend",
        Trend.Mixed => "Mixed",
        _ => "Insufficient data"
    };

    public static string Display(this Signal signal) => signal switch
    {
        Signal.PullbackCandidate => "Pullback candidate",
        Signal.ExtendedWait => "Extended - wait",
        Signal.Avoid => "Avoid",
        Signal.Watch => "Watch",
        _ => "Insufficient data"
    };
}
