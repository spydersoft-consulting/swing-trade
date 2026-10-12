namespace Spydersoft.SwingTrade.Data.Entities;

/// <summary>Trend and Signal are not stored; they are derived on read from current Settings.</summary>
public class IndicatorSnapshot
{
    public string Ticker { get; set; } = string.Empty;
    public DateOnly AsOfDate { get; set; }
    public decimal LastClose { get; set; }
    public decimal? Ma20 { get; set; }
    public decimal? Ma50 { get; set; }
    public int BarsUsed { get; set; }
}
