using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Contracts.Watchlist;

/// <summary>
/// One active ticker. Trend and Signal are derived on read from the current Settings; they are never stored.
/// <see cref="ReentryOkAfter"/> is null when there is no losing closed trade inside the wash-sale rule ("Clear").
/// </summary>
public sealed record WatchlistRowDto(
    string Ticker,
    string Name,
    string? Category,
    DateOnly? AsOfDate,
    decimal? Last,
    decimal? Ma20,
    decimal? Ma50,
    decimal? PctVs20,
    Trend Trend,
    string TrendText,
    Signal Signal,
    string SignalText,
    DateOnly? ReentryOkAfter,
    bool Stale);
