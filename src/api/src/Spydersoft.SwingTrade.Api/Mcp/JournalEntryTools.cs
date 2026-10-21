using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Spydersoft.SwingTrade.Api.Services;
using Spydersoft.SwingTrade.Contracts.Notes;
using Spydersoft.SwingTrade.Contracts.Sizer;
using Spydersoft.SwingTrade.Contracts.Trades;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Api.Mcp;

/// <summary>
/// Step 5 of the weekly routine: record a pass or log the planned trade. Registered only when <c>Mcp:EnableJournalWrites</c>
/// is on, and then only for callers who also hold <c>swingtrade.trader</c> (the <c>Write</c> policy). Orders are never placed;
/// the owner places them at the broker.
/// </summary>
[McpServerToolType]
public class JournalEntryTools(NoteService notes, TradeService trades, SizerService sizer, TimeProvider clock)
{
    public const string RecordPassToolName = "record_pass";
    public const string LogPlannedTradeToolName = "log_planned_trade";

    [McpServerTool(Name = RecordPassToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = SwingTradeAuth.Policies.Write)]
    [Description("Records a pass in the journal: a note of kind Pass for a ticker you looked at and did not trade. Needs the trader role as well as the MCP role. It never changes trades, settings or prices." + JournalTools.Notice)]
    public async Task<string> RecordPass(
        [Description("Ticker symbol that was passed on.")] string ticker,
        [Description("Why the owner passed.")] string reason,
        [Description("Date, yyyy-MM-dd. Defaults to today in New York.")] string? date = null,
        CancellationToken ct = default)
    {
        var parsed = JournalTools.ParseDate(date, nameof(date)) ?? TodayInNewYork();
        var key = InstrumentService.Normalize(ticker);
        var created = McpEnvelopeFactory.Unwrap(
            await notes.CreateAsync(new CreateNoteRequest { Date = parsed, Kind = NoteKind.Pass, Text = reason, Ticker = key }, ct), "Note");
        return System.Text.Json.JsonSerializer.Serialize(created, McpEnvelopeFactory.Json);
    }

    [McpServerTool(Name = LogPlannedTradeToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = SwingTradeAuth.Policies.Write)]
    [Description("Logs a Planned trade (a limit order the owner has placed or is about to place) with the share count the position sizer returns for this entry, stop and target. Refused, and nothing is saved, when the sizer verdict is not OK. Needs the trader role as well as the MCP role. It does not place an order." + JournalTools.Notice)]
    public async Task<string> LogPlannedTrade(
        [Description("Ticker symbol; must already be on the watchlist.")] string ticker,
        [Description("Entry (limit) price.")] decimal entry,
        [Description("Stop price (must be below entry).")] decimal stop,
        [Description("Target price.")] decimal target,
        [Description("Pullback, Breakout, MeanReversion or Other (default Pullback).")] string setup = "Pullback",
        [Description("Optional rationale for the trade.")] string? rationale = null,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<TradeSetup>(setup?.Trim(), ignoreCase: true, out var parsedSetup) || !Enum.IsDefined(parsedSetup))
            throw new McpException($"Unknown setup '{setup}'. Use Pullback, Breakout, MeanReversion or Other.");

        var key = InstrumentService.Normalize(ticker);
        var sizing = McpEnvelopeFactory.Unwrap(
            await sizer.SizeAsync(new SizerRequest { Ticker = key, Entry = entry, Stop = stop, Target = target }, ct), "Sizer input");
        if (!sizing.IsOk)
            throw new McpException($"The sizer verdict is not OK ({sizing.Verdict}). Nothing was logged.");

        var trade = McpEnvelopeFactory.Unwrap(
            await trades.CreateAsync(new CreateTradeRequest
            {
                Ticker = key,
                Status = TradeStatus.Planned,
                Setup = parsedSetup,
                Shares = sizing.Shares,
                EntryPrice = entry,
                StopPrice = stop,
                TargetPrice = target,
                Rationale = rationale
            }, ct), "Trade");
        return System.Text.Json.JsonSerializer.Serialize(new { trade, sizing }, McpEnvelopeFactory.Json);
    }

    private DateOnly TodayInNewYork() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(BarCompletion.MarketTimeZoneId)).DateTime);
}
