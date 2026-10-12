namespace Spydersoft.SwingTrade.Rules;

public sealed record ClosedTradeRef(
    int Id,
    string Ticker,
    string? WashSaleGroup,
    DateOnly OpenDate,
    DateOnly ExitDate,
    decimal Pnl);

/// <summary>Informational only: never changes P&amp;L. Tax treatment is for the owner and a tax professional.</summary>
public static class WashSale
{
    /// <summary>
    /// Flagged when ANOTHER Closed trade for the same ticker (or the same WashSaleGroup) lost money and
    /// OpenDate - window &lt;= its ExitDate &lt;= OpenDate + window.
    /// </summary>
    public static bool IsFlagged(
        int candidateId,
        string ticker,
        string? washSaleGroup,
        DateOnly openDate,
        IEnumerable<ClosedTradeRef> closedTrades,
        RulesSettings s)
    {
        var from = openDate.AddDays(-s.WashSaleWindowDays);
        var to = openDate.AddDays(s.WashSaleWindowDays);

        return closedTrades.Any(c =>
            c.Id != candidateId
            && c.Pnl < 0m
            && SameInstrument(c, ticker, washSaleGroup)
            && c.ExitDate >= from
            && c.ExitDate <= to);
    }

    /// <summary>Latest losing ExitDate + window + 1 day, or null (shown as 'Clear') with no losing trade.</summary>
    public static DateOnly? ReentryOkAfter(
        string ticker,
        string? washSaleGroup,
        IEnumerable<ClosedTradeRef> closedTrades,
        RulesSettings s)
    {
        DateOnly? latest = null;
        foreach (var c in closedTrades)
        {
            if (c.Pnl < 0m && SameInstrument(c, ticker, washSaleGroup) && (latest is null || c.ExitDate > latest))
                latest = c.ExitDate;
        }

        return latest?.AddDays(s.WashSaleWindowDays + 1);
    }

    private static bool SameInstrument(ClosedTradeRef c, string ticker, string? group) =>
        string.Equals(c.Ticker, ticker, StringComparison.OrdinalIgnoreCase)
        || (group is not null && c.WashSaleGroup is not null
            && string.Equals(c.WashSaleGroup, group, StringComparison.OrdinalIgnoreCase));
}
