namespace Spydersoft.SwingTrade.Data.Entities;

public class Instrument
{
    public int Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public bool Active { get; set; } = true;
    public bool IsBenchmark { get; set; }
    public string? WashSaleGroup { get; set; }
}
