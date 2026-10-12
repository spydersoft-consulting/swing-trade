using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Spydersoft.SwingTrade.Data.Entities;

namespace Spydersoft.SwingTrade.Data.Configuration;

public class IndicatorSnapshotConfiguration : IEntityTypeConfiguration<IndicatorSnapshot>
{
    public void Configure(EntityTypeBuilder<IndicatorSnapshot> builder)
    {
        builder.HasKey(s => new { s.Ticker, s.AsOfDate });
        builder.Property(s => s.Ticker).HasMaxLength(16);
        builder.Property(s => s.LastClose).HasPrecision(18, 4);
        builder.Property(s => s.Ma20).HasPrecision(18, 6);
        builder.Property(s => s.Ma50).HasPrecision(18, 6);
    }
}
