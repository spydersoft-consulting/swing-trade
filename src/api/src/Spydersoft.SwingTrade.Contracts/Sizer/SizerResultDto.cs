namespace Spydersoft.SwingTrade.Contracts.Sizer;

/// <summary>
/// The sizer output. A pure calculation: nothing is saved. Available cash is the dashboard figure
/// (starting capital + realized P&amp;L - open cost - planned cost).
/// </summary>
public sealed record SizerResultDto(
    string? Ticker,
    decimal Entry,
    decimal Stop,
    decimal Target,
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
    bool IsOk,
    string? Warning,
    decimal AvailableCash,
    int OpenCount);
