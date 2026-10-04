using HockeyIndex.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HockeyIndex.Api.Infrastructure.Persistence.Configurations;

internal sealed class ProviderUsageConfiguration : IEntityTypeConfiguration<ProviderUsage>
{
    public void Configure(EntityTypeBuilder<ProviderUsage> builder)
    {
        builder.ToTable("provider_usage");
        builder.HasKey(usage => new { usage.Provider, usage.HostId, usage.Day });
    }
}
