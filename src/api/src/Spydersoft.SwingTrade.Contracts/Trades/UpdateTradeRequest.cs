using System.ComponentModel.DataAnnotations;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Contracts.Trades;

/// <summary>Edits a trade in place. Status changes go through fill, close and cancel.</summary>
public class UpdateTradeRequest
{
    [MaxLength(16)]
    public string Ticker { get; set; } = string.Empty;

    public TradeSetup Setup { get; set; } = TradeSetup.Pullback;
    public DateOnly? OpenDate { get; set; }
    public int Shares { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal StopPrice { get; set; }
    public decimal TargetPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public DateOnly? ExitDate { get; set; }
    public bool? FollowedPlan { get; set; }

    [MaxLength(4000)]
    public string? Rationale { get; set; }

    [MaxLength(4000)]
    public string? Lessons { get; set; }
}
