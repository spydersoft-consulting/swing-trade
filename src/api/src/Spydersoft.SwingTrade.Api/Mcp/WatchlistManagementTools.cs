using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Spydersoft.SwingTrade.Api.Services;
using Spydersoft.SwingTrade.Contracts.Instruments;

namespace Spydersoft.SwingTrade.Api.Mcp;

/// <summary>
/// Adds tickers to the watchlist and activates or deactivates them. Registered only when <c>Mcp:EnableWatchlistManagement</c>
/// is on, and then only for callers who also hold <c>swingtrade.trader</c>. A new ticker has no prices until the next refresh.
/// </summary>
[McpServerToolType]
public class WatchlistManagementTools(InstrumentService instruments)
{
    public const string AddInstrumentToolName = "add_instrument";
    public const string SetInstrumentActiveToolName = "set_instrument_active";

    [McpServerTool(Name = AddInstrumentToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = SwingTradeAuth.Policies.Write)]
    [Description("Adds a ticker to the watchlist, active. It has no prices or signal until the next market-data refresh. Fails if the ticker already exists. Needs the trader role as well as the MCP role." + JournalTools.Notice)]
    public async Task<string> AddInstrument(
        [Description("Ticker symbol, for example XLE.")] string ticker,
        [Description("Display name, for example Energy Select Sector SPDR Fund.")] string name,
        [Description("Optional category.")] string? category = null,
        CancellationToken ct = default)
    {
        var created = McpEnvelopeFactory.Unwrap(
            await instruments.CreateAsync(new CreateInstrumentRequest { Ticker = ticker, Name = name, Category = category, Active = true }, ct), "Instrument");
        return System.Text.Json.JsonSerializer.Serialize(created, McpEnvelopeFactory.Json);
    }

    [McpServerTool(Name = SetInstrumentActiveToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = SwingTradeAuth.Policies.Write)]
    [Description("Activates or deactivates a watchlist ticker without changing its name, category or wash-sale group. A deactivated ticker drops out of the default watchlist and the refresh. Needs the trader role as well as the MCP role." + JournalTools.Notice)]
    public async Task<string> SetInstrumentActive(
        [Description("Ticker symbol.")] string ticker,
        [Description("True to activate, false to deactivate.")] bool active,
        CancellationToken ct = default)
    {
        var key = InstrumentService.Normalize(ticker);
        var current = (await instruments.ListAsync(null, ct)).FirstOrDefault(i => i.Ticker == key)
            ?? throw new McpException($"Ticker {key} was not found.");
        var updated = McpEnvelopeFactory.Unwrap(
            await instruments.UpdateAsync(key, new UpdateInstrumentRequest
            {
                Name = current.Name,
                Category = current.Category,
                Active = active,
                IsBenchmark = current.IsBenchmark,
                WashSaleGroup = current.WashSaleGroup
            }, ct), $"Ticker {key}");
        return System.Text.Json.JsonSerializer.Serialize(updated, McpEnvelopeFactory.Json);
    }
}
