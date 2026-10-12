namespace Spydersoft.SwingTrade.Rules;

public sealed record DashboardTrade(
    TradeStatus Status,
    TradeSetup Setup,
    int Shares,
    decimal Entry,
    decimal Stop,
    decimal? ExitPrice,
    DateOnly? OpenDate,
    DateOnly? ExitDate,
    bool? FollowedPlan);

/// <summary>SPY close on the first trade's OpenDate and the latest SPY close.</summary>
public sealed record BenchmarkPrices(decimal SpyAtFirstTrade, decimal SpyLatest);

public sealed record SetupStats(int Trades, decimal? WinRate, decimal NetPnl);

public sealed record EquityPoint(DateOnly Date, decimal AccountValue);

public sealed record DashboardMetrics(
    decimal RealizedPnl,
    decimal AccountValue,
    decimal ReturnPct,
    decimal TaxSetAside,
    decimal AfterTaxPnl,
    int OpenCount,
    bool OpenCountOverLimit,
    decimal CapitalAtWork,
    decimal CapitalAtWorkPct,
    decimal RiskAtStops,
    decimal RiskAtStopsPct,
    decimal ReservedByPlanned,
    decimal AvailableCash,
    int ClosedCount,
    decimal? WinRate,
    decimal? AvgWin,
    decimal? AvgLoss,
    decimal? ProfitFactor,
    decimal? AvgR,
    decimal? PlanAdherence,
    decimal? AvgDaysHeld,
    IReadOnlyDictionary<TradeSetup, SetupStats> BySetup,
    decimal? SpyReturnPct,
    decimal? YouMinusSpy,
    IReadOnlyList<EquityPoint> EquityCurve);

public static class DashboardCalculator
{
    public static DashboardMetrics Compute(
        IReadOnlyCollection<DashboardTrade> trades,
        RulesSettings s,
        BenchmarkPrices? benchmark)
    {
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(s);

        var closed = trades
            .Where(t => t is { Status: TradeStatus.Closed, ExitPrice: not null })
            .Select(t => new ClosedView(t, (t.ExitPrice!.Value - t.Entry) * t.Shares))
            .ToList();
        var open = trades.Where(t => t.Status == TradeStatus.Open).ToList();
        var planned = trades.Where(t => t.Status == TradeStatus.Planned).ToList();

        var realized = closed.Sum(c => c.Pnl);
        var returnPct = realized / s.StartingCapital;
        var taxSetAside = Math.Max(0m, realized) * s.TaxSetAsideRate;

        var openCost = open.Sum(t => t.Shares * t.Entry);
        var plannedCost = planned.Sum(t => t.Shares * t.Entry);
        var riskAtStops = open.Sum(t => (t.Entry - t.Stop) * t.Shares);

        var wins = closed.Where(c => c.Pnl > 0m).ToList();
        var losses = closed.Where(c => c.Pnl < 0m).ToList();
        var sumWins = wins.Sum(c => c.Pnl);
        var sumLosses = losses.Sum(c => c.Pnl);

        var rMultiples = closed
            .Select(c => (risk: (c.Trade.Entry - c.Trade.Stop) * c.Trade.Shares, c.Pnl))
            .Where(x => x.risk > 0m)
            .Select(x => x.Pnl / x.risk)
            .ToList();
        var daysHeld = closed
            .Where(c => c.Trade.OpenDate is not null && c.Trade.ExitDate is not null)
            .Select(c => (decimal)(c.Trade.ExitDate!.Value.DayNumber - c.Trade.OpenDate!.Value.DayNumber))
            .ToList();

        decimal? spyReturn = benchmark is { SpyAtFirstTrade: > 0m } b ? b.SpyLatest / b.SpyAtFirstTrade - 1m : null;

        return new DashboardMetrics(
            RealizedPnl: realized,
            AccountValue: s.StartingCapital + realized,
            ReturnPct: returnPct,
            TaxSetAside: taxSetAside,
            AfterTaxPnl: realized - taxSetAside,
            OpenCount: open.Count,
            OpenCountOverLimit: open.Count > s.MaxOpenPositions,
            CapitalAtWork: openCost,
            CapitalAtWorkPct: openCost / s.StartingCapital,
            RiskAtStops: riskAtStops,
            RiskAtStopsPct: riskAtStops / s.StartingCapital,
            ReservedByPlanned: plannedCost,
            AvailableCash: PositionSizer.AvailableCash(s.StartingCapital, realized, openCost, plannedCost),
            ClosedCount: closed.Count,
            WinRate: Rate(wins.Count, closed.Count),
            AvgWin: wins.Count == 0 ? null : sumWins / wins.Count,
            AvgLoss: losses.Count == 0 ? null : sumLosses / losses.Count,
            ProfitFactor: losses.Count == 0 ? null : sumWins / -sumLosses,
            AvgR: rMultiples.Count == 0 ? null : rMultiples.Sum() / rMultiples.Count,
            PlanAdherence: Rate(closed.Count(c => c.Trade.FollowedPlan == true), closed.Count),
            AvgDaysHeld: daysHeld.Count == 0 ? null : daysHeld.Sum() / daysHeld.Count,
            BySetup: BySetup(closed),
            SpyReturnPct: spyReturn,
            YouMinusSpy: spyReturn is null ? null : returnPct - spyReturn,
            EquityCurve: EquityCurve(closed, s.StartingCapital));
    }

    private static decimal? Rate(int numerator, int denominator) =>
        denominator == 0 ? null : (decimal)numerator / denominator;

    private static Dictionary<TradeSetup, SetupStats> BySetup(List<ClosedView> closed) =>
        Enum.GetValues<TradeSetup>().ToDictionary(
            setup => setup,
            setup =>
            {
                var group = closed.Where(c => c.Trade.Setup == setup).ToList();
                return new SetupStats(group.Count, Rate(group.Count(c => c.Pnl > 0m), group.Count), group.Sum(c => c.Pnl));
            });

    private static List<EquityPoint> EquityCurve(List<ClosedView> closed, decimal startingCapital)
    {
        var curve = new List<EquityPoint>();
        var running = startingCapital;
        foreach (var day in closed.Where(c => c.Trade.ExitDate is not null).GroupBy(c => c.Trade.ExitDate!.Value).OrderBy(g => g.Key))
        {
            running += day.Sum(c => c.Pnl);
            curve.Add(new EquityPoint(day.Key, running));
        }

        return curve;
    }

    private sealed record ClosedView(DashboardTrade Trade, decimal Pnl);
}
