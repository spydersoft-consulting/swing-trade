using Microsoft.EntityFrameworkCore;
using Spydersoft.SwingTrade.Api.Refresh;
using Spydersoft.SwingTrade.Contracts.Watchlist;
using Spydersoft.SwingTrade.Data;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Api.Services;

public class WatchlistService(SwingTradeDbContext db, SettingsService settings, TradeService trades, MarketCalendar calendar)
{
    private static readonly string[] SortKeys = ["ticker", "signal", "pctvs20", "last", "asof"];

    /// <summary>One row per active ticker. Trend and Signal come from the Rules library under the current Settings.</summary>
    public async Task<ServiceResult<List<WatchlistRowDto>>> GetAsync(WatchlistQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new ValidationErrors();
        Signal? signalFilter = null;
        if (!string.IsNullOrWhiteSpace(query.Signal))
        {
            signalFilter = ParseSignal(query.Signal);
            if (signalFilter is null)
                errors.Add(nameof(query.Signal), $"Unknown signal '{query.Signal}'. Use a name such as PullbackCandidate or its text such as 'Pullback candidate'.");
        }

        var (sortKey, descending) = ParseSort(query.Sort);
        if (sortKey is null)
            errors.Add(nameof(query.Sort), $"Unknown sort '{query.Sort}'. Use {string.Join(", ", SortKeys)} (prefix '-' for descending).");
        if (errors.Any)
            return errors.ToResult<List<WatchlistRowDto>>();

        var s = await settings.GetRulesAsync(ct);
        var instruments = await db.Instruments.AsNoTracking().Where(i => i.Active).ToListAsync(ct);
        var tickers = instruments.Select(i => i.Ticker).ToList();
        var snapshots = (await db.IndicatorSnapshots.AsNoTracking().Where(x => tickers.Contains(x.Ticker)).ToListAsync(ct))
            .GroupBy(x => x.Ticker)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.AsOfDate).First());
        var closed = await trades.ClosedRefsAsync(ct);

        var rows = instruments.Select(i =>
        {
            snapshots.TryGetValue(i.Ticker, out var snap);
            var eval = snap is null
                ? new TrendSignalResult(Trend.InsufficientData, Signal.InsufficientData, null)
                : TrendSignal.Evaluate(new Indicators(snap.LastClose, snap.Ma20, snap.Ma50, snap.BarsUsed), s);
            return new WatchlistRowDto(
                i.Ticker, i.Name, i.Category, snap?.AsOfDate, snap?.LastClose, snap?.Ma20, snap?.Ma50, eval.PctVs20,
                eval.Trend, eval.Trend.Display(), eval.Signal, eval.Signal.Display(),
                WashSale.ReentryOkAfter(i.Ticker, i.WashSaleGroup, closed, s),
                calendar.IsStale(snap?.AsOfDate));
        });

        if (signalFilter is { } wanted)
            rows = rows.Where(r => r.Signal == wanted);

        return ServiceResult<List<WatchlistRowDto>>.Ok(Sort(rows, sortKey!, descending));
    }

    private static List<WatchlistRowDto> Sort(IEnumerable<WatchlistRowDto> rows, string key, bool descending)
    {
        if (key == "signal" && !descending)
        {
            // Default: Pullback candidates first, then by ticker.
            return rows.OrderBy(r => r.Signal == Signal.PullbackCandidate ? 0 : 1)
                .ThenBy(r => r.Ticker, StringComparer.Ordinal).ToList();
        }

        Func<WatchlistRowDto, object?> selector = key switch
        {
            "pctvs20" => r => r.PctVs20,
            "last" => r => r.Last,
            "asof" => r => r.AsOfDate,
            "signal" => r => r.Signal == Signal.PullbackCandidate ? 0 : 1,
            _ => r => r.Ticker
        };

        var comparer = Comparer<object?>.Create(Compare);
        var ordered = descending ? rows.OrderByDescending(selector, comparer) : rows.OrderBy(selector, comparer);
        return ordered.ThenBy(r => r.Ticker, StringComparer.Ordinal).ToList();
    }

    // Nulls sort first ascending.
    private static int Compare(object? a, object? b) =>
        (a, b) switch
        {
            (null, null) => 0,
            (null, _) => -1,
            (_, null) => 1,
            (string x, string y) => string.CompareOrdinal(x, y),
            (IComparable x, _) => x.CompareTo(b),
            _ => 0
        };

    private static (string? Key, bool Descending) ParseSort(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return ("signal", false);

        var raw = sort.Trim();
        var descending = raw.StartsWith('-');
        var key = raw.TrimStart('-', '+').ToLowerInvariant();
        return SortKeys.Contains(key) ? (key, descending) : (null, false);
    }

    internal static Signal? ParseSignal(string text)
    {
        var wanted = Normalize(text);
        foreach (var signal in Enum.GetValues<Signal>())
        {
            if (Normalize(signal.ToString()) == wanted || Normalize(signal.Display()) == wanted)
                return signal;
        }

        return null;
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
