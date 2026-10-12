namespace Spydersoft.SwingTrade.Contracts.Dashboard;

public sealed record SetupStatsDto(int Trades, decimal? WinRate, decimal NetPnl);

public sealed record EquityPointDto(DateOnly Date, decimal AccountValue);

public sealed record DashboardDto(
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
    IReadOnlyDictionary<string, SetupStatsDto> BySetup,
    decimal? SpyReturnPct,
    decimal? YouMinusSpy,
    IReadOnlyList<EquityPointDto> EquityCurve);
