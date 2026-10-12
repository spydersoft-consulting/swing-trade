namespace Spydersoft.SwingTrade.Contracts.Refresh;

public sealed record RefreshRunDto(
    int Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string Provider,
    string Status,
    int TickersOk,
    int TickersFailed,
    string? Message);
