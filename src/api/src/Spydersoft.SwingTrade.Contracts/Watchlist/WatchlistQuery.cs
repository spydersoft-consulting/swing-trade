namespace Spydersoft.SwingTrade.Contracts.Watchlist;

public class WatchlistQuery
{
    /// <summary>Filter by signal: the enum name ("PullbackCandidate") or its display text ("Pullback candidate"), case-insensitive.</summary>
    public string? Signal { get; set; }

    /// <summary>
    /// ticker, signal, pctVs20, last or asOf, optionally prefixed with "-" for descending. Default (omitted or "signal"):
    /// Pullback candidates first, then by ticker.
    /// </summary>
    public string? Sort { get; set; }
}
