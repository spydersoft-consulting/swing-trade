namespace Spydersoft.SwingTrade.Contracts.Bars;

public class BarsQuery
{
    public const int DefaultDays = 120;
    public const int MaxDays = 120;

    /// <summary>Number of most recent completed bars returned; capped at 120.</summary>
    public int Days { get; set; } = DefaultDays;
}
