namespace Spydersoft.SwingTrade.Contracts.Instruments;

public sealed record InstrumentDto(
    string Ticker,
    string Name,
    string? Category,
    bool Active,
    bool IsBenchmark,
    string? WashSaleGroup);
