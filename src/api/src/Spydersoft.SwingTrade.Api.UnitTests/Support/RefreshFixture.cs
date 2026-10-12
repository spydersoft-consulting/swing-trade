using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Spydersoft.SwingTrade.Api.Refresh;
using Spydersoft.SwingTrade.Data;
using Spydersoft.SwingTrade.Data.Entities;
using Spydersoft.SwingTrade.MarketData;

namespace Spydersoft.SwingTrade.Api.UnitTests.Support;

/// <summary>Instant delays, so retry back-off and rate-limit spacing never wait.</summary>
internal sealed class NoDelay : IDelay
{
    public Task DelayAsync(TimeSpan duration, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>A provider that always reports itself unavailable (not retryable, so tests stay fast).</summary>
internal sealed class DownProvider(string name) : IMarketDataProvider
{
    public string Name => name;
    public TimeSpan MinDelayBetweenCalls => TimeSpan.Zero;

    public Task<IReadOnlyList<DailyBar>> GetDailyBars(string ticker, DateOnly from, DateOnly to, CancellationToken ct) =>
        throw new ProviderUnavailableException($"{name} is down", retryable: false);
}

/// <summary>
/// The real refresh services over an in-memory database, the fake provider and a controllable clock.
/// Default clock: Monday 2026-10-12 21:00 UTC (17:00 New York), so Monday's bar is complete.
/// </summary>
internal sealed class RefreshFixture : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public RefreshFixture(Func<FakeMarketDataProvider, IMarketDataProvider[]>? providers = null, Action<MarketDataOptions>? configure = null)
    {
        Clock = new FakeTimeProvider();
        Fake = new FakeMarketDataProvider();

        var options = new MarketDataOptions { Providers = ["fake"] };
        configure?.Invoke(options);

        var dbName = $"refresh-tests-{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<SwingTradeDbContext>(o => o.UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(Options.Create(options));
        services.AddSingleton<IDelay, NoDelay>();
        services.AddSingleton<MarketCalendar>();
        services.AddSingleton<RefreshSchedule>();
        services.AddSingleton<RefreshCoordinator>();
        var chain = providers?.Invoke(Fake) ?? [Fake];
        services.AddScoped<IBarFetcher>(sp => new FallbackMarketDataProvider(
            chain, sp.GetRequiredService<IOptions<MarketDataOptions>>(), sp.GetRequiredService<IDelay>(),
            sp.GetRequiredService<TimeProvider>(), NullLogger<FallbackMarketDataProvider>.Instance));
        services.AddScoped<IngestionService>();
        services.AddScoped<RefreshService>();

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        Db = _scope.ServiceProvider.GetRequiredService<SwingTradeDbContext>();
        Refresh = _scope.ServiceProvider.GetRequiredService<RefreshService>();
        Ingestion = _scope.ServiceProvider.GetRequiredService<IngestionService>();
        Coordinator = _provider.GetRequiredService<RefreshCoordinator>();
    }

    public FakeTimeProvider Clock { get; }
    public FakeMarketDataProvider Fake { get; }
    public SwingTradeDbContext Db { get; }
    public RefreshService Refresh { get; }
    public IngestionService Ingestion { get; }
    public RefreshCoordinator Coordinator { get; }
    public IServiceScopeFactory ScopeFactory => _provider.GetRequiredService<IServiceScopeFactory>();

    public async Task AddInstrumentsAsync(params string[] tickers)
    {
        foreach (var t in tickers)
            Db.Instruments.Add(new Instrument { Ticker = t, Name = t, Active = true, IsBenchmark = t == "SPY" });
        await Db.SaveChangesAsync();
    }

    public void SetClock(int year, int month, int day, int hourUtc, int minuteUtc = 0) =>
        Clock.UtcNow = new DateTimeOffset(year, month, day, hourUtc, minuteUtc, 0, TimeSpan.Zero);

    /// <summary>Consecutive weekday dates ending on <paramref name="last"/>, oldest first.</summary>
    public static List<DateOnly> WeekdaysEnding(DateOnly last, int count)
    {
        var dates = new List<DateOnly>();
        for (var d = last; dates.Count < count; d = d.AddDays(-1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                dates.Add(d);
        }

        dates.Reverse();
        return dates;
    }

    public static DailyBar Bar(DateOnly date, decimal close) => new(date, close, close, close, close, 100);

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
