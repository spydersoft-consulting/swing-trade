using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using Spydersoft.SwingTrade.Api.Refresh;
using Spydersoft.SwingTrade.Api.Services;
using Spydersoft.SwingTrade.Contracts.Settings;
using Spydersoft.SwingTrade.Data;

namespace Spydersoft.SwingTrade.Api.Mcp;

/// <summary>
/// What every tool returns: when the data is from (<c>asOf</c>, the last completed close), whether it is current
/// (<c>dataStatus</c>), the Settings the numbers were computed under, and the same payload the REST endpoint returns.
/// </summary>
public sealed record McpEnvelope<T>(DateOnly? AsOf, string DataStatus, SettingsDto Settings, T Data);

public static class DataStatus
{
    public const string Fresh = "fresh";
    public const string Stale = "stale";

    public static string From(bool stale) => stale ? Stale : Fresh;
}

/// <summary>Builds envelopes and serialises them the way the REST API does (camelCase, string enums), so values compare equal.</summary>
public class McpEnvelopeFactory(SwingTradeDbContext db, SettingsService settings, MarketCalendar calendar)
{
    public static readonly JsonSerializerOptions Json = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    /// <summary>The newest IndicatorSnapshot date for a ticker, or across all tickers.</summary>
    public async Task<DateOnly?> AsOfAsync(string? ticker, CancellationToken ct)
    {
        var snapshots = db.IndicatorSnapshots.AsNoTracking();
        if (ticker is not null)
            snapshots = snapshots.Where(s => s.Ticker == ticker);
        return await snapshots.MaxAsync(s => (DateOnly?)s.AsOfDate, ct);
    }

    /// <param name="ticker">Scopes asOf to one instrument when the tool is about one.</param>
    /// <param name="alsoStale">Extra staleness evidence, such as a stale watchlist row.</param>
    public async Task<string> WrapAsync<T>(T data, string? ticker = null, bool alsoStale = false, CancellationToken ct = default)
    {
        var asOf = await AsOfAsync(ticker, ct);
        var snapshot = await settings.GetAsync(ct);
        var envelope = new McpEnvelope<T>(asOf, DataStatus.From(alsoStale || calendar.IsStale(asOf)), snapshot, data);
        return JsonSerializer.Serialize(envelope, Json);
    }

    /// <summary>Turns a service outcome into data, or an MCP error the model can read.</summary>
    public static T Unwrap<T>(ServiceResult<T> result, string what) => result.Kind switch
    {
        ResultKind.Ok => result.Value!,
        ResultKind.NotFound => throw new McpException($"{what} was not found."),
        ResultKind.Invalid => throw new McpException(string.Join(" ", result.Errors.SelectMany(e => e.Value))),
        _ => throw new McpException(result.Detail ?? "The request could not be completed.")
    };
}
