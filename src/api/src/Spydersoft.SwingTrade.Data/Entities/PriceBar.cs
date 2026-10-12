namespace Spydersoft.SwingTrade.Data.Entities;

/// <summary>Append-only daily history. Close is split-adjusted, not dividend-adjusted.</summary>
public class PriceBar
{
    public string Ticker { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
}
