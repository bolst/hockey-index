using HockeyIndex.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HockeyIndex.Api.Infrastructure.Persistence.Configurations;

internal sealed class BlocklistEntryConfiguration : IEntityTypeConfiguration<BlocklistEntry>
{
    public void Configure(EntityTypeBuilder<BlocklistEntry> builder)
    {
        builder.ToTable("blocklist", table => table.HasCheckConstraint("ck_blocklist_kind", "kind IN ('phone','domain')"));
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Kind).HasConversion(
            kind => kind == BlocklistKind.Phone ? "phone" : "domain",
            value => value == "phone" ? BlocklistKind.Phone : BlocklistKind.Domain);
        builder.HasIndex(entry => new { entry.Kind, entry.Value }).IsUnique().HasDatabaseName("ux_blocklist_kind_value");
    }
}
