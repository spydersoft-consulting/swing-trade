using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Contracts.Settings;

/// <summary>The single settings row. Percentages are fractions (0.015 means 1.5%).</summary>
public sealed record SettingsDto(
    decimal StartingCapital,
    decimal MaxRiskPct,
    int MaxOpenPositions,
    decimal MinRewardRisk,
    decimal TaxSetAsideRate,
    int WashSaleWindowDays,
    decimal MaxPositionPct,
    decimal PullbackBandLow,
    decimal PullbackBandHigh,
    decimal ExtendedThreshold,
    RiskBasis RiskBasis);
