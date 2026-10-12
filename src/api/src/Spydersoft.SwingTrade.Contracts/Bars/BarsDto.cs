namespace Spydersoft.SwingTrade.Contracts.Bars;

public sealed record BarPointDto(DateOnly Date, decimal Close, decimal? Ma20, decimal? Ma50);

/// <summary>Completed daily closes, oldest first, with the 20 and 50 day simple averages ending at each bar.</summary>
public sealed record BarsDto(string Ticker, int Days, IReadOnlyList<BarPointDto> Bars);
