using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Spydersoft.SwingTrade.Data.Entities;

namespace Spydersoft.SwingTrade.Data.Configuration;

public class JournalNoteConfiguration : IEntityTypeConfiguration<JournalNote>
{
    public void Configure(EntityTypeBuilder<JournalNote> builder)
    {
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Ticker).HasMaxLength(16);
        builder.Property(n => n.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(n => n.Text).IsRequired().HasMaxLength(4000);
        builder.HasIndex(n => n.Date);
    }
}
