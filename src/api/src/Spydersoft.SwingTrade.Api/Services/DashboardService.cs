using Microsoft.EntityFrameworkCore;
using Spydersoft.SwingTrade.Contracts.Dashboard;
using Spydersoft.SwingTrade.Data;
using Spydersoft.SwingTrade.Data.Entities;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Api.Services;

public class DashboardService(SwingTradeDbContext db, SettingsService settings)
{
    public async Task<DashboardDto> GetAsync(CancellationToken ct = default)
    {
        var s = await settings.GetRulesAsync(ct);
        var trades = await db.Trades.AsNoTracking().ToListAsync(ct);
        var benchmark = await LoadBenchmarkAsync(trades, ct);

        var metrics = DashboardCalculator.Compute(trades.Select(TradeMapping.ToDashboardTrade).ToList(), s, benchmark);
        return ToDto(metrics);
    }

    /// <summary>Benchmark close on (or just before) the first open date, and the latest benchmark close.</summary>
    private async Task<BenchmarkPrices?> LoadBenchmarkAsync(List<Trade> trades, CancellationToken ct)
    {
        DateOnly? firstOpen = trades
            .Where(t => t.Status is TradeStatus.Open or TradeStatus.Closed && t.OpenDate is not null)
            .Select(t => t.OpenDate!.Value)
            .Order()
            .Select(d => (DateOnly?)d)
            .FirstOrDefault();
        if (firstOpen is null)
            return null;

        var ticker = await db.Instruments.Where(i => i.IsBenchmark).Select(i => i.Ticker).FirstOrDefaultAsync(ct);
        if (ticker is null)
            return null;

        var bars = db.PriceBars.AsNoTracking().Where(b => b.Ticker == ticker);
        var atFirst = await bars.Where(b => b.Date <= firstOpen.Value).OrderByDescending(b => b.Date).FirstOrDefaultAsync(ct);
        var latest = await bars.OrderByDescending(b => b.Date).FirstOrDefaultAsync(ct);
        return atFirst is null || latest is null ? null : new BenchmarkPrices(atFirst.Close, latest.Close);
    }

    private static DashboardDto ToDto(DashboardMetrics m) => new(
        m.RealizedPnl, m.AccountValue, m.ReturnPct, m.TaxSetAside, m.AfterTaxPnl, m.OpenCount, m.OpenCountOverLimit,
        m.CapitalAtWork, m.CapitalAtWorkPct, m.RiskAtStops, m.RiskAtStopsPct, m.ReservedByPlanned, m.AvailableCash,
        m.ClosedCount, m.WinRate, m.AvgWin, m.AvgLoss, m.ProfitFactor, m.AvgR, m.PlanAdherence, m.AvgDaysHeld,
        m.BySetup.ToDictionary(e => e.Key.ToString(), e => new SetupStatsDto(e.Value.Trades, e.Value.WinRate, e.Value.NetPnl)),
        m.SpyReturnPct, m.YouMinusSpy,
        m.EquityCurve.Select(p => new EquityPointDto(p.Date, p.AccountValue)).ToList());
}
