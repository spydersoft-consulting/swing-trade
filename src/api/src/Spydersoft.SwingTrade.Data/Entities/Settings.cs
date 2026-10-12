using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Data.Entities;

/// <summary>The single settings row (Id is always 1). Percentages are fractions.</summary>
public class Settings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public decimal StartingCapital { get; set; }
    public decimal MaxRiskPct { get; set; }
    public int MaxOpenPositions { get; set; }
    public decimal MinRewardRisk { get; set; }
    public decimal TaxSetAsideRate { get; set; }
    public int WashSaleWindowDays { get; set; }
    public decimal MaxPositionPct { get; set; }
    public decimal PullbackBandLow { get; set; }
    public decimal PullbackBandHigh { get; set; }
    public decimal ExtendedThreshold { get; set; }
    public RiskBasis RiskBasis { get; set; }
}
