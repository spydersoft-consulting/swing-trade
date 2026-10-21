using System.ComponentModel;
using ModelContextProtocol.Server;
using Spydersoft.SwingTrade.Api.Services;
using Spydersoft.SwingTrade.Contracts.Trades;

namespace Spydersoft.SwingTrade.Api.Mcp;

/// <summary>
/// Keeps the journal in step with the broker after the owner acts there: fill, close or cancel. Registered only when
/// <c>Mcp:EnableTradeLifecycle</c> is on, and then only for callers who also hold <c>swingtrade.trader</c>. The status
/// rules are the trade service's (Planned to Open, Open to Closed, Planned to Cancelled); no order is ever placed.
/// </summary>
[McpServerToolType]
public class TradeLifecycleTools(TradeService trades, TimeProvider clock)
{
    public const string FillTradeToolName = "fill_trade";
    public const string CloseTradeToolName = "close_trade";
    public const string CancelTradeToolName = "cancel_trade";

    [McpServerTool(Name = FillTradeToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = SwingTradeAuth.Policies.McpWrite)]
    [Description("Marks a Planned trade as Open at the price the broker filled it. The fill price replaces the limit price. Only a Planned trade can be filled. Needs the trader role as well as the MCP role. It does not place an order." + JournalTools.Notice)]
    public async Task<string> FillTrade(
        [Description("Trade id.")] int id,
        [Description("Price the order filled at.")] decimal fillPrice,
        [Description("Fill date, yyyy-MM-dd. Defaults to today in New York.")] string? fillDate = null,
        CancellationToken ct = default)
    {
        var date = JournalTools.ParseDate(fillDate, nameof(fillDate)) ?? JournalTools.TodayInNewYork(clock);
        var trade = McpEnvelopeFactory.Unwrap(
            await trades.FillAsync(id, new FillTradeRequest { FillPrice = fillPrice, FillDate = date }, ct), $"Trade {id}");
        return System.Text.Json.JsonSerializer.Serialize(trade, McpEnvelopeFactory.Json);
    }

    [McpServerTool(Name = CloseTradeToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = SwingTradeAuth.Policies.McpWrite)]
    [Description("Closes an Open trade at the price the broker sold it, recording the P&L inputs. Only an Open trade can be closed. Needs the trader role as well as the MCP role. It does not place an order." + JournalTools.Notice)]
    public async Task<string> CloseTrade(
        [Description("Trade id.")] int id,
        [Description("Price the position was sold at.")] decimal exitPrice,
        [Description("Exit date, yyyy-MM-dd. Defaults to today in New York.")] string? exitDate = null,
        [Description("Whether the owner followed the plan.")] bool? followedPlan = null,
        [Description("Optional lessons learned.")] string? lessons = null,
        CancellationToken ct = default)
    {
        var date = JournalTools.ParseDate(exitDate, nameof(exitDate)) ?? JournalTools.TodayInNewYork(clock);
        var trade = McpEnvelopeFactory.Unwrap(
            await trades.CloseAsync(id, new CloseTradeRequest { ExitPrice = exitPrice, ExitDate = date, FollowedPlan = followedPlan, Lessons = lessons }, ct),
            $"Trade {id}");
        return System.Text.Json.JsonSerializer.Serialize(trade, McpEnvelopeFactory.Json);
    }

    [McpServerTool(Name = CancelTradeToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = SwingTradeAuth.Policies.McpWrite)]
    [Description("Cancels a Planned trade whose order did not fill. Only a Planned trade can be cancelled. Needs the trader role as well as the MCP role." + JournalTools.Notice)]
    public async Task<string> CancelTrade([Description("Trade id.")] int id, CancellationToken ct = default)
    {
        var trade = McpEnvelopeFactory.Unwrap(await trades.CancelAsync(id, ct), $"Trade {id}");
        return System.Text.Json.JsonSerializer.Serialize(trade, McpEnvelopeFactory.Json);
    }
}
