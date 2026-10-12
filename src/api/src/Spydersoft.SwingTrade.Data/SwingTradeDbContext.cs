using Microsoft.EntityFrameworkCore;
using Spydersoft.SwingTrade.Data.Configuration;
using Spydersoft.SwingTrade.Data.Entities;

namespace Spydersoft.SwingTrade.Data;

public class SwingTradeDbContext(DbContextOptions<SwingTradeDbContext> options) : DbContext(options)
{
    public DbSet<Settings> Settings => Set<Settings>();
    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<PriceBar> PriceBars => Set<PriceBar>();
    public DbSet<IndicatorSnapshot> IndicatorSnapshots => Set<IndicatorSnapshot>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<JournalNote> JournalNotes => Set<JournalNote>();
    public DbSet<RefreshRun> RefreshRuns => Set<RefreshRun>();
    public DbSet<McpCallLog> McpCallLogs => Set<McpCallLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SettingsConfiguration());
        modelBuilder.ApplyConfiguration(new InstrumentConfiguration());
        modelBuilder.ApplyConfiguration(new PriceBarConfiguration());
        modelBuilder.ApplyConfiguration(new IndicatorSnapshotConfiguration());
        modelBuilder.ApplyConfiguration(new TradeConfiguration());
        modelBuilder.ApplyConfiguration(new JournalNoteConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshRunConfiguration());
        modelBuilder.ApplyConfiguration(new McpCallLogConfiguration());
    }
}
