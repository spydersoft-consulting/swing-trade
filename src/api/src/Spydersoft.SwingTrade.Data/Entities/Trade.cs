using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Data.Entities;

public class Trade
{
    public int Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public TradeStatus Status { get; set; }
    public TradeSetup Setup { get; set; }

    /// <summary>Null while Planned (EntryPrice is then the limit price).</summary>
    public DateOnly? OpenDate { get; set; }
    public int Shares { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal StopPrice { get; set; }
    public decimal TargetPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public DateOnly? ExitDate { get; set; }
    public bool? FollowedPlan { get; set; }
    public string? Rationale { get; set; }
    public string? Lessons { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
