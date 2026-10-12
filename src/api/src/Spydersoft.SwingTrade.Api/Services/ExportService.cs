using System.Globalization;
using System.Text;
using Spydersoft.SwingTrade.Contracts.Trades;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Api.Services;

/// <summary>
/// The trade log as CSV for tax time. Values come from <see cref="TradeService"/> (so the Rules library computed every
/// P&amp;L, R and flag). Culture-invariant; RFC 4180 quoting; CRLF line ends. Money has two decimals, fractions six,
/// ratios three; prices keep two to four decimals.
/// </summary>
public class ExportService(TradeService trades, TimeProvider clock)
{
    public const string ContentType = "text/csv; charset=utf-8";

    internal static readonly string[] Columns =
    [
        "Id", "Ticker", "Status", "Setup", "OpenDate", "Shares", "EntryPrice", "StopPrice", "TargetPrice", "ExitPrice",
        "ExitDate", "Position", "RiskDollars", "RiskPct", "PlannedRewardRisk", "Pnl", "PnlPct", "RMultiple", "DaysHeld",
        "WashSaleFlag", "FollowedPlan", "Rationale", "Lessons"
    ];

    public string FileName()
    {
        var today = BarCompletion.ToNewYork(clock.GetUtcNow()).Date;
        return $"swing-trade-trades-{today.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.csv";
    }

    public async Task<string> TradesCsvAsync(CancellationToken ct = default)
    {
        var rows = (await trades.ListAsync(new TradeListQuery(), ct))
            .OrderBy(t => t.OpenDate ?? DateOnly.MaxValue).ThenBy(t => t.Id);
        return Build(rows);
    }

    internal static string Build(IEnumerable<TradeDto> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(',', Columns)).Append("\r\n");
        foreach (var t in rows)
        {
            string[] fields =
            [
                Num(t.Id), t.Ticker, t.Status.ToString(), t.Setup.ToString(), Date(t.OpenDate), Num(t.Shares),
                Price(t.EntryPrice), Price(t.StopPrice), Price(t.TargetPrice), Price(t.ExitPrice), Date(t.ExitDate),
                Fixed(t.Position, 2), Fixed(t.RiskDollars, 2), Fixed(t.RiskPct, 6), Fixed(t.PlannedRewardRisk, 3),
                Fixed(t.Pnl, 2), Fixed(t.PnlPct, 6), Fixed(t.RMultiple, 3), t.DaysHeld is { } d ? Num(d) : string.Empty,
                t.WashSaleFlag ? "true" : "false", t.FollowedPlan is { } f ? (f ? "true" : "false") : string.Empty,
                t.Rationale ?? string.Empty, t.Lessons ?? string.Empty
            ];
            sb.Append(string.Join(',', fields.Select(Escape))).Append("\r\n");
        }

        return sb.ToString();
    }

    /// <summary>RFC 4180: quote a field containing a comma, quote, CR or LF; double embedded quotes.</summary>
    internal static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) < 0 ? value : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string Num(int v) => v.ToString(CultureInfo.InvariantCulture);

    // Prices keep at least two decimals and up to four (the stored precision), whatever scale the database returned.
    private static string Price(decimal? v) => v is { } x ? x.ToString("0.00##", CultureInfo.InvariantCulture) : string.Empty;

    private static string Fixed(decimal? v, int places) =>
        v is { } x
            ? Math.Round(x, places, MidpointRounding.AwayFromZero).ToString("F" + places, CultureInfo.InvariantCulture)
            : string.Empty;

    private static string Date(DateOnly? v) => v is { } d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;
}
