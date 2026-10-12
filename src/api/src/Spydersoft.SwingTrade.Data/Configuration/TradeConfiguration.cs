using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Spydersoft.SwingTrade.Data.Entities;

namespace Spydersoft.SwingTrade.Data.Configuration;

public class TradeConfiguration : IEntityTypeConfiguration<Trade>
{
    public void Configure(EntityTypeBuilder<Trade> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Ticker).IsRequired().HasMaxLength(16);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Setup).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.EntryPrice).HasPrecision(18, 4);
        builder.Property(t => t.StopPrice).HasPrecision(18, 4);
        builder.Property(t => t.TargetPrice).HasPrecision(18, 4);
        builder.Property(t => t.ExitPrice).HasPrecision(18, 4);
        builder.Property(t => t.Rationale).HasMaxLength(4000);
        builder.Property(t => t.Lessons).HasMaxLength(4000);

        builder.HasIndex(t => t.Status);
        builder.HasIndex(t => t.Ticker);
    }
}
