using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Spydersoft.SwingTrade.Api.Refresh;
using Spydersoft.SwingTrade.Api.Services;
using Spydersoft.SwingTrade.Contracts.Bars;
using Spydersoft.SwingTrade.Contracts.Notes;
using Spydersoft.SwingTrade.Contracts.Refresh;
using Spydersoft.SwingTrade.Contracts.Sizer;
using Spydersoft.SwingTrade.Contracts.Trades;
using Spydersoft.SwingTrade.Contracts.Watchlist;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Api.Mcp;

/// <summary>
/// Read-only tools over the same application services the REST controllers call. They compute nothing themselves:
/// every number comes from the services and the Rules library, and is returned as compact JSON in an envelope with
/// asOf, dataStatus and the Settings snapshot.
/// </summary>
[McpServerToolType]
public class JournalTools(
    DashboardService dashboard,
    WatchlistService watchlist,
    TradeService trades,
    BarService bars,
    SizerService sizer,
    SettingsService settings,
    NoteService notes,
    RefreshService refresh,
    RefreshSchedule schedule,
    McpEnvelopeFactory envelope)
{
    public const int MaxBars = BarsQuery.MaxDays;
    public const int MaxTrades = 100;
    public const int MaxNotes = 100;

    /// <summary>Appended to every tool description so the model frames its answer correctly.</summary>
    public const string Notice =
        " You are giving a review against the owner's own rules, not making decisions for them. Signals are informational, not investment advice. " +
        "Quote the numbers this tool returns exactly; do not calculate your own.";

    [McpServerTool(Name = "get_dashboard", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Realized P&L, account value, cash, cash reserved by planned orders, tax set-aside, win rate, average R, profit factor, open positions, capital at work, risk at stops, results by setup and the SPY comparison." + Notice)]
    public async Task<string> GetDashboard(CancellationToken ct) =>
        await envelope.WrapAsync(await dashboard.GetAsync(ct), ct: ct);

    [McpServerTool(Name = "get_watchlist", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("One row per ticker: last close, MA20, MA50, percent versus the 20-day average, Trend, Signal, re-entry date after a wash-sale loss, and a stale flag. Same values as the Watchlist screen, Pullback candidates first." + Notice)]
    public async Task<string> GetWatchlist(
        [Description("Only active tickers (default true). False also lists deactivated ones.")] bool activeOnly = true,
        CancellationToken ct = default)
    {
        var rows = McpEnvelopeFactory.Unwrap(await watchlist.GetAsync(new WatchlistQuery(), activeOnly, ct), "Watchlist");
        return await envelope.WrapAsync(rows, alsoStale: rows.Any(r => r.Stale), ct: ct);
    }

    [McpServerTool(Name = "get_instrument", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("One ticker: its watchlist row, any open or planned trade, wash-sale status, and its last 5 completed closes." + Notice)]
    public async Task<string> GetInstrument([Description("Ticker symbol, for example XLE.")] string ticker, CancellationToken ct = default)
    {
        var key = InstrumentService.Normalize(ticker);
        var rows = McpEnvelopeFactory.Unwrap(await watchlist.GetAsync(new WatchlistQuery(), activeOnly: false, ct), "Watchlist");
        var row = rows.FirstOrDefault(r => r.Ticker == key) ?? throw new McpException($"Ticker {key} was not found.");

        var open = (await trades.ListAsync(new TradeListQuery { Ticker = key }, ct))
            .Where(t => t.Status is TradeStatus.Open or TradeStatus.Planned)
            .ToList();
        var recent = McpEnvelopeFactory.Unwrap(await bars.GetAsync(key, new BarsQuery { Days = 5 }, ct), $"Ticker {key}");

        var data = new
        {
            row,
            openOrPlannedTrades = open,
            washSale = new { clear = row.ReentryOkAfter is null, row.ReentryOkAfter },
            last5Closes = recent.Bars.Select(b => new { b.Date, b.Close })
        };
        return await envelope.WrapAsync(data, key, row.Stale, ct);
    }

    [McpServerTool(Name = "get_bars", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Completed daily closes for one ticker with MA20 and MA50, oldest first. At most 120 bars; a larger request is clamped." + Notice)]
    public async Task<string> GetBars(
        [Description("Ticker symbol.")] string ticker,
        [Description("Number of most recent completed bars (1 to 120, default 120).")] int days = MaxBars,
        CancellationToken ct = default)
    {
        var key = InstrumentService.Normalize(ticker);
        var data = McpEnvelopeFactory.Unwrap(await bars.GetAsync(key, new BarsQuery { Days = Math.Clamp(days, 1, MaxBars) }, ct), $"Ticker {key}");
        return await envelope.WrapAsync(data, key, ct: ct);
    }

    [McpServerTool(Name = "get_levels", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Swing low and prior high for one ticker: the lowest and highest completed close in the lookback window, with their dates and the last close. Close-based (the app stores closes only). Use them as the stop and target candidates for size_position." + Notice)]
    public async Task<string> GetLevels(
        [Description("Ticker symbol.")] string ticker,
        [Description("Number of most recent completed bars to scan (5 to 120, default 20).")] int lookback = SwingLevelCalculator.DefaultLookback,
        CancellationToken ct = default)
    {
        var key = InstrumentService.Normalize(ticker);
        var series = McpEnvelopeFactory.Unwrap(await bars.GetAsync(key, new BarsQuery { Days = MaxBars }, ct), $"Ticker {key}").Bars;
        if (series.Count == 0)
            throw new McpException($"No completed bars are available for {key}.");

        var levels = SwingLevelCalculator.Calculate(series.Select(b => b.Close).ToList(), lookback);
        var data = new
        {
            last = series[^1].Close,
            lastDate = series[^1].Date,
            levels.Lookback,
            levels.BarsUsed,
            swingLow = new { price = levels.SwingLow, date = series[levels.SwingLowIndex].Date },
            priorHigh = new { price = levels.PriorHigh, date = series[levels.PriorHighIndex].Date }
        };
        return await envelope.WrapAsync(data, key, ct: ct);
    }

    [McpServerTool(Name = "list_trades", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Trades with computed fields: position, risk, planned reward:risk, P&L, percent, R-multiple, days held, wash-sale flag. Newest first, at most 100 per call (a larger limit is clamped); totalCount says how many matched. from/to filter on the open date (yyyy-MM-dd)." + Notice)]
    public async Task<string> ListTrades(
        [Description("Planned, Open, Closed or Cancelled.")] string? status = null,
        [Description("Ticker symbol.")] string? ticker = null,
        [Description("Earliest open date, yyyy-MM-dd.")] string? from = null,
        [Description("Latest open date, yyyy-MM-dd.")] string? to = null,
        [Description("Maximum trades returned (1 to 100, default 100).")] int limit = MaxTrades,
        CancellationToken ct = default)
    {
        var query = new TradeListQuery { Ticker = ticker };
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<TradeStatus>(status.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                throw new McpException($"Unknown status '{status}'. Use Planned, Open, Closed or Cancelled.");
            query.Status = parsed;
        }

        var fromDate = ParseDate(from, nameof(from));
        var toDate = ParseDate(to, nameof(to));
        IEnumerable<TradeDto> rows = await trades.ListAsync(query, ct);
        if (fromDate is not null) rows = rows.Where(t => t.OpenDate >= fromDate);
        if (toDate is not null) rows = rows.Where(t => t.OpenDate <= toDate);

        var all = rows.ToList();
        var page = all.Take(Math.Clamp(limit, 1, MaxTrades)).ToList();
        return await envelope.WrapAsync(new { totalCount = all.Count, returned = page.Count, trades = page }, ct: ct);
    }

    [McpServerTool(Name = "get_trade", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("One trade in full with its computed fields, plus the journal notes for the same ticker from the day the trade was opened. The trend and signal at entry are not recorded by the app, so entryTrendSignal is always null." + Notice)]
    public async Task<string> GetTrade([Description("Trade id.")] int id, CancellationToken ct = default)
    {
        var trade = McpEnvelopeFactory.Unwrap(await trades.GetAsync(id, ct), $"Trade {id}");
        var query = new NoteListQuery
        {
            Ticker = trade.Ticker,
            From = trade.OpenDate ?? DateOnly.FromDateTime(trade.CreatedAt.UtcDateTime),
            To = trade.ExitDate
        };
        var tradeNotes = await notes.ListAsync(query, ct);
        return await envelope.WrapAsync(new { trade, notes = tradeNotes, entryTrendSignal = (object?)null }, trade.Ticker, ct: ct);
    }

    [McpServerTool(Name = "size_position", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("The position sizer: shares by risk, cap and cash, cost, risk dollars, reward:risk, MaxEntryForMinRR, verdict (with a SKIP reason) and warning. Give entry, stop and target; or give a ticker and omit entry to use its last completed close as the entry. A pure calculation; nothing is saved." + Notice)]
    public async Task<string> SizePosition(
        [Description("Stop price (must be below entry).")] decimal stop,
        [Description("Target price.")] decimal target,
        [Description("Entry (limit) price. Omit with a ticker to use the last completed close.")] decimal? entry = null,
        [Description("Ticker symbol; optional when entry is given.")] string? ticker = null,
        CancellationToken ct = default)
    {
        var entrySource = "provided";
        var key = string.IsNullOrWhiteSpace(ticker) ? null : InstrumentService.Normalize(ticker);
        if (entry is null)
        {
            if (key is null)
                throw new McpException("Provide an entry price, or a ticker to use its last completed close.");
            var rows = McpEnvelopeFactory.Unwrap(await watchlist.GetAsync(new WatchlistQuery(), activeOnly: false, ct), "Watchlist");
            entry = rows.FirstOrDefault(r => r.Ticker == key)?.Last
                ?? throw new McpException($"No completed close is available for {key}; provide an entry price.");
            entrySource = "lastClose";
        }

        var result = McpEnvelopeFactory.Unwrap(
            await sizer.SizeAsync(new SizerRequest { Ticker = key, Entry = entry.Value, Stop = stop, Target = target }, ct), "Sizer input");
        return await envelope.WrapAsync(new { entrySource, sizing = result }, key, ct: ct);
    }

    [McpServerTool(Name = "get_settings", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("The rule thresholds: capital, risk percent, position cap, minimum reward:risk, tax rate, wash-sale window, signal band, extended threshold, risk basis and the moving-average lengths." + Notice)]
    public async Task<string> GetSettings(CancellationToken ct) =>
        await envelope.WrapAsync(new
        {
            settings = await settings.GetAsync(ct),
            movingAverageDays = new { @short = IndicatorCalculator.ShortWindow, @long = IndicatorCalculator.LongWindow }
        }, ct: ct);

    [McpServerTool(Name = "get_refresh_status", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("The last market-data refresh run, each ticker's as-of date and stale flag, and the next scheduled run." + Notice)]
    public async Task<string> GetRefreshStatus(CancellationToken ct)
    {
        var last = (await refresh.ListAsync(new RefreshRunListQuery { Page = 1, PageSize = 1 }, ct)).Items.FirstOrDefault();
        var rows = McpEnvelopeFactory.Unwrap(await watchlist.GetAsync(new WatchlistQuery(), activeOnly: true, ct), "Watchlist");
        var next = schedule.Next();
        var data = new
        {
            lastRun = (RefreshRunDto?)last,
            tickers = rows.Select(r => new { r.Ticker, r.AsOfDate, r.Stale }),
            nextScheduled = new { next.At, attempt = next.Attempt.ToString() }
        };
        return await envelope.WrapAsync(data, alsoStale: rows.Any(r => r.Stale), ct: ct);
    }

    [McpServerTool(Name = "list_notes", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Journal entries and the pass log, newest first, at most 100 per call. Filter by dates (yyyy-MM-dd) and kind (Pass, Review, General)." + Notice)]
    public async Task<string> ListNotes(
        [Description("Earliest date, yyyy-MM-dd.")] string? from = null,
        [Description("Latest date, yyyy-MM-dd.")] string? to = null,
        [Description("Pass, Review or General.")] string? kind = null,
        CancellationToken ct = default)
    {
        var query = new NoteListQuery { From = ParseDate(from, nameof(from)), To = ParseDate(to, nameof(to)) };
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!Enum.TryParse<NoteKind>(kind.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                throw new McpException($"Unknown kind '{kind}'. Use Pass, Review or General.");
            query.Kind = parsed;
        }

        var all = await notes.ListAsync(query, ct);
        return await envelope.WrapAsync(new { totalCount = all.Count, notes = all.Take(MaxNotes).ToList() }, ct: ct);
    }

    internal static DateOnly TodayInNewYork(TimeProvider clock) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(BarCompletion.MarketTimeZoneId)).DateTime);

    internal static DateOnly? ParseDate(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : throw new McpException($"{name} must be a date in yyyy-MM-dd format.");
    }
}

/// <summary>
/// The only write tool, registered only when <c>Mcp:EnableAddNote</c> is on, and then only for callers who also hold
/// <c>swingtrade.trader</c> (the <c>Write</c> policy). It appends a journal note through the existing notes service.
/// </summary>
[McpServerToolType]
public class JournalWriteTools(NoteService notes, TradeService trades)
{
    public const string AddNoteToolName = "add_note";

    [McpServerTool(Name = AddNoteToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = SwingTradeAuth.Policies.Write)]
    [Description("Appends one journal note (the only write tool). Needs the trader role as well as the MCP role. It never changes trades, settings or prices." + JournalTools.Notice)]
    public async Task<string> AddNote(
        [Description("Note date, yyyy-MM-dd.")] string date,
        [Description("Pass, Review or General.")] string kind,
        [Description("The note text.")] string text,
        [Description("Optional trade id; the note is filed under that trade's ticker.")] int? tradeId = null,
        CancellationToken ct = default)
    {
        var parsedDate = JournalTools.ParseDate(date, nameof(date)) ?? throw new McpException("date is required (yyyy-MM-dd).");
        if (!Enum.TryParse<NoteKind>(kind?.Trim(), ignoreCase: true, out var parsedKind) || !Enum.IsDefined(parsedKind))
            throw new McpException($"Unknown kind '{kind}'. Use Pass, Review or General.");

        string? ticker = null;
        if (tradeId is { } id)
            ticker = McpEnvelopeFactory.Unwrap(await trades.GetAsync(id, ct), $"Trade {id}").Ticker;

        var created = McpEnvelopeFactory.Unwrap(
            await notes.CreateAsync(new CreateNoteRequest { Date = parsedDate, Kind = parsedKind, Text = text, Ticker = ticker }, ct), "Note");
        return System.Text.Json.JsonSerializer.Serialize(created, McpEnvelopeFactory.Json);
    }
}
