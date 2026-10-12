using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Spydersoft.SwingTrade.Data.Entities;

namespace Spydersoft.SwingTrade.Data.Configuration;

public class RefreshRunConfiguration : IEntityTypeConfiguration<RefreshRun>
{
    public void Configure(EntityTypeBuilder<RefreshRun> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Provider).IsRequired().HasMaxLength(50);
        builder.Property(r => r.Status).IsRequired().HasMaxLength(20);
        builder.Property(r => r.Message).HasMaxLength(2000);
        builder.HasIndex(r => r.StartedAt);
    }
}
