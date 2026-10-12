using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Contracts.Trades;

/// <summary>Stored fields followed by values computed on read by the rules library.</summary>
public sealed record TradeDto(
    int Id,
    string Ticker,
    TradeStatus Status,
    TradeSetup Setup,
    DateOnly? OpenDate,
    int Shares,
    decimal EntryPrice,
    decimal StopPrice,
    decimal TargetPrice,
    decimal? ExitPrice,
    DateOnly? ExitDate,
    bool? FollowedPlan,
    string? Rationale,
    string? Lessons,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    decimal Position,
    decimal RiskDollars,
    decimal RiskPct,
    decimal PlannedRewardRisk,
    decimal? Pnl,
    decimal? PnlPct,
    decimal? RMultiple,
    int? DaysHeld,
    bool RiskOverLimit,
    bool RewardRiskBelowMin,
    bool WashSaleFlag);
