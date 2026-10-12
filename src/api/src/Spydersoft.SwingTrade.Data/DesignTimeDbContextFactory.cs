using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Spydersoft.SwingTrade.Data;

/// <summary>Used by <c>dotnet ef</c> only; no database connection is opened to generate a migration.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SwingTradeDbContext>
{
    public SwingTradeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SwingTradeDbContext>()
            .UseNpgsql("Host=localhost;Database=swingtrade;Username=postgres;Password=design-time-only")
            .Options;
        return new SwingTradeDbContext(options);
    }
}
