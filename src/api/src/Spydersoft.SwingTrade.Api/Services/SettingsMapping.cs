using Spydersoft.SwingTrade.Contracts.Settings;
using Spydersoft.SwingTrade.Data.Entities;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Api.Services;

internal static class SettingsMapping
{
    public static RulesSettings ToRules(Settings s) => new(
        s.StartingCapital, s.MaxRiskPct, s.MaxOpenPositions, s.MinRewardRisk, s.TaxSetAsideRate,
        s.WashSaleWindowDays, s.MaxPositionPct, s.PullbackBandLow, s.PullbackBandHigh,
        s.ExtendedThreshold, s.RiskBasis);

    public static SettingsDto ToDto(Settings s) => new(
        s.StartingCapital, s.MaxRiskPct, s.MaxOpenPositions, s.MinRewardRisk, s.TaxSetAsideRate,
        s.WashSaleWindowDays, s.MaxPositionPct, s.PullbackBandLow, s.PullbackBandHigh,
        s.ExtendedThreshold, s.RiskBasis);

    public static SettingsDto ToDto(RulesSettings s) => new(
        s.StartingCapital, s.MaxRiskPct, s.MaxOpenPositions, s.MinRewardRisk, s.TaxSetAsideRate,
        s.WashSaleWindowDays, s.MaxPositionPct, s.PullbackBandLow, s.PullbackBandHigh,
        s.ExtendedThreshold, s.RiskBasis);
}
