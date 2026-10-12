using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Contracts.Settings;

public class UpdateSettingsRequest
{
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
