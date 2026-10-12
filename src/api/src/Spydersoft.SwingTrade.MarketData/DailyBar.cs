namespace Spydersoft.SwingTrade.MarketData;

/// <summary>One trading day (New York exchange date). Close is split-adjusted, NOT dividend-adjusted.</summary>
public sealed record DailyBar(DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close, long Volume);
