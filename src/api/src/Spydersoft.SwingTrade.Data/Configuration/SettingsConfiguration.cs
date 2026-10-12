using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Spydersoft.SwingTrade.Data.Entities;

namespace Spydersoft.SwingTrade.Data.Configuration;

public class SettingsConfiguration : IEntityTypeConfiguration<Settings>
{
    public void Configure(EntityTypeBuilder<Settings> builder)
    {
        builder.ToTable("Settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.StartingCapital).HasPrecision(18, 2);
        builder.Property(s => s.MaxRiskPct).HasPrecision(9, 6);
        builder.Property(s => s.MinRewardRisk).HasPrecision(9, 4);
        builder.Property(s => s.TaxSetAsideRate).HasPrecision(9, 6);
        builder.Property(s => s.MaxPositionPct).HasPrecision(9, 6);
        builder.Property(s => s.PullbackBandLow).HasPrecision(9, 6);
        builder.Property(s => s.PullbackBandHigh).HasPrecision(9, 6);
        builder.Property(s => s.ExtendedThreshold).HasPrecision(9, 6);
        builder.Property(s => s.RiskBasis).HasConversion<string>().HasMaxLength(20);
    }
}
