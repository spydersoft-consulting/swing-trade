using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Spydersoft.SwingTrade.Data.Entities;

namespace Spydersoft.SwingTrade.Data.Configuration;

public class McpCallLogConfiguration : IEntityTypeConfiguration<McpCallLog>
{
    public void Configure(EntityTypeBuilder<McpCallLog> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Tool).IsRequired().HasMaxLength(100);
        builder.Property(l => l.Arguments).HasMaxLength(4000);
        builder.Property(l => l.Caller).HasMaxLength(255);
        builder.Property(l => l.Status).IsRequired().HasMaxLength(20);
        builder.HasIndex(l => l.At);
    }
}
