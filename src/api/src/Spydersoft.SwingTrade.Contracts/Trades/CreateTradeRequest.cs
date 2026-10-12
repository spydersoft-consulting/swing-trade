using System.ComponentModel.DataAnnotations;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Contracts.Trades;

/// <summary>Creates a Planned (limit order working) or Open trade.</summary>
public class CreateTradeRequest
{
    [MaxLength(16)]
    public string Ticker { get; set; } = string.Empty;

    public TradeStatus Status { get; set; } = TradeStatus.Planned;
    public TradeSetup Setup { get; set; } = TradeSetup.Pullback;
    public DateOnly? OpenDate { get; set; }
    public int Shares { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal StopPrice { get; set; }
    public decimal TargetPrice { get; set; }

    [MaxLength(4000)]
    public string? Rationale { get; set; }
}
