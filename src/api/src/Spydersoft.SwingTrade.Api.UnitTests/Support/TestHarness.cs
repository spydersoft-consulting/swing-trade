using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Spydersoft.SwingTrade.Api.Services;
using Spydersoft.SwingTrade.Data;
using Spydersoft.SwingTrade.Data.Entities;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Api.UnitTests.Support;

/// <summary>An in-memory database with the real services wired to it.</summary>
internal sealed class TestHarness : IDisposable
{
    public TestHarness()
    {
        var options = new DbContextOptionsBuilder<SwingTradeDbContext>()
            .UseInMemoryDatabase($"swingtrade-tests-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Db = new SwingTradeDbContext(options);
        Clock = new FakeTimeProvider();
        Settings = new SettingsService(Db);
        Instruments = new InstrumentService(Db);
        Trades = new TradeService(Db, Settings, Clock);
        Notes = new NoteService(Db);
        Dashboard = new DashboardService(Db, Settings);
    }

    public SwingTradeDbContext Db { get; }
    public FakeTimeProvider Clock { get; }
    public SettingsService Settings { get; }
    public InstrumentService Instruments { get; }
    public TradeService Trades { get; }
    public NoteService Notes { get; }
    public DashboardService Dashboard { get; }

    public static async Task<TestHarness> SeededAsync()
    {
        var ctx = new TestHarness();
        await DatabaseSeeder.SeedAsync(ctx.Db, ctx.Clock);
        return ctx;
    }

    /// <summary>Adds a closed trade directly (the API cannot create one in a single call).</summary>
    public async Task<Trade> AddClosedAsync(string ticker, decimal entry, decimal exit, DateOnly open, DateOnly close, int shares = 10, decimal stop = 0m)
    {
        var trade = new Trade
        {
            Ticker = ticker,
            Status = TradeStatus.Closed,
            Setup = TradeSetup.Pullback,
            OpenDate = open,
            Shares = shares,
            EntryPrice = entry,
            StopPrice = stop == 0m ? entry - 1m : stop,
            TargetPrice = entry + 5m,
            ExitPrice = exit,
            ExitDate = close,
            CreatedAt = Clock.UtcNow,
            UpdatedAt = Clock.UtcNow
        };
        Db.Trades.Add(trade);
        await Db.SaveChangesAsync();
        return trade;
    }

    public void Dispose() => Db.Dispose();
}
