using HockeyIndex.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Host = HockeyIndex.Api.Domain.Host;

namespace HockeyIndex.Api.Infrastructure.Persistence.Configurations;

internal sealed class HostConfiguration : IEntityTypeConfiguration<Host>
{
    public void Configure(EntityTypeBuilder<Host> builder)
    {
        builder.ToTable("hosts", table => table.HasCheckConstraint("ck_hosts_status", "status IN ('active','banned')"));
        builder.Property(host => host.Id).ValueGeneratedNever();
        builder.Property(host => host.UserName).IsRequired();
        builder.Property(host => host.NormalizedUserName).IsRequired();
        builder.Property(host => host.PhoneNumber).IsRequired();
        builder.Property(host => host.Email).HasColumnType("citext");
        builder.Property(host => host.NormalizedEmail).HasColumnType("citext");
        builder.Property(host => host.IsVoip).HasDefaultValue(false);
        builder.Property(host => host.IsAdmin).HasDefaultValue(false);
        builder.Property(host => host.Status).HasConversion(
            status => status == HostStatus.Banned ? "banned" : "active",
            value => value == "banned" ? HostStatus.Banned : HostStatus.Active);

        builder.HasIndex(host => host.NormalizedUserName).IsUnique().HasDatabaseName("ux_hosts_normalized_user_name");
        builder.HasIndex(host => host.NormalizedEmail).HasDatabaseName("ix_hosts_normalized_email");
        builder.HasIndex(host => host.PhoneNumber).IsUnique().HasDatabaseName("ux_hosts_phone_number");
        builder.HasIndex(host => host.NormalizedEmail, "ux_hosts_verified_email")
            .IsUnique()
            .HasFilter("email_confirmed")
            .HasDatabaseName("ux_hosts_verified_email");
        builder.HasMany<IdentityUserClaim<Guid>>().WithOne().HasForeignKey(claim => claim.UserId).HasConstraintName("fk_host_claims_hosts_user_id");
    }
}

internal sealed class HostClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder) => builder.ToTable("host_claims");
}

internal sealed class HostLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder) => builder.ToTable("host_logins");
}

internal sealed class HostTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder) => builder.ToTable("host_tokens");
}
