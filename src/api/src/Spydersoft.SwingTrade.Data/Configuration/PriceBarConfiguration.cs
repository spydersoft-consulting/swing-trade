using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Spydersoft.SwingTrade.Data.Entities;

namespace Spydersoft.SwingTrade.Data.Configuration;

public class PriceBarConfiguration : IEntityTypeConfiguration<PriceBar>
{
    public void Configure(EntityTypeBuilder<PriceBar> builder)
    {
        // Unique on (Ticker, Date), so the pair is the key.
        builder.HasKey(b => new { b.Ticker, b.Date });
        builder.Property(b => b.Ticker).HasMaxLength(16);
        builder.Property(b => b.Open).HasPrecision(18, 4);
        builder.Property(b => b.High).HasPrecision(18, 4);
        builder.Property(b => b.Low).HasPrecision(18, 4);
        builder.Property(b => b.Close).HasPrecision(18, 4);
    }
}
