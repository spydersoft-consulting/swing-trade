using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Contracts.Trades;

public class TradeListQuery
{
    public TradeStatus? Status { get; set; }
    public string? Ticker { get; set; }
}
