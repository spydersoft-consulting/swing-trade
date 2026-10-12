using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Spydersoft.SwingTrade.Data.Entities;

namespace Spydersoft.SwingTrade.Data.Configuration;

public class InstrumentConfiguration : IEntityTypeConfiguration<Instrument>
{
    public void Configure(EntityTypeBuilder<Instrument> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Ticker).IsRequired().HasMaxLength(16);
        builder.Property(i => i.Name).IsRequired().HasMaxLength(200);
        builder.Property(i => i.Category).HasMaxLength(100);
        builder.Property(i => i.WashSaleGroup).HasMaxLength(50);
        builder.HasIndex(i => i.Ticker).IsUnique();
    }
}
