using System.ComponentModel.DataAnnotations;

namespace Spydersoft.SwingTrade.Contracts.Trades;

public class CloseTradeRequest
{
    public decimal ExitPrice { get; set; }
    public DateOnly ExitDate { get; set; }
    public bool? FollowedPlan { get; set; }

    [MaxLength(4000)]
    public string? Lessons { get; set; }
}
