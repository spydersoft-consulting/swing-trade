using Microsoft.EntityFrameworkCore;
using Spydersoft.SwingTrade.Data.Entities;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Data;

/// <summary>
/// Idempotent startup seed. Each block runs only when its table is empty, so user edits are never
/// overwritten. The sample trade is seeded only together with the instruments, so deleting every
/// trade later does not bring it back.
/// </summary>
public static class DatabaseSeeder
{
    public static readonly string[] ActiveTickers =
        ["SPY", "QQQ", "IWM", "DIA", "XLK", "XLF", "XLE", "XLV", "XLY", "XLI", "XLU", "GLD"];

    public static readonly string[] InactiveTickers =
        ["XLP", "XLB", "XLRE", "XLC", "SMH", "IBB", "KRE", "ITA", "XOP", "SPLG", "QQQM", "GLDM"];

    public static async Task SeedAsync(SwingTradeDbContext db, TimeProvider clock, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);

        if (!await db.Settings.AnyAsync(ct))
        {
            var d = RulesSettings.Default;
            db.Settings.Add(new Settings
            {
                Id = Settings.SingletonId,
                StartingCapital = d.StartingCapital,
                MaxRiskPct = d.MaxRiskPct,
                MaxOpenPositions = d.MaxOpenPositions,
                MinRewardRisk = d.MinRewardRisk,
                TaxSetAsideRate = d.TaxSetAsideRate,
                WashSaleWindowDays = d.WashSaleWindowDays,
                MaxPositionPct = d.MaxPositionPct,
                PullbackBandLow = d.PullbackBandLow,
                PullbackBandHigh = d.PullbackBandHigh,
                ExtendedThreshold = d.ExtendedThreshold,
                RiskBasis = d.RiskBasis
            });
            await db.SaveChangesAsync(ct);
        }

        if (await db.Instruments.AnyAsync(ct))
            return;

        foreach (var ticker in ActiveTickers)
            db.Instruments.Add(new Instrument { Ticker = ticker, Name = NameOf(ticker), Active = true, IsBenchmark = ticker == "SPY" });
        foreach (var ticker in InactiveTickers)
            db.Instruments.Add(new Instrument { Ticker = ticker, Name = NameOf(ticker), Active = false });

        var now = clock.GetUtcNow();
        db.Trades.Add(new Trade
        {
            Ticker = "XLE",
            Status = TradeStatus.Closed,
            Setup = TradeSetup.Pullback,
            OpenDate = new DateOnly(2026, 10, 5),
            Shares = 15,
            EntryPrice = 62.34m,
            StopPrice = 61.40m,
            TargetPrice = 65.90m,
            ExitPrice = 65.91m,
            ExitDate = new DateOnly(2026, 10, 12),
            FollowedPlan = true,
            Rationale = "Uptrend, small dip under the 20-day average; borderline practice trade",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync(ct);
    }

    private static string NameOf(string ticker) => Names.GetValueOrDefault(ticker, ticker);

    private static readonly Dictionary<string, string> Names = new()
    {
        ["SPY"] = "SPDR S&P 500 ETF Trust",
        ["QQQ"] = "Invesco QQQ Trust",
        ["IWM"] = "iShares Russell 2000 ETF",
        ["DIA"] = "SPDR Dow Jones Industrial Average ETF",
        ["XLK"] = "Technology Select Sector SPDR Fund",
        ["XLF"] = "Financial Select Sector SPDR Fund",
        ["XLE"] = "Energy Select Sector SPDR Fund",
        ["XLV"] = "Health Care Select Sector SPDR Fund",
        ["XLY"] = "Consumer Discretionary Select Sector SPDR Fund",
        ["XLI"] = "Industrial Select Sector SPDR Fund",
        ["XLU"] = "Utilities Select Sector SPDR Fund",
        ["GLD"] = "SPDR Gold Shares",
        ["XLP"] = "Consumer Staples Select Sector SPDR Fund",
        ["XLB"] = "Materials Select Sector SPDR Fund",
        ["XLRE"] = "Real Estate Select Sector SPDR Fund",
        ["XLC"] = "Communication Services Select Sector SPDR Fund",
        ["SMH"] = "VanEck Semiconductor ETF",
        ["IBB"] = "iShares Biotechnology ETF",
        ["KRE"] = "SPDR S&P Regional Banking ETF",
        ["ITA"] = "iShares U.S. Aerospace & Defense ETF",
        ["XOP"] = "SPDR S&P Oil & Gas Exploration & Production ETF",
        ["SPLG"] = "SPDR Portfolio S&P 500 ETF",
        ["QQQM"] = "Invesco NASDAQ 100 ETF",
        ["GLDM"] = "SPDR Gold MiniShares Trust"
    };
}
