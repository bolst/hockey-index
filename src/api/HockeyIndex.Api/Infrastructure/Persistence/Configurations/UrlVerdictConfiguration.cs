using HockeyIndex.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HockeyIndex.Api.Infrastructure.Persistence.Configurations;

internal sealed class UrlVerdictConfiguration : IEntityTypeConfiguration<UrlVerdict>
{
    public void Configure(EntityTypeBuilder<UrlVerdict> builder)
    {
        builder.ToTable("url_verdicts");
        builder.HasKey(verdict => verdict.UrlHash);
        builder.HasIndex(verdict => verdict.ExpiresAt);
    }
}
