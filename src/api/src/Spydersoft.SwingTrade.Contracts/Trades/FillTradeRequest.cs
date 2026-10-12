namespace Spydersoft.SwingTrade.Contracts.Trades;

/// <summary>Planned to Open. The fill price replaces the limit price as the entry.</summary>
public class FillTradeRequest
{
    public decimal FillPrice { get; set; }
    public DateOnly FillDate { get; set; }
}
