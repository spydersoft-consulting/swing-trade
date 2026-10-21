using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spydersoft.SwingTrade.Api.Mcp;
using Spydersoft.SwingTrade.Api.UnitTests.Support;
using Spydersoft.SwingTrade.Data;
using Spydersoft.SwingTrade.Data.Entities;
using Spydersoft.SwingTrade.Rules;
using static Spydersoft.SwingTrade.Api.UnitTests.Support.McpTestHost;

namespace Spydersoft.SwingTrade.Api.UnitTests.Mcp;

public class McpTests
{
    private static readonly string[] ReadTools =
    [
        "get_dashboard", "get_watchlist", "get_instrument", "get_bars", "list_trades", "get_trade",
        "size_position", "get_settings", "get_refresh_status", "list_notes", "get_levels"
    ];

    private static readonly DateOnly Friday = new(2026, 10, 9);

    private static string[] McpOnly => [SwingTradeAuth.Roles.Mcp];
    private static string[] McpAndTrader => [SwingTradeAuth.Roles.Mcp, SwingTradeAuth.Roles.Trader];

    private static async Task SeedMarketAsync(SwingTradeDbContext db)
    {
        // XLE is a pullback shape, QQQ extended, SPY the benchmark; enough bars for bars/closes tests.
        var date = Friday;
        var bars = new List<PriceBar>();
        for (var i = 0; i < 150; i++)
        {
            while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) date = date.AddDays(-1);
            bars.Add(new PriceBar { Ticker = "XLE", Date = date, Open = 60 + i, High = 61 + i, Low = 59 + i, Close = 60m + (150 - i) / 10m, Volume = 1000 });
            date = date.AddDays(-1);
        }

        db.PriceBars.AddRange(bars);
        db.IndicatorSnapshots.AddRange(
            new IndicatorSnapshot { Ticker = "XLE", AsOfDate = Friday, LastClose = 62.82m, Ma20 = 63.47m, Ma50 = 62.12m, BarsUsed = 50 },
            new IndicatorSnapshot { Ticker = "QQQ", AsOfDate = Friday, LastClose = 751.27m, Ma20 = 737.54m, Ma50 = 725.27m, BarsUsed = 50 },
            new IndicatorSnapshot { Ticker = "SPY", AsOfDate = Friday, LastClose = 778.57m, Ma20 = 767.98m, Ma50 = 767.99m, BarsUsed = 50 });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonNode?> RestAsync(McpTestHost host, string path)
    {
        var client = host.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token("reader-sub", [SwingTradeAuth.Roles.Reader]));
        var response = await client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), path);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonNode?> ToolDataAsync(McpTestHost host, string tool, object? args = null)
    {
        var payload = ToolPayload(await ToolAsync(host.CreateClient(), tool, args));
        return JsonNode.Parse(payload.GetProperty("data").GetRawText());
    }

    private static async Task<JsonElement> ToolEnvelopeAsync(McpTestHost host, string tool, object? args = null) =>
        ToolPayload(await ToolAsync(host.CreateClient(), tool, args));

    private static void AssertSame(JsonNode? actual, JsonNode? expected) =>
        Assert.That(JsonNode.DeepEquals(actual, expected), Is.True, $"MCP: {actual}\nREST: {expected}");

    // ---------------------------------------------------------------------------------------------- tools/list

    private static async Task<string[]> ListToolNamesAsync(McpTestHost host, string? token = null)
    {
        var rpc = await CallAsync(host.CreateClient(), "tools/list", new { }, token);
        return rpc.GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()!).ToArray();
    }

    [Test]
    public async Task ToolsList_AddNoteFlagOff_IsExactlyTheReadTools()
    {
        await using var host = await StartAsync();
        Assert.That(await ListToolNamesAsync(host), Is.EquivalentTo(ReadTools));
        Assert.That(await ListToolNamesAsync(host, Token(roles: McpAndTrader)), Is.EquivalentTo(ReadTools));
    }

    [Test]
    public async Task ToolsList_AddNoteFlagOn_AddsAddNoteOnlyForCallersWhoAlsoHoldTrader()
    {
        await using var host = await StartAsync(new() { ["Mcp:EnableAddNote"] = "true" });
        Assert.That(await ListToolNamesAsync(host, Token(roles: McpAndTrader)), Is.EquivalentTo(ReadTools.Append("add_note")));
        Assert.That(await ListToolNamesAsync(host, Token(roles: McpOnly)), Is.EquivalentTo(ReadTools));
    }

    [Test]
    public async Task ToolDescriptions_SayItIsAReview_NotAdvice_AndToQuoteNumbers()
    {
        await using var host = await StartAsync();
        var rpc = await CallAsync(host.CreateClient(), "tools/list", new { });
        foreach (var tool in rpc.GetProperty("result").GetProperty("tools").EnumerateArray())
        {
            var description = tool.GetProperty("description").GetString()!;
            Assert.That(description, Does.Contain("review against the owner's own rules"), tool.GetProperty("name").GetString());
            Assert.That(description, Does.Contain("not investment advice"));
            Assert.That(description, Does.Contain("Quote the numbers"));
        }
    }

    // ---------------------------------------------------------------------------------------------- add_note

    [Test]
    public async Task AddNote_WithTraderAndMcp_AppendsOnlyANote()
    {
        await using var host = await StartAsync(new() { ["Mcp:EnableAddNote"] = "true" });
        var rpc = await ToolAsync(host.CreateClient(), "add_note",
            new { date = "2026-10-12", kind = "Review", text = "Looked at XLE.", tradeId = 1 }, Token(roles: McpAndTrader));
        var note = ToolPayload(rpc);
        Assert.That(note.GetProperty("kind").GetString(), Is.EqualTo("Review"));
        Assert.That(note.GetProperty("ticker").GetString(), Is.EqualTo("XLE"));
        Assert.That(await host.WithDbAsync(db => db.JournalNotes.CountAsync()), Is.EqualTo(1));
    }

    [Test]
    public async Task AddNote_ForMcpOnlyRole_IsDeniedAndWritesNothing()
    {
        await using var host = await StartAsync(new() { ["Mcp:EnableAddNote"] = "true" });
        var rpc = await CallAsync(host.CreateClient(), "tools/call",
            new { name = "add_note", arguments = new { date = "2026-10-12", kind = "Pass", text = "x" } }, Token(roles: McpOnly));
        Assert.That(rpc.TryGetProperty("error", out _) || rpc.GetProperty("result").GetProperty("isError").GetBoolean(), Is.True, rpc.ToString());
        Assert.That(await host.WithDbAsync(db => db.JournalNotes.CountAsync()), Is.Zero);
    }

    [Test]
    public async Task AddNote_FlagOff_IsUnknown()
    {
        await using var host = await StartAsync();
        var rpc = await CallAsync(host.CreateClient(), "tools/call",
            new { name = "add_note", arguments = new { date = "2026-10-12", kind = "Pass", text = "x" } }, Token(roles: McpAndTrader));
        Assert.That(rpc.TryGetProperty("error", out _) || rpc.GetProperty("result").GetProperty("isError").GetBoolean(), Is.True, rpc.ToString());
        Assert.That(await host.WithDbAsync(db => db.JournalNotes.CountAsync()), Is.Zero);
    }

    // ---------------------------------------------------------------------------------------------- journal writes

    private static readonly string[] JournalWriteTools = ["record_pass", "log_planned_trade"];

    [Test]
    public async Task ToolsList_JournalWritesFlagOn_AddsTheTwoToolsOnlyForCallersWhoAlsoHoldTrader()
    {
        await using var host = await StartAsync(new() { ["Mcp:EnableJournalWrites"] = "true" });
        Assert.That(await ListToolNamesAsync(host, Token(roles: McpAndTrader)), Is.EquivalentTo(ReadTools.Concat(JournalWriteTools)));
        Assert.That(await ListToolNamesAsync(host, Token(roles: McpOnly)), Is.EquivalentTo(ReadTools));
    }

    [Test]
    public async Task RecordPass_WritesAPassNoteForTheTicker_DefaultingTheDate()
    {
        await using var host = await StartAsync(new() { ["Mcp:EnableJournalWrites"] = "true" }, SeedMarketAsync);
        var note = ToolPayload(await ToolAsync(host.CreateClient(), "record_pass",
            new { ticker = "xle", reason = "Extended, waiting." }, Token(roles: McpAndTrader)));
        Assert.Multiple(() =>
        {
            Assert.That(note.GetProperty("kind").GetString(), Is.EqualTo("Pass"));
            Assert.That(note.GetProperty("ticker").GetString(), Is.EqualTo("XLE"));
            Assert.That(note.GetProperty("date").GetString(), Is.Not.Empty);
        });
        Assert.That(await host.WithDbAsync(db => db.JournalNotes.CountAsync()), Is.EqualTo(1));
    }

    [Test]
    public async Task LogPlannedTrade_UsesTheSizerShares_AndSavesAPlannedTrade()
    {
        await using var host = await StartAsync(new() { ["Mcp:EnableJournalWrites"] = "true" }, SeedMarketAsync);
        var before = await host.WithDbAsync(db => db.Trades.CountAsync());
        var result = ToolPayload(await ToolAsync(host.CreateClient(), "log_planned_trade",
            new { ticker = "XLE", entry = 62.82m, stop = 61.40m, target = 65.90m, rationale = "Pullback to MA20." }, Token(roles: McpAndTrader)));

        var trade = result.GetProperty("trade");
        Assert.Multiple(() =>
        {
            Assert.That(trade.GetProperty("status").GetString(), Is.EqualTo("Planned"));
            Assert.That(trade.GetProperty("shares").GetInt32(), Is.EqualTo(result.GetProperty("sizing").GetProperty("shares").GetInt32()));
            Assert.That(trade.GetProperty("shares").GetInt32(), Is.GreaterThan(0));
        });
        Assert.That(await host.WithDbAsync(db => db.Trades.CountAsync()), Is.EqualTo(before + 1));
    }

    [Test]
    public async Task LogPlannedTrade_WhenTheSizerSaysSkip_SavesNothing()
    {
        await using var host = await StartAsync(new() { ["Mcp:EnableJournalWrites"] = "true" }, SeedMarketAsync);
        var before = await host.WithDbAsync(db => db.Trades.CountAsync());
        var rpc = await ToolAsync(host.CreateClient(), "log_planned_trade",
            new { ticker = "XLE", entry = 62.82m, stop = 61.40m, target = 62.90m }, Token(roles: McpAndTrader));
        Assert.That(rpc.GetProperty("result").GetProperty("isError").GetBoolean(), Is.True);
        Assert.That(await host.WithDbAsync(db => db.Trades.CountAsync()), Is.EqualTo(before));
    }

    [Test]
    public async Task JournalWrites_ForMcpOnlyRole_AreDeniedAndWriteNothing()
    {
        await using var host = await StartAsync(new() { ["Mcp:EnableJournalWrites"] = "true" }, SeedMarketAsync);
        var before = await host.WithDbAsync(db => db.Trades.CountAsync());
        foreach (var (tool, args) in new (string, object)[]
                 {
                     ("record_pass", new { ticker = "XLE", reason = "x" }),
                     ("log_planned_trade", new { ticker = "XLE", entry = 62.82m, stop = 61.40m, target = 65.90m })
                 })
        {
            var rpc = await CallAsync(host.CreateClient(), "tools/call", new { name = tool, arguments = args }, Token(roles: McpOnly));
            Assert.That(rpc.TryGetProperty("error", out _) || rpc.GetProperty("result").GetProperty("isError").GetBoolean(), Is.True, rpc.ToString());
        }

        Assert.That(await host.WithDbAsync(db => db.JournalNotes.CountAsync()), Is.Zero);
        Assert.That(await host.WithDbAsync(db => db.Trades.CountAsync()), Is.EqualTo(before));
    }

    // ---------------------------------------------------------------------------------------------- trade lifecycle

    private static readonly string[] LifecycleTools = ["fill_trade", "close_trade", "cancel_trade"];
    private static readonly Dictionary<string, string?> LifecycleOn = new() { ["Mcp:EnableTradeLifecycle"] = "true" };

    private static async Task AddTradesAsync(SwingTradeDbContext db)
    {
        db.Trades.AddRange(
            new Trade
            {
                Ticker = "XLE", Status = TradeStatus.Planned, Setup = TradeSetup.Pullback, Shares = 10, EntryPrice = 62.82m, StopPrice = 61.40m,
                TargetPrice = 65.90m, CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch
            },
            new Trade
            {
                Ticker = "XLE", Status = TradeStatus.Open, Setup = TradeSetup.Pullback, OpenDate = Friday, Shares = 10, EntryPrice = 62.82m,
                StopPrice = 61.40m, TargetPrice = 65.90m, CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch
            });
        await db.SaveChangesAsync();
    }

    private static async Task<(int Planned, int Open)> TradeIdsAsync(McpTestHost host) =>
        (await host.WithDbAsync(db => db.Trades.Where(t => t.Status == TradeStatus.Planned).Select(t => t.Id).SingleAsync()),
         await host.WithDbAsync(db => db.Trades.Where(t => t.Status == TradeStatus.Open && t.Shares == 10).Select(t => t.Id).SingleAsync()));

    [Test]
    public async Task ToolsList_TradeLifecycleFlagOn_AddsTheThreeToolsOnlyForCallersWhoAlsoHoldTrader()
    {
        await using var host = await StartAsync(LifecycleOn);
        Assert.That(await ListToolNamesAsync(host, Token(roles: McpAndTrader)), Is.EquivalentTo(ReadTools.Concat(LifecycleTools)));
        Assert.That(await ListToolNamesAsync(host, Token(roles: McpOnly)), Is.EquivalentTo(ReadTools));
    }

    [Test]
    public async Task FillTrade_OpensAPlannedTrade_AtTheFillPrice()
    {
        await using var host = await StartAsync(LifecycleOn, async db => { await SeedMarketAsync(db); await AddTradesAsync(db); });
        var (planned, _) = await TradeIdsAsync(host);
        var trade = ToolPayload(await ToolAsync(host.CreateClient(), "fill_trade",
            new { id = planned, fillPrice = 62.75m, fillDate = "2026-10-12" }, Token(roles: McpAndTrader)));
        Assert.Multiple(() =>
        {
            Assert.That(trade.GetProperty("status").GetString(), Is.EqualTo("Open"));
            Assert.That(trade.GetProperty("entryPrice").GetDecimal(), Is.EqualTo(62.75m));
            Assert.That(trade.GetProperty("openDate").GetString(), Is.EqualTo("2026-10-12"));
        });
    }

    [Test]
    public async Task CloseTrade_ClosesAnOpenTrade_AndRecordsTheExit()
    {
        await using var host = await StartAsync(LifecycleOn, async db => { await SeedMarketAsync(db); await AddTradesAsync(db); });
        var (_, open) = await TradeIdsAsync(host);
        var trade = ToolPayload(await ToolAsync(host.CreateClient(), "close_trade",
            new { id = open, exitPrice = 65.90m, exitDate = "2026-10-20", followedPlan = true, lessons = "Held to target." }, Token(roles: McpAndTrader)));
        Assert.Multiple(() =>
        {
            Assert.That(trade.GetProperty("status").GetString(), Is.EqualTo("Closed"));
            Assert.That(trade.GetProperty("exitPrice").GetDecimal(), Is.EqualTo(65.90m));
            Assert.That(trade.GetProperty("followedPlan").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task CancelTrade_CancelsAPlannedTrade()
    {
        await using var host = await StartAsync(LifecycleOn, async db => { await SeedMarketAsync(db); await AddTradesAsync(db); });
        var (planned, _) = await TradeIdsAsync(host);
        var trade = ToolPayload(await ToolAsync(host.CreateClient(), "cancel_trade", new { id = planned }, Token(roles: McpAndTrader)));
        Assert.That(trade.GetProperty("status").GetString(), Is.EqualTo("Cancelled"));
    }

    [Test]
    public async Task Lifecycle_WrongStatus_IsAnErrorAndChangesNothing()
    {
        await using var host = await StartAsync(LifecycleOn, async db => { await SeedMarketAsync(db); await AddTradesAsync(db); });
        var (planned, open) = await TradeIdsAsync(host);
        var client = host.CreateClient();
        foreach (var (tool, args) in new (string, object)[]
                 {
                     ("fill_trade", new { id = open, fillPrice = 62m }),
                     ("close_trade", new { id = planned, exitPrice = 65m }),
                     ("cancel_trade", new { id = open })
                 })
        {
            var rpc = await ToolAsync(client, tool, args, Token(roles: McpAndTrader));
            Assert.That(rpc.GetProperty("result").GetProperty("isError").GetBoolean(), Is.True, tool);
        }

        Assert.That(await host.WithDbAsync(db => db.Trades.CountAsync(t => t.Status == TradeStatus.Planned)), Is.EqualTo(1));
        Assert.That(await host.WithDbAsync(db => db.Trades.CountAsync(t => t.Status == TradeStatus.Open && t.Shares == 10)), Is.EqualTo(1));
    }

    [Test]
    public async Task Lifecycle_ForMcpOnlyRole_IsDeniedAndChangesNothing()
    {
        await using var host = await StartAsync(LifecycleOn, async db => { await SeedMarketAsync(db); await AddTradesAsync(db); });
        var (planned, _) = await TradeIdsAsync(host);
        var rpc = await CallAsync(host.CreateClient(), "tools/call",
            new { name = "cancel_trade", arguments = new { id = planned } }, Token(roles: McpOnly));
        Assert.That(rpc.TryGetProperty("error", out _) || rpc.GetProperty("result").GetProperty("isError").GetBoolean(), Is.True, rpc.ToString());
        Assert.That(await host.WithDbAsync(db => db.Trades.CountAsync(t => t.Status == TradeStatus.Planned)), Is.EqualTo(1));
    }

    // ---------------------------------------------------------------------------------------------- results equal REST

    [Test]
    public async Task GetDashboard_EqualsRest_AndCarriesCashAndTaxSetAside()
    {
        await using var host = await StartAsync(arrange: SeedMarketAsync);
        var data = await ToolDataAsync(host, "get_dashboard");
        AssertSame(data, await RestAsync(host, "/api/v1/dashboard"));
        Assert.That((decimal)data!["availableCash"]!, Is.EqualTo(2053.55m));
        Assert.That((decimal)data["reservedByPlanned"]!, Is.Zero);
        Assert.That((decimal)data["taxSetAside"]!, Is.EqualTo(14.4585m));
        Assert.That(data.AsObject().ContainsKey("expectancy"), Is.False);
        Assert.That(data.AsObject().ContainsKey("drawdown"), Is.False);
    }

    [Test]
    public async Task GetWatchlist_EqualsRest_AndActiveOnlyFalseAddsInactive()
    {
        await using var host = await StartAsync(arrange: SeedMarketAsync);
        AssertSame(await ToolDataAsync(host, "get_watchlist"), await RestAsync(host, "/api/v1/watchlist"));
        Assert.That((await ToolDataAsync(host, "get_watchlist"))!.AsArray().Count, Is.EqualTo(12));
        Assert.That((await ToolDataAsync(host, "get_watchlist", new { activeOnly = false }))!.AsArray().Count, Is.EqualTo(24));
    }

    [Test]
    public async Task GetInstrument_ReturnsRowTradesWashSaleAndLastFiveCloses()
    {
        await using var host = await StartAsync(arrange: SeedMarketAsync);
        var data = (await ToolDataAsync(host, "get_instrument", new { ticker = "xle" }))!;
        var rest = await RestAsync(host, "/api/v1/watchlist");
        AssertSame(data["row"], rest!.AsArray().Single(r => (string?)r!["ticker"] == "XLE"));
        Assert.That(data["last5Closes"]!.AsArray().Count, Is.EqualTo(5));
        Assert.That((bool)data["washSale"]!["clear"]!, Is.True);
        Assert.That(data["openOrPlannedTrades"]!.AsArray(), Is.Empty);
    }

    [Test]
    public async Task GetInstrument_UnknownTicker_IsAnErrorResult()
    {
        await using var host = await StartAsync(arrange: SeedMarketAsync);
        var result = (await ToolAsync(host.CreateClient(), "get_instrument", new { ticker = "NOPE" })).GetProperty("result");
        Assert.That(result.GetProperty("isError").GetBoolean(), Is.True);
    }

    [Test]
    public async Task GetBars_EqualsRest_AndIsCappedAt120()
    {
        await using var host = await StartAsync(arrange: SeedMarketAsync);
        var data = await ToolDataAsync(host, "get_bars", new { ticker = "XLE", days = 5000 });
        Assert.That(data!["bars"]!.AsArray().Count, Is.EqualTo(120));
        AssertSame(data, await RestAsync(host, "/api/v1/instruments/XLE/bars?days=120"));
        Assert.That((await ToolDataAsync(host, "get_bars", new { ticker = "XLE", days = 7 }))!["bars"]!.AsArray().Count, Is.EqualTo(7));
    }

    [Test]
    public async Task GetLevels_ReturnsLowestAndHighestCloseOfTheLookback_WithDates()
    {
        await using var host = await StartAsync(arrange: SeedMarketAsync);
        var data = await ToolDataAsync(host, "get_levels", new { ticker = "XLE", lookback = 10 });
        var closes = (await ToolDataAsync(host, "get_bars", new { ticker = "XLE", days = 10 }))!["bars"]!.AsArray()
            .Select(b => (Date: (string)b!["date"]!, Close: (decimal)b["close"]!)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That((decimal)data!["swingLow"]!["price"]!, Is.EqualTo(closes.Min(c => c.Close)));
            Assert.That((decimal)data["priorHigh"]!["price"]!, Is.EqualTo(closes.Max(c => c.Close)));
            Assert.That((decimal)data["last"]!, Is.EqualTo(closes[^1].Close));
            Assert.That((int)data["barsUsed"]!, Is.EqualTo(10));
        });
    }

    [Test]
    public async Task GetLevels_UnknownTicker_IsAnError()
    {
        await using var host = await StartAsync(arrange: SeedMarketAsync);
        var rpc = await ToolAsync(host.CreateClient(), "get_levels", new { ticker = "NOPE" });
        Assert.That(rpc.GetProperty("result").GetProperty("isError").GetBoolean(), Is.True);
    }

    [Test]
    public async Task ListTrades_EqualsRest_AndIsCappedAt100()
    {
        await using var host = await StartAsync(arrange: async db =>
        {
            for (var i = 0; i < 120; i++)
            {
                db.Trades.Add(new Trade
                {
                    Ticker = "XLK", Status = TradeStatus.Closed, Setup = TradeSetup.Pullback, OpenDate = new DateOnly(2026, 1, 1).AddDays(i),
                    Shares = 1, EntryPrice = 10, StopPrice = 9, TargetPrice = 13, ExitPrice = 11, ExitDate = new DateOnly(2026, 1, 2).AddDays(i),
                    CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch
                });
            }

            await db.SaveChangesAsync();
        });

        var data = (await ToolDataAsync(host, "list_trades", new { limit = 5000 }))!;
        Assert.That((int)data["totalCount"]!, Is.EqualTo(121));
        Assert.That(data["trades"]!.AsArray().Count, Is.EqualTo(100));

        var xle = (await ToolDataAsync(host, "list_trades", new { ticker = "XLE", status = "closed" }))!;
        AssertSame(xle["trades"], await RestAsync(host, "/api/v1/trades?ticker=XLE&status=Closed"));

        var ranged = (await ToolDataAsync(host, "list_trades", new { from = "2026-01-10", to = "2026-01-12", ticker = "XLK" }))!;
        Assert.That(ranged["trades"]!.AsArray().Count, Is.EqualTo(3));
    }

    [Test]
    public async Task ListTrades_BadStatusOrDate_IsAnErrorResult()
    {
        await using var host = await StartAsync();
        var client = host.CreateClient();
        Assert.That((await ToolAsync(client, "list_trades", new { status = "Bogus" })).GetProperty("result").GetProperty("isError").GetBoolean(), Is.True);
        Assert.That((await ToolAsync(client, "list_trades", new { from = "10/12/2026" })).GetProperty("result").GetProperty("isError").GetBoolean(), Is.True);
    }

    [Test]
    public async Task GetTrade_EqualsRestTrade_WithNotes()
    {
        await using var host = await StartAsync(arrange: async db =>
        {
            db.JournalNotes.Add(new JournalNote { Date = new DateOnly(2026, 10, 6), Ticker = "XLE", Kind = Spydersoft.SwingTrade.Contracts.Notes.NoteKind.Review, Text = "held the dip" });
            await db.SaveChangesAsync();
        });
        var data = (await ToolDataAsync(host, "get_trade", new { id = 1 }))!;
        AssertSame(data["trade"], await RestAsync(host, "/api/v1/trades/1"));
        Assert.That(data["notes"]!.AsArray().Count, Is.EqualTo(1));
        Assert.That(data["entryTrendSignal"], Is.Null);
        Assert.That((await ToolAsync(host.CreateClient(), "get_trade", new { id = 999 })).GetProperty("result").GetProperty("isError").GetBoolean(), Is.True);
    }

    [Test]
    public async Task GetSettings_EqualsRestSettings()
    {
        await using var host = await StartAsync();
        var data = (await ToolDataAsync(host, "get_settings"))!;
        AssertSame(data["settings"], await RestAsync(host, "/api/v1/settings"));
        Assert.That((int)data["movingAverageDays"]!["short"]!, Is.EqualTo(IndicatorCalculator.ShortWindow));
        Assert.That((int)data["movingAverageDays"]!["long"]!, Is.EqualTo(IndicatorCalculator.LongWindow));
    }

    [Test]
    public async Task ListNotes_EqualsRestNotes()
    {
        await using var host = await StartAsync(arrange: async db =>
        {
            db.JournalNotes.Add(new JournalNote { Date = new DateOnly(2026, 10, 9), Kind = Spydersoft.SwingTrade.Contracts.Notes.NoteKind.Pass, Text = "no candidates" });
            await db.SaveChangesAsync();
        });
        var data = (await ToolDataAsync(host, "list_notes", new { kind = "pass" }))!;
        AssertSame(data["notes"], await RestAsync(host, "/api/v1/notes?kind=Pass"));
    }

    [Test]
    public async Task GetRefreshStatus_HasLastRunTickersAndNextRun()
    {
        await using var host = await StartAsync(arrange: async db =>
        {
            db.RefreshRuns.Add(new RefreshRun { StartedAt = DateTimeOffset.UnixEpoch, Provider = "fake", Status = "Succeeded", TickersOk = 12 });
            await db.SaveChangesAsync();
        });
        var data = (await ToolDataAsync(host, "get_refresh_status"))!;
        Assert.That((string)data["lastRun"]!["status"]!, Is.EqualTo("Succeeded"));
        Assert.That(data["tickers"]!.AsArray().Count, Is.EqualTo(12));
        Assert.That(data["nextScheduled"]!["at"], Is.Not.Null);
    }

    // ---------------------------------------------------------------------------------------------- size_position fixtures

    private static async Task<JsonNode> SizeAsync(McpTestHost host, object args) =>
        (await ToolDataAsync(host, "size_position", args))!["sizing"]!;

    private static async Task AssertSizerAsync(McpTestHost host, decimal entry, decimal stop, decimal target, int shares, decimal cost, decimal risk, decimal rr, decimal maxEntry, string verdict)
    {
        var sizing = await SizeAsync(host, new { entry, stop, target });
        Assert.Multiple(() =>
        {
            Assert.That((int)sizing["shares"]!, Is.EqualTo(shares));
            Assert.That((decimal)sizing["positionCost"]!, Is.EqualTo(cost));
            Assert.That((decimal)sizing["riskDollars"]!, Is.EqualTo(risk));
            Assert.That(Math.Round((decimal)sizing["rewardRisk"]!, 3), Is.EqualTo(rr));
            Assert.That(Math.Round((decimal)sizing["maxEntryForMinRewardRisk"]!, 2), Is.EqualTo(maxEntry));
            Assert.That((string)sizing["verdict"]!, Is.EqualTo(verdict));
        });

        // The tool is a wrapper: the same request through the REST endpoint gives the same sizing.
        var client = host.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sizer")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { entry, stop, target }), System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token("r", [SwingTradeAuth.Roles.Reader]));
        var rest = JsonNode.Parse(await (await client.SendAsync(request)).Content.ReadAsStringAsync());
        AssertSame(sizing, rest);
    }

    [Test]
    public async Task SizePosition_MatchesThePlanFixtures_AndTheRestSizer()
    {
        await using var host = await StartAsync();
        await AssertSizerAsync(host, 62.82m, 61.40m, 65.90m, 15, 942.30m, 21.30m, 2.169m, 62.90m, PositionSizer.VerdictOk);
        await AssertSizerAsync(host, 769.64m, 753.50m, 773.50m, 1, 769.64m, 16.14m, 0.239m, 760.17m, PositionSizer.VerdictRewardRisk);
        await AssertSizerAsync(host, 62.34m, 61.40m, 65.90m, 16, 997.44m, 15.04m, 3.787m, 62.90m, PositionSizer.VerdictOk);
    }

    [Test]
    public async Task SizePosition_WithOnly500DollarsOfCash_SizesEightShares()
    {
        // 2000 + 53.55 realised - 1553.55 held in an open trade leaves exactly 500.
        await using var host = await StartAsync(arrange: async db =>
        {
            db.Trades.Add(new Trade
            {
                Ticker = "XLK", Status = TradeStatus.Open, Setup = TradeSetup.Other, OpenDate = Friday, Shares = 1,
                EntryPrice = 1553.55m, StopPrice = 1500m, TargetPrice = 1700m, CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch
            });
            await db.SaveChangesAsync();
        });
        var sizing = await SizeAsync(host, new { entry = 62.34m, stop = 61.40m, target = 65.90m });
        Assert.That((int)sizing["shares"]!, Is.EqualTo(8));
        Assert.That((int)sizing["sharesByCash"]!, Is.EqualTo(8));
    }

    [Test]
    public async Task SizePosition_WithTickerAndNoEntry_UsesTheLastClose()
    {
        await using var host = await StartAsync(arrange: SeedMarketAsync);
        var data = (await ToolDataAsync(host, "size_position", new { ticker = "XLE", stop = 61.40m, target = 65.90m }))!;
        Assert.That((string)data["entrySource"]!, Is.EqualTo("lastClose"));
        Assert.That((decimal)data["sizing"]!["entry"]!, Is.EqualTo(62.82m));
        Assert.That((await ToolAsync(host.CreateClient(), "size_position", new { stop = 1, target = 2 })).GetProperty("result").GetProperty("isError").GetBoolean(), Is.True);
    }

    // ---------------------------------------------------------------------------------------------- asOf, dataStatus, settings snapshot

    [Test]
    public async Task EveryReadTool_CarriesAsOfDataStatusAndTheSettingsSnapshot()
    {
        await using var host = await StartAsync(arrange: SeedMarketAsync);
        var calls = new (string Tool, object? Args)[]
        {
            ("get_dashboard", null), ("get_watchlist", null), ("get_instrument", new { ticker = "XLE" }), ("get_bars", new { ticker = "XLE" }),
            ("list_trades", null), ("get_trade", new { id = 1 }), ("size_position", new { entry = 62.82m, stop = 61.40m, target = 65.90m }),
            ("get_settings", null), ("get_refresh_status", null), ("list_notes", null), ("get_levels", new { ticker = "XLE" })
        };
        Assert.That(calls.Select(c => c.Tool), Is.EquivalentTo(ReadTools));
        foreach (var (tool, args) in calls)
        {
            var envelope = await ToolEnvelopeAsync(host, tool, args);
            Assert.That(envelope.GetProperty("asOf").GetString(), Is.EqualTo("2026-10-09"), tool);
            Assert.That(envelope.GetProperty("dataStatus").GetString(), Is.AnyOf("fresh", "stale"), tool);
            Assert.That(envelope.GetProperty("settings").GetProperty("startingCapital").GetDecimal(), Is.EqualTo(2000m), tool);
            Assert.That(envelope.TryGetProperty("data", out _), Is.True, tool);
        }
    }

    [Test]
    public async Task DataStatus_IsStaleWhenTheLatestBarIsOld_AndFreshWhenCurrent()
    {
        await using var host = await StartAsync(arrange: SeedMarketAsync);
        // Clock is Monday 2026-10-12 17:00 New York, so Monday's bar is the latest expected and Friday's is stale.
        Assert.That((await ToolEnvelopeAsync(host, "get_settings")).GetProperty("dataStatus").GetString(), Is.EqualTo("stale"));

        host.Clock.UtcNow = new DateTimeOffset(2026, 10, 10, 15, 0, 0, TimeSpan.Zero); // Saturday: Friday is the latest expected bar.
        Assert.That((await ToolEnvelopeAsync(host, "get_settings")).GetProperty("dataStatus").GetString(), Is.EqualTo("fresh"));
    }

    // ---------------------------------------------------------------------------------------------- resource and prompts

    [Test]
    public async Task RulesResource_ContainsEverySettingAndThresholdValue_AndFollowsSettingsChanges()
    {
        await using var host = await StartAsync();
        var client = host.CreateClient();

        async Task<string> ReadAsync()
        {
            var rpc = await CallAsync(client, "resources/read", new { uri = "journal://rules" });
            return rpc.GetProperty("result").GetProperty("contents")[0].GetProperty("text").GetString()!;
        }

        var text = await ReadAsync();
        AssertContainsEverySetting(text, RulesSettings.Default);

        await host.WithDbAsync(async db =>
        {
            var row = await db.Settings.SingleAsync();
            row.MaxRiskPct = 0.0125m;
            row.PullbackBandHigh = 0.0345m;
            row.ExtendedThreshold = 0.0777m;
            row.MinRewardRisk = 2.5m;
            row.StartingCapital = 3500m;
            row.WashSaleWindowDays = 45;
            await db.SaveChangesAsync();
            return 0;
        });
        var changed = await ReadAsync();
        Assert.That(changed, Is.Not.EqualTo(text));
        AssertContainsEverySetting(changed, RulesSettings.Default with
        {
            MaxRiskPct = 0.0125m, PullbackBandHigh = 0.0345m, ExtendedThreshold = 0.0777m, MinRewardRisk = 2.5m, StartingCapital = 3500m, WashSaleWindowDays = 45
        });
    }

    internal static void AssertContainsEverySetting(string text, RulesSettings s)
    {
        Assert.Multiple(() =>
        {
            foreach (var property in typeof(RulesSettings).GetProperties().Where(p => p.Name != nameof(RulesSettings.Default) && p.DeclaringType == typeof(RulesSettings)))
            {
                var value = property.GetValue(s)!;
                var shown = value is decimal d ? RulesText.Format(d) : value.ToString()!;
                Assert.That(text, Does.Contain($"{property.Name} = {shown}"), $"setting {property.Name}");
            }

            Assert.That(text, Does.Contain(RulesText.Format(s.PullbackBandLow)));
            Assert.That(text, Does.Contain(RulesText.Format(s.PullbackBandHigh)));
            Assert.That(text, Does.Contain(RulesText.Format(s.ExtendedThreshold)));
            Assert.That(text, Does.Contain(PositionSizer.VerdictOk).And.Contain(PositionSizer.VerdictRewardRisk).And.Contain(PositionSizer.VerdictCash));
            Assert.That(text, Does.Contain($"MA{IndicatorCalculator.LongWindow}"));
        });
    }

    [Test]
    public void RulesText_DoesNotHardCodeThresholdLiterals()
    {
        // A threshold that differs from every default must still appear, and no default may linger when it changes.
        var s = RulesSettings.Default with { PullbackBandLow = -0.0123m, PullbackBandHigh = 0.0456m, ExtendedThreshold = 0.0789m, MaxPositionPct = 0.31m, TaxSetAsideRate = 0.19m };
        var text = RulesText.Build(s);
        AssertContainsEverySetting(text, s);
        Assert.That(text, Does.Not.Contain("0.05 ").And.Not.Contain("-0.01 ").And.Not.Contain("0.02 "));
    }

    [Test]
    public async Task Resources_ListsRules_AndPromptsListsBoth()
    {
        await using var host = await StartAsync();
        var client = host.CreateClient();
        var resources = await CallAsync(client, "resources/list", new { });
        Assert.That(resources.GetProperty("result").GetProperty("resources").EnumerateArray().Select(r => r.GetProperty("uri").GetString()), Is.EquivalentTo(new[] { "journal://rules" }));

        var prompts = await CallAsync(client, "prompts/list", new { });
        Assert.That(prompts.GetProperty("result").GetProperty("prompts").EnumerateArray().Select(p => p.GetProperty("name").GetString()),
            Is.EquivalentTo(new[] { "weekly_review", "trade_postmortem" }));

        var review = await CallAsync(client, "prompts/get", new { name = "weekly_review" });
        var text = review.GetProperty("result").GetProperty("messages")[0].GetProperty("content").GetProperty("text").GetString()!;
        Assert.That(text, Does.Contain("get_dashboard").And.Contain("size_position").And.Contain("record a pass"));

        var post = await CallAsync(client, "prompts/get", new { name = "trade_postmortem", arguments = new { tradeId = "1" } });
        Assert.That(post.GetProperty("result").GetProperty("messages")[0].GetProperty("content").GetProperty("text").GetString(), Does.Contain("trade 1 (XLE)"));
    }

    // ---------------------------------------------------------------------------------------------- authentication and authorization

    [Test]
    public async Task NoToken_Gets401_WithResourceMetadataChallenge()
    {
        await using var host = await StartAsync();
        var response = await host.CreateClient().SendAsync(Rpc("tools/list"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(response.Headers.WwwAuthenticate.ToString(),
            Is.EqualTo("Bearer resource_metadata=\"https://swing.example.test/.well-known/oauth-protected-resource\""));
    }

    [Test]
    public async Task InvalidToken_Gets401_WithChallenge()
    {
        await using var host = await StartAsync();
        var response = await host.CreateClient().SendAsync(Rpc("tools/list", token: "not.a.jwt"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(response.Headers.WwwAuthenticate.ToString(), Does.Contain("resource_metadata=\"https://swing.example.test/.well-known/oauth-protected-resource\""));
    }

    [Test]
    public async Task WrongAudience_AndExpiredToken_Get401()
    {
        await using var host = await StartAsync();
        var client = host.CreateClient();
        Assert.That((await client.SendAsync(Rpc("tools/list", token: Token(audience: "other-api")))).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await client.SendAsync(Rpc("tools/list", token: Token(expires: DateTime.UtcNow.AddMinutes(-1))))).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task ResourceIndicatorAudience_IsAcceptedOnMcp()
    {
        await using var host = await StartAsync();
        var response = await host.CreateClient().SendAsync(Rpc("tools/list", token: Token(audience: McpTestHost.PublicUrl)));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task ResourceIndicatorAudience_IsRejectedOnRest()
    {
        await using var host = await StartAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/dashboard");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
            Token(roles: [SwingTradeAuth.Roles.Mcp, SwingTradeAuth.Roles.Reader], audience: McpTestHost.PublicUrl));
        var response = await host.CreateClient().SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [TestCase("")]
    [TestCase("swingtrade.reader")]
    [TestCase("swingtrade.admin")]
    [TestCase("swingtrade.reader,swingtrade.trader,swingtrade.admin")]
    public async Task OwnerWithoutTheMcpRole_Gets403(string roleList)
    {
        await using var host = await StartAsync();
        var response = await host.CreateClient().SendAsync(Rpc("tools/list", token: Token(roles: roleList.Split(',', StringSplitOptions.RemoveEmptyEntries))));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task McpRole_ButAnotherSubject_Gets403()
    {
        await using var host = await StartAsync();
        var response = await host.CreateClient().SendAsync(Rpc("tools/list", token: Token(sub: "someone-else")));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task OwnerWithMcpRole_Gets200()
    {
        await using var host = await StartAsync();
        var response = await host.CreateClient().SendAsync(Rpc("tools/list", token: Token()));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task TokenInTheQueryString_IsNotAccepted()
    {
        await using var host = await StartAsync();
        var client = host.CreateClient();
        var token = Token();
        foreach (var name in new[] { "access_token", "token", "bearer" })
        {
            var response = await client.SendAsync(Rpc("tools/list", path: $"/mcp?{name}={token}"));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), name);
        }
    }

    [Test]
    public async Task NoOwnerConfigured_FailsClosed_EvenForAValidMcpToken()
    {
        await using var host = await StartAsync(new() { ["Mcp:OwnerSubject"] = "" });
        var response = await host.CreateClient().SendAsync(Rpc("tools/list", token: Token()));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public void OutsideDevelopmentAndTesting_MissingOwnerOrNonHttpsUrl_FailsStartup()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => new McpOptions { PublicUrl = PublicUrl }.Validate(relaxed: false));
            Assert.Throws<InvalidOperationException>(() => new McpOptions { PublicUrl = "http://swing.example.test/mcp", OwnerSubject = "o" }.Validate(relaxed: false));
            Assert.Throws<InvalidOperationException>(() => new McpOptions { OwnerSubject = "o" }.Validate(relaxed: false));
            Assert.DoesNotThrow(() => new McpOptions { PublicUrl = PublicUrl, OwnerSubject = "o" }.Validate(relaxed: false));
            Assert.DoesNotThrow(() => new McpOptions().Validate(relaxed: true));
        });
    }

    // ---------------------------------------------------------------------------------------------- metadata, host filter, rate limit

    [TestCase("/.well-known/oauth-protected-resource")]
    [TestCase("/.well-known/oauth-protected-resource/mcp")]
    public async Task ProtectedResourceMetadata_IsPublic_AndExact(string path)
    {
        await using var host = await StartAsync();
        var response = await host.CreateClient().GetAsync(path);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Multiple(() =>
        {
            Assert.That((string)json["resource"]!, Is.EqualTo(PublicUrl));
            Assert.That(json["authorization_servers"]!.AsArray().Select(n => (string)n!), Is.EqualTo(new[] { Authority }));
            Assert.That(json["scopes_supported"]!.AsArray().Select(n => (string)n!), Is.EqualTo(new[] { SwingTradeAuth.Audience }));
            Assert.That(json["bearer_methods_supported"]!.AsArray().Select(n => (string)n!), Is.EqualTo(new[] { "header" }));
        });
    }

    [Test]
    public async Task ProtectedResourceMetadata_StripsATrailingSlashFromTheIssuer()
    {
        await using var host = await StartAsync(new() { ["Auth:Authority"] = "https://auth.example.test/" });
        var json = JsonNode.Parse(await host.CreateClient().GetStringAsync("/.well-known/oauth-protected-resource"))!;
        Assert.That((string)json["authorization_servers"]![0]!, Is.EqualTo("https://auth.example.test"));
    }

    [Test]
    public async Task McpRoutes_RejectAHostThatIsNotAllowlisted()
    {
        await using var host = await StartAsync();
        var bad = host.CreateClient("evil.example.test");
        Assert.That((await bad.SendAsync(Rpc("tools/list", token: Token()))).StatusCode, Is.EqualTo(HttpStatusCode.MisdirectedRequest));
        Assert.That((await bad.GetAsync("/.well-known/oauth-protected-resource")).StatusCode, Is.EqualTo(HttpStatusCode.MisdirectedRequest));
    }

    [Test]
    public async Task AllowedHostsConfig_OverridesThePublicUrlHost()
    {
        await using var host = await StartAsync(new() { ["Mcp:AllowedHosts:0"] = "gateway.internal" });
        Assert.That((await host.CreateClient("gateway.internal").SendAsync(Rpc("tools/list", token: Token()))).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await host.CreateClient().SendAsync(Rpc("tools/list", token: Token()))).StatusCode, Is.EqualTo(HttpStatusCode.MisdirectedRequest));
    }

    [Test]
    public async Task RestRoutes_AreNotSubjectToTheMcpHostAllowlist()
    {
        await using var host = await StartAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/settings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token("r", [SwingTradeAuth.Roles.Reader]));
        Assert.That((await host.CreateClient("internal.service").SendAsync(request)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task McpRequests_AreRateLimited()
    {
        await using var host = await StartAsync(new() { ["Mcp:RequestsPerMinute"] = "3" });
        var client = host.CreateClient();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
            codes.Add((await client.SendAsync(Rpc("tools/list", token: Token()))).StatusCode);
        Assert.That(codes, Is.EqualTo(new[] { HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests }));
    }

    [Test]
    public async Task NoCorsHeadersAreEverSent()
    {
        await using var host = await StartAsync();
        var request = Rpc("tools/list", token: Token());
        request.Headers.Add("Origin", "https://claude.ai");
        var response = await host.CreateClient().SendAsync(request);
        Assert.That(response.Headers.Contains("Access-Control-Allow-Origin"), Is.False);
    }

    // ---------------------------------------------------------------------------------------------- McpCallLog

    [Test]
    public async Task EveryCall_IsLogged_WithoutTheTokenOrAuthorizationHeader()
    {
        await using var host = await StartAsync(arrange: SeedMarketAsync);
        var token = Token();
        var client = host.CreateClient();
        await ToolAsync(client, "get_dashboard", null, token);
        await ToolAsync(client, "get_bars", new { ticker = "XLE", days = 5, token = "should-be-redacted", note = token }, token);
        await CallAsync(client, "resources/read", new { uri = "journal://rules" }, token);
        await CallAsync(client, "prompts/get", new { name = "trade_postmortem", arguments = new { tradeId = "1" } }, token);
        await ToolAsync(client, "get_trade", new { id = 12345 }, token); // an error result

        var rows = await host.WithDbAsync(db => db.McpCallLogs.AsNoTracking().OrderBy(l => l.Id).ToListAsync());
        Assert.That(rows.Select(r => r.Tool), Is.EqualTo(new[] { "get_dashboard", "get_bars", "resource:journal://rules", "prompt:trade_postmortem", "get_trade" }));
        Assert.That(rows.Select(r => r.Status), Is.EqualTo(new[] { "ok", "ok", "ok", "ok", "error" }));
        Assert.That(rows.Select(r => r.Caller).Distinct(), Is.EqualTo(new[] { Owner }));
        Assert.That(rows.All(r => r.DurationMs >= 0 && r.At == host.Clock.UtcNow), Is.True);
        Assert.That(rows[1].Arguments, Does.Contain("XLE").And.Contain("[redacted]"));

        var everything = string.Join("\n", rows.SelectMany(r => new[] { r.Tool, r.Arguments, r.Caller, r.Status }));
        Assert.That(everything, Does.Not.Contain(token));
        Assert.That(everything, Does.Not.Contain("eyJ"));
        Assert.That(everything.ToLowerInvariant(), Does.Not.Contain("bearer"));
        Assert.That(everything, Does.Not.Contain("should-be-redacted"));
    }

    [Test]
    public async Task ATokenShapedToolName_IsRedactedInTheLog()
    {
        await using var host = await StartAsync();
        var token = Token();
        await CallAsync(host.CreateClient(), "tools/call", new { name = token }, token);

        var tools = await host.WithDbAsync(db => db.McpCallLogs.AsNoTracking().Select(l => l.Tool).ToListAsync());
        Assert.That(tools, Is.Not.Empty);
        Assert.That(string.Join("\n", tools), Does.Not.Contain("eyJ"));
    }

    [Test]
    public async Task ADeniedRequest_WritesNoLogRow()
    {
        await using var host = await StartAsync();
        await host.CreateClient().SendAsync(Rpc("tools/call", new { name = "get_dashboard" }, token: Token(sub: "intruder")));
        await host.CreateClient().SendAsync(Rpc("tools/call", new { name = "get_dashboard" }));
        Assert.That(await host.WithDbAsync(db => db.McpCallLogs.CountAsync()), Is.Zero);
    }

    [Test]
    public void Sanitizer_RedactsTokenShapesAndTruncates()
    {
        var args = JsonDocument.Parse("""{"a":"Bearer abc.def.ghi","b":"eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.sig12345","apiKey":"k","c":"x","long":"%LONG%","nested":{"authorization":"zzz","ok":1}}"""
            .Replace("%LONG%", new string('y', 500))).RootElement.EnumerateObject().Select(p => new KeyValuePair<string, JsonElement>(p.Name, p.Value));
        var result = McpArgumentSanitizer.Sanitize(args)!;
        Assert.That(result, Does.Not.Contain("abc.def").And.Not.Contain("eyJ").And.Not.Contain("zzz").And.Not.Contain("\"k\""));
        Assert.That(result.Length, Is.LessThanOrEqualTo(McpArgumentSanitizer.MaxTotalLength + 3));
        Assert.That(McpArgumentSanitizer.Sanitize(null), Is.Null);
    }

    [Test]
    public async Task Retention_DeletesRowsOlderThanNinetyDays_AndKeepsTheRest()
    {
        using var h = new TestHarness();
        var now = h.Clock.UtcNow;
        h.Db.McpCallLogs.AddRange(
            new McpCallLog { At = now.AddDays(-91), Tool = "old", Status = "ok" },
            new McpCallLog { At = now.AddDays(-90).AddMinutes(-1), Tool = "just-old", Status = "ok" },
            new McpCallLog { At = now.AddDays(-89), Tool = "recent", Status = "ok" },
            new McpCallLog { At = now, Tool = "now", Status = "ok" });
        await h.Db.SaveChangesAsync();

        var removed = await McpCallLogRetentionService.PurgeAsync(h.Db, now);

        Assert.That(removed, Is.EqualTo(2));
        Assert.That(await h.Db.McpCallLogs.Select(l => l.Tool).OrderBy(t => t).ToListAsync(), Is.EqualTo(new[] { "now", "recent" }));
    }

    [Test]
    public async Task RetentionService_RunsAtStartup()
    {
        await using var host = await StartAsync(arrange: async db =>
        {
            db.McpCallLogs.Add(new McpCallLog { At = new FakeTimeProvider().UtcNow.AddDays(-200), Tool = "ancient", Status = "ok" });
            await db.SaveChangesAsync();
        });
        for (var i = 0; i < 50 && await host.WithDbAsync(db => db.McpCallLogs.AnyAsync()); i++)
            await Task.Delay(100);
        Assert.That(await host.WithDbAsync(db => db.McpCallLogs.CountAsync()), Is.Zero);
    }

    // ---------------------------------------------------------------------------------------------- REST: /api/v1/mcp/*

    [Test]
    public async Task McpRestEndpoints_FollowTheirPolicies()
    {
        await using var host = await StartAsync(new() { ["Mcp:EnableAddNote"] = "true" }, async db =>
        {
            db.McpCallLogs.AddRange(Enumerable.Range(0, 30).Select(i =>
                new McpCallLog { At = new FakeTimeProvider().UtcNow.AddMinutes(-i), Tool = $"t{i}", Status = "ok", Caller = Owner }));
            await db.SaveChangesAsync();
        });
        var client = host.CreateClient();

        async Task<HttpResponseMessage> Get(string path, string[] roles)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token("any", roles));
            return await client.SendAsync(request);
        }

        Assert.That((await Get("/api/v1/mcp/info", [SwingTradeAuth.Roles.Reader])).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await Get("/api/v1/mcp/info", [])).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await Get("/api/v1/mcp/calls", [SwingTradeAuth.Roles.Trader])).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await Get("/api/v1/mcp/calls", [SwingTradeAuth.Roles.Mcp])).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        var info = JsonNode.Parse(await (await Get("/api/v1/mcp/info", [SwingTradeAuth.Roles.Reader])).Content.ReadAsStringAsync())!;
        Assert.That((string)info["endpointUrl"]!, Is.EqualTo(PublicUrl));
        Assert.That((string)info["ownerSubject"]!, Is.EqualTo(Owner));
        Assert.That((bool)info["addNoteEnabled"]!, Is.True);

        var calls = JsonNode.Parse(await (await Get("/api/v1/mcp/calls", [SwingTradeAuth.Roles.Admin])).Content.ReadAsStringAsync())!.AsArray();
        Assert.That(calls.Count, Is.EqualTo(20));
        Assert.That((string)calls[0]!["tool"]!, Is.EqualTo("t0"));
        var five = JsonNode.Parse(await (await Get("/api/v1/mcp/calls?take=5", [SwingTradeAuth.Roles.Admin])).Content.ReadAsStringAsync())!.AsArray();
        Assert.That(five.Count, Is.EqualTo(5));
    }

    // ---------------------------------------------------------------------------------------------- routing accounted for

    [Test]
    public async Task EveryEndpoint_IsAuthorizedOrExplicitlyAnonymous_AndMcpRoutesAreAccountedFor()
    {
        await using var host = await StartAsync();
        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        var mcp = endpoints.Where(e => e.RoutePattern.RawText!.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase)).ToList();
        var wellKnown = endpoints.Where(e => e.RoutePattern.RawText!.StartsWith("/.well-known/", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.That(mcp, Is.Not.Empty, "/mcp must be mapped");
        Assert.That(wellKnown.Select(e => e.RoutePattern.RawText), Is.EquivalentTo(new[]
        {
            "/.well-known/oauth-protected-resource", "/.well-known/oauth-protected-resource/mcp"
        }));

        foreach (var e in mcp)
        {
            Assert.That(e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy), Does.Contain(SwingTradeAuth.Policies.McpOwner), e.DisplayName);
            Assert.That(e.Metadata.GetMetadata<IAllowAnonymous>(), Is.Null, e.DisplayName);
        }

        foreach (var e in wellKnown)
            Assert.That(e.Metadata.GetMetadata<IAllowAnonymous>(), Is.Not.Null, e.DisplayName);

        // Everything else is a controller action, and AuthorizationTests proves each has a known policy.
        var others = endpoints.Except(mcp).Except(wellKnown).ToList();
        Assert.That(others, Is.Not.Empty);
        foreach (var e in others)
            Assert.That(e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).FirstOrDefault(p => !string.IsNullOrEmpty(p)), Is.Not.Null, e.DisplayName);
    }
}
